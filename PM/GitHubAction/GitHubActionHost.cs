using System.Diagnostics;
using PM.Project;

namespace PM.GitHubAction;

internal static class GitHubActionHost
{
    internal const string CommandName = "__github-action";

    internal static Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken) =>
        new GitHubActionDispatcher(
                new PmActionProcessRunner(typeof(GitHubActionHost).Assembly.Location),
                Console.Out,
                Console.Error)
            .RunAsync(arguments, GitHubActionEnvironment.FromProcess(), cancellationToken);
}

internal sealed record GitHubActionEnvironment(
    string? Workspace,
    string? OutputFile,
    string? StepSummaryFile)
{
    internal static GitHubActionEnvironment FromProcess() => new(
        Environment.GetEnvironmentVariable("GITHUB_WORKSPACE"),
        Environment.GetEnvironmentVariable("GITHUB_OUTPUT"),
        Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY"));
}

internal sealed class GitHubActionDispatcher(
    IPmActionProcessRunner processRunner,
    TextWriter output,
    TextWriter error)
{
    internal async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        GitHubActionEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var request = GitHubActionRequest.TryCreate(arguments);
        if (!request.Success) return Fail(request.Message!);
        if (string.IsNullOrWhiteSpace(environment.OutputFile))
            return Fail("GITHUB_OUTPUT is required by the PM GitHub Action.");
        if (string.IsNullOrWhiteSpace(environment.StepSummaryFile))
            return Fail("GITHUB_STEP_SUMMARY is required by the PM GitHub Action.");

        var context = GitHubActionPathContext.TryCreate(environment, request.Payload!);
        if (!context.Success) return Fail(context.Message!);

        var invocation = BuildInvocation(request.Payload!, context.Payload!);
        if (request.Payload!.Command == GitHubActionCommand.Version)
        {
            var version = await processRunner.CaptureAsync(invocation, context.Payload!.WorkingDirectory,
                cancellationToken);
            await WriteCapturedAsync(version);
            if (version.ExitCode != 0) return version.ExitCode;
            return await CompleteAsync(request.Payload!, context.Payload!, version.StandardOutput, environment,
                cancellationToken);
        }

        var exitCode = await processRunner.RunAsync(invocation, context.Payload!.WorkingDirectory, cancellationToken);
        if (exitCode != 0)
        {
            await TryAppendSummaryAsync(environment.StepSummaryFile,
                $"PM `{request.Payload.CommandValue}` failed with exit code {exitCode}.", cancellationToken);
            return exitCode;
        }

        var packagedVersion = await processRunner.CaptureAsync(["--version"], context.Payload.WorkingDirectory,
            cancellationToken);
        if (packagedVersion.ExitCode != 0)
        {
            await WriteCapturedAsync(packagedVersion);
            return packagedVersion.ExitCode;
        }

        return await CompleteAsync(request.Payload, context.Payload, packagedVersion.StandardOutput, environment,
            cancellationToken);
    }

    private async Task<int> CompleteAsync(
        GitHubActionRequest request,
        GitHubActionPathContext context,
        string versionOutput,
        GitHubActionEnvironment environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = versionOutput.Trim();
        if (string.IsNullOrWhiteSpace(version) || version.Any(char.IsControl))
            return Fail("The packaged PM runtime returned an invalid version.");

        try
        {
            var sitePath = request.Command == GitHubActionCommand.SiteBuild
                ? context.SitePath!
                : string.Empty;
            await File.AppendAllTextAsync(environment.OutputFile!,
                $"pm-version={version}\nsite-path={sitePath}\n", cancellationToken);
            var detail = request.Command == GitHubActionCommand.SiteBuild
                ? $" Site output: `{sitePath}`."
                : string.Empty;
            await AppendSummaryAsync(environment.StepSummaryFile!,
                $"PM `{request.CommandValue}` completed with Project Model {version}.{detail}", cancellationToken);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"Unable to write GitHub Action outputs: {exception.Message}");
        }
    }

    private static IReadOnlyList<string> BuildInvocation(
        GitHubActionRequest request,
        GitHubActionPathContext context) => request.Command switch
    {
        GitHubActionCommand.Doctor => ["doctor"],
        GitHubActionCommand.Version => ["--version"],
        GitHubActionCommand.SiteBuild when request.Force =>
            ["site", "build", "--output", context.OutputDirectory!, "--force"],
        GitHubActionCommand.SiteBuild => ["site", "build", "--output", context.OutputDirectory!],
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    private async Task WriteCapturedAsync(PmActionProcessResult result)
    {
        if (result.StandardOutput.Length > 0) await output.WriteAsync(result.StandardOutput);
        if (result.StandardError.Length > 0) await error.WriteAsync(result.StandardError);
    }

    private static Task AppendSummaryAsync(
        string path,
        string message,
        CancellationToken cancellationToken = default) =>
        File.AppendAllTextAsync(path, $"### Project Model\n\n{message}\n", cancellationToken);

    private static async Task TryAppendSummaryAsync(
        string path,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await AppendSummaryAsync(path, message, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // PM's diagnostic and exit status remain authoritative when summary writing fails.
        }
    }

    private int Fail(string message)
    {
        error.WriteLine($"PM GitHub Action: {message}");
        return 1;
    }
}

internal enum GitHubActionCommand
{
    Doctor,
    SiteBuild,
    Version,
}

internal sealed record GitHubActionRequest(
    GitHubActionCommand Command,
    string CommandValue,
    string WorkingDirectory,
    string OutputDirectory,
    bool Force)
{
    internal static ActionResult<GitHubActionRequest> TryCreate(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 4)
            return ActionResult<GitHubActionRequest>.Fail(
                "Expected exactly command, working-directory, output-directory, and force arguments.");

        var command = arguments[0] switch
        {
            "doctor" => GitHubActionCommand.Doctor,
            "site-build" => GitHubActionCommand.SiteBuild,
            "version" => GitHubActionCommand.Version,
            _ => (GitHubActionCommand?)null,
        };
        if (command == null)
            return ActionResult<GitHubActionRequest>.Fail(
                "command must be exactly doctor, site-build, or version.");

        var force = arguments[3] switch
        {
            "true" => true,
            "false" => false,
            _ => (bool?)null,
        };
        if (force == null)
            return ActionResult<GitHubActionRequest>.Fail("force must be exactly true or false.");
        if (force.Value && command != GitHubActionCommand.SiteBuild)
            return ActionResult<GitHubActionRequest>.Fail("force may be true only for site-build.");

        return ActionResult<GitHubActionRequest>.Ok(new GitHubActionRequest(
            command.Value,
            arguments[0],
            arguments[1],
            arguments[2],
            force.Value));
    }
}

internal sealed record GitHubActionPathContext(
    string Workspace,
    string WorkingDirectory,
    string? ProjectDirectory,
    string? OutputDirectory,
    string? SitePath)
{
    internal static ActionResult<GitHubActionPathContext> TryCreate(
        GitHubActionEnvironment environment,
        GitHubActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(environment.Workspace))
            return ActionResult<GitHubActionPathContext>.Fail(
                "GITHUB_WORKSPACE must identify the checked-out workspace.");

        var workspace = ActionPathResolver.TryResolveExistingAbsoluteDirectory(environment.Workspace);
        if (!workspace.Success)
            return ActionResult<GitHubActionPathContext>.Fail($"Invalid GITHUB_WORKSPACE: {workspace.Message}");

        var working = ActionPathResolver.TryResolveExistingRelativeDirectory(
            workspace.Payload!, request.WorkingDirectory, "working-directory");
        if (!working.Success) return ActionResult<GitHubActionPathContext>.Fail(working.Message!);

        string? projectDirectory = null;
        string? pmDirectory = null;
        if (request.Command is GitHubActionCommand.Doctor or GitHubActionCommand.SiteBuild)
        {
            projectDirectory = FindProjectDirectory(workspace.Payload!, working.Payload!);
            if (projectDirectory == null)
                return ActionResult<GitHubActionPathContext>.Fail(
                    "No PM project was found from working-directory within GITHUB_WORKSPACE.");

            var resolvedPmDirectory = ActionPathResolver.TryResolveExistingAbsoluteDirectory(
                Path.Combine(projectDirectory, GlobalConfig.PmDirName));
            if (!resolvedPmDirectory.Success ||
                !ActionPathResolver.IsWithin(workspace.Payload!, resolvedPmDirectory.Payload!) ||
                !ActionPathResolver.IsWithin(projectDirectory, resolvedPmDirectory.Payload!))
                return ActionResult<GitHubActionPathContext>.Fail(
                    "The discovered project's .pm directory must remain within its workspace project root.");
            pmDirectory = resolvedPmDirectory.Payload;
        }

        if (request.Command != GitHubActionCommand.SiteBuild)
            return ActionResult<GitHubActionPathContext>.Ok(new GitHubActionPathContext(
                workspace.Payload!, working.Payload!, projectDirectory, null, null));

        var destination = ActionPathResolver.TryResolveRelativeDestination(
            workspace.Payload!, request.OutputDirectory, "output-directory");
        if (!destination.Success) return ActionResult<GitHubActionPathContext>.Fail(destination.Message!);

        var outputDirectory = destination.Payload!;
        if (ActionPathResolver.SamePath(outputDirectory, workspace.Payload!))
            return ActionResult<GitHubActionPathContext>.Fail("output-directory cannot be the workspace root.");
        if (ActionPathResolver.SamePath(outputDirectory, working.Payload!))
            return ActionResult<GitHubActionPathContext>.Fail("output-directory cannot be working-directory.");
        if (ActionPathResolver.SamePath(outputDirectory, projectDirectory!))
            return ActionResult<GitHubActionPathContext>.Fail("output-directory cannot be the project root.");
        if (ActionPathResolver.IsWithin(outputDirectory, projectDirectory!))
            return ActionResult<GitHubActionPathContext>.Fail("output-directory cannot be an ancestor of the project.");

        if (ActionPathResolver.IsWithin(pmDirectory!, outputDirectory))
            return ActionResult<GitHubActionPathContext>.Fail("output-directory cannot be .pm or its descendants.");

        var sitePath = Path.GetRelativePath(workspace.Payload!, outputDirectory)
            .Replace(Path.DirectorySeparatorChar, '/');
        return ActionResult<GitHubActionPathContext>.Ok(new GitHubActionPathContext(
            workspace.Payload!, working.Payload!, projectDirectory, outputDirectory, sitePath));
    }

    private static string? FindProjectDirectory(string workspace, string workingDirectory)
    {
        var current = workingDirectory;
        while (ActionPathResolver.IsWithin(workspace, current))
        {
            if (ProjectRoot.TryOpenExact(current, out _)) return current;
            if (ActionPathResolver.SamePath(current, workspace)) break;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return null;
    }
}

internal static class ActionPathResolver
{
    internal static ActionResult<string> TryResolveExistingAbsoluteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || !Path.IsPathFullyQualified(path))
            return ActionResult<string>.Fail("The path must be an absolute directory without control characters.");

        try
        {
            return ResolveExistingDirectory(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ActionResult<string>.Fail(exception.Message);
        }
    }

    internal static ActionResult<string> TryResolveExistingRelativeDirectory(
        string workspace,
        string path,
        string inputName)
    {
        var safe = ValidateRelativeInput(path, inputName);
        if (!safe.Success) return ActionResult<string>.Fail(safe.Message!);

        try
        {
            var resolved = ResolveFromWorkspace(workspace, path, requireExisting: true);
            if (!resolved.Success) return resolved;
            if (!IsWithin(workspace, resolved.Payload!))
                return ActionResult<string>.Fail($"{inputName} resolves outside GITHUB_WORKSPACE.");
            return resolved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ActionResult<string>.Fail($"Invalid {inputName}: {exception.Message}");
        }
    }

    internal static ActionResult<string> TryResolveRelativeDestination(
        string workspace,
        string path,
        string inputName)
    {
        var safe = ValidateRelativeInput(path, inputName);
        if (!safe.Success) return ActionResult<string>.Fail(safe.Message!);

        try
        {
            var resolved = ResolveFromWorkspace(workspace, path, requireExisting: false);
            if (!resolved.Success) return resolved;
            if (!IsWithin(workspace, resolved.Payload!))
                return ActionResult<string>.Fail($"{inputName} resolves outside GITHUB_WORKSPACE.");
            return resolved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ActionResult<string>.Fail($"Invalid {inputName}: {exception.Message}");
        }
    }

    internal static bool IsWithin(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = Normalize(root);
        var normalizedCandidate = Normalize(candidate);
        return normalizedCandidate.Equals(normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    internal static bool SamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static ActionResult ValidateRelativeInput(string path, string inputName)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ActionResult.Fail($"{inputName} must be a non-empty workspace-relative path.");
        if (path.Any(char.IsControl) || path.Contains('\\') || Path.IsPathFullyQualified(path))
            return ActionResult.Fail($"{inputName} must be a workspace-relative path without control characters.");
        if (path.Split('/').Any(segment => segment == ".."))
            return ActionResult.Fail($"{inputName} cannot contain parent traversal segments.");
        return ActionResult.Ok();
    }

    private static ActionResult<string> ResolveFromWorkspace(string workspace, string relative, bool requireExisting)
    {
        var current = Normalize(workspace);
        var missing = false;
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            current = Path.Combine(current, segment);
            if (missing) continue;

            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget != null)
            {
                var target = directory.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not DirectoryInfo || !target.Exists)
                    return ActionResult<string>.Fail("The path contains a broken or non-directory symbolic link.");
                current = Normalize(target.FullName);
                continue;
            }

            if (directory.Exists) continue;
            if (File.Exists(current))
                return ActionResult<string>.Fail("The path contains a file where a directory is required.");
            if (requireExisting)
                return ActionResult<string>.Fail("The selected directory does not exist.");
            missing = true;
        }

        return ActionResult<string>.Ok(Normalize(current));
    }

    private static ActionResult<string> ResolveExistingDirectory(string absolutePath)
    {
        var root = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrEmpty(root)) return ActionResult<string>.Fail("The directory path has no root.");

        var current = root;
        var relative = Path.GetRelativePath(root, absolutePath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget != null)
            {
                var target = directory.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not DirectoryInfo || !target.Exists)
                    return ActionResult<string>.Fail("The path contains a broken or non-directory symbolic link.");
                current = Normalize(target.FullName);
                continue;
            }

            if (!directory.Exists)
                return ActionResult<string>.Fail("The selected directory does not exist.");
        }

        return ActionResult<string>.Ok(Normalize(current));
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

internal interface IPmActionProcessRunner
{
    Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);

    Task<PmActionProcessResult> CaptureAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}

internal sealed record PmActionProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class PmActionProcessRunner(string assemblyPath) : IPmActionProcessRunner
{
    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = Start(arguments, workingDirectory, redirect: false);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public async Task<PmActionProcessResult> CaptureAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = Start(arguments, workingDirectory, redirect: true);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new PmActionProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private Process Start(IReadOnlyList<string> arguments, string workingDirectory, bool redirect)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
        };
        start.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        return Process.Start(start) ?? throw new InvalidOperationException("Unable to launch the PM runtime.");
    }
}

internal sealed record ActionResult<T>(bool Success, string? Message = null, T? Payload = default)
{
    internal static ActionResult<T> Ok(T payload) => new(true, Payload: payload);
    internal static ActionResult<T> Fail(string message) => new(false, message);
}

internal sealed record ActionResult(bool Success, string? Message = null)
{
    internal static ActionResult Ok() => new(true);
    internal static ActionResult Fail(string message) => new(false, message);
}
