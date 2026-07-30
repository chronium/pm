using System.ComponentModel;
using System.Diagnostics;
using PM.Project;

namespace PM.Application;

public enum LinkedProjectResolutionStatus
{
    Available,
    Unregistered,
    Missing,
    Invalid,
    IdentityMismatch,
    StaleBinding,
    UninitializedSubmodule,
}

public enum LinkedProjectResolutionSource
{
    None,
    ActiveProject,
    Registry,
    PathHint,
}

public sealed record LinkedProjectResolutionDiagnostic(string Code, string Message);

public sealed record LinkedProjectRepairAction(
    string Command,
    IReadOnlyList<string> Arguments,
    string DisplayCommand);

public sealed record LinkedProjectResolution(
    LinkedProjectDeclaration Declaration,
    LinkedProjectResolutionStatus Status,
    LinkedProjectResolutionSource Source,
    ProjectRoot? Project,
    string? RepositoryPath,
    bool WriteTrusted,
    IReadOnlyList<LinkedProjectResolutionDiagnostic> Diagnostics,
    LinkedProjectRepairAction? RepairAction = null);

public interface ILinkedProjectSubmoduleInspector
{
    Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
        string repositoryPath,
        string pathHint,
        CancellationToken cancellationToken = default);
}

public sealed class GitLinkedProjectSubmoduleInspector : ILinkedProjectSubmoduleInspector
{
    public async Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
        string repositoryPath,
        string pathHint,
        CancellationToken cancellationToken = default)
    {
        var gitModulesPath = Path.Combine(repositoryPath, ".gitmodules");
        if (!File.Exists(gitModulesPath))
            return AppResult<LinkedProjectRepairAction?>.Ok(null);

        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("config");
        startInfo.ArgumentList.Add("--null");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(gitModulesPath);
        startInfo.ArgumentList.Add("--get-regexp");
        startInfo.ArgumentList.Add("^submodule\\..*\\.path$");

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return AppResult<LinkedProjectRepairAction?>.Fail(
                    "submodule_inspection_failed", "Git could not be started to inspect submodules.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdoutTask;
            _ = await stderrTask;

            if (process.ExitCode != 0)
                return AppResult<LinkedProjectRepairAction?>.Ok(null);

            var normalizedHint = NormalizeSubmodulePath(pathHint);
            var declared = output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.IndexOf('\n') is var separator && separator >= 0
                    ? entry[(separator + 1)..]
                    : string.Empty)
                .Any(value => string.Equals(NormalizeSubmodulePath(value), normalizedHint,
                                  OperatingSystem.IsWindows()
                                      ? StringComparison.OrdinalIgnoreCase
                                      : StringComparison.Ordinal));
            if (!declared)
                return AppResult<LinkedProjectRepairAction?>.Ok(null);

            string[] arguments = ["submodule", "update", "--init", "--", pathHint];
            return AppResult<LinkedProjectRepairAction?>.Ok(new LinkedProjectRepairAction(
                "git",
                arguments,
                FormatDisplayCommand("git", arguments)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AppResult<LinkedProjectRepairAction?>.Fail(
                "submodule_inspection_timeout", "Git submodule inspection timed out.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return AppResult<LinkedProjectRepairAction?>.Fail(
                "submodule_inspection_failed", "Git submodule inspection failed.");
        }
    }

    public static string FormatDisplayCommand(string command, IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { command }.Concat(arguments).Select(QuoteForDisplay));

    private static string NormalizeSubmodulePath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/').TrimEnd('/');
        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    private static string QuoteForDisplay(string value)
    {
        if (value.Length > 0 && value.All(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '/' or ':'))
            return value;

        if (OperatingSystem.IsWindows())
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        return $"'{value.Replace("'", "'\\''")}'";
    }
}

public sealed class LinkedProjectResolver(
    LinkedProjectRegistryStore registry,
    ILinkedProjectSubmoduleInspector submoduleInspector)
{
    public async Task<LinkedProjectResolution> ResolveAsync(
        ProjectRoot activeProject,
        LinkedProjectDeclaration declaration,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<LinkedProjectResolutionDiagnostic>();
        if (activeProject.TryReadProjectId(out var activeProjectId) &&
            string.Equals(activeProjectId, declaration.ProjectId, StringComparison.Ordinal))
            return Available(declaration, activeProject, LinkedProjectResolutionSource.ActiveProject, false, diagnostics);

        var registryBinding = registry.Get(declaration.ProjectId);
        CandidateFailure? registryFailure = null;
        if (registryBinding.Success)
        {
            var candidate = VerifyCandidate(declaration.ProjectId, registryBinding.Payload!.RepositoryPath);
            if (candidate.Success)
                return Available(declaration, candidate.Project!, LinkedProjectResolutionSource.Registry,
                    registryBinding.Payload.WriteTrusted, diagnostics);

            registryFailure = candidate;
            diagnostics.Add(new LinkedProjectResolutionDiagnostic(
                "stale_project_binding",
                $"The registered repository for {declaration.ProjectId} is no longer valid: {candidate.Message}"));
        }
        else if (registryBinding.ErrorCode is not "project_not_registered")
        {
            diagnostics.Add(new LinkedProjectResolutionDiagnostic(
                registryBinding.ErrorCode ?? "project_registry_unavailable",
                registryBinding.Message ?? "The local project registry could not be read."));
        }

        CandidateFailure? pathFailure = null;
        if (declaration.PathHint != null)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(activeProject.RepositoryPath, declaration.PathHint));
            var candidate = VerifyCandidate(declaration.ProjectId, candidatePath);
            if (candidate.Success)
            {
                var remembered = registry.Remember(candidate.Project!);
                if (!remembered.Success)
                    diagnostics.Add(new LinkedProjectResolutionDiagnostic(
                        remembered.ErrorCode ?? "project_registry_write_failed",
                        remembered.Message ?? "The valid path hint could not be remembered."));
                return Available(declaration, candidate.Project!, LinkedProjectResolutionSource.PathHint, false,
                    diagnostics);
            }

            pathFailure = candidate;
            diagnostics.Add(new LinkedProjectResolutionDiagnostic(candidate.Code, candidate.Message));

            var submodule = await submoduleInspector.InspectAsync(
                activeProject.RepositoryPath, declaration.PathHint, cancellationToken);
            if (submodule.Success && submodule.Payload != null)
                return Unavailable(declaration, LinkedProjectResolutionStatus.UninitializedSubmodule,
                    candidatePath, diagnostics, submodule.Payload);
            if (!submodule.Success)
                diagnostics.Add(new LinkedProjectResolutionDiagnostic(
                    submodule.ErrorCode ?? "submodule_inspection_failed",
                    submodule.Message ?? "Submodule state could not be inspected."));
        }

        var decisiveFailure = pathFailure ?? registryFailure;
        if (decisiveFailure != null)
        {
            var status = registryFailure != null && pathFailure == null &&
                         decisiveFailure.Status == LinkedProjectResolutionStatus.Missing
                ? LinkedProjectResolutionStatus.StaleBinding
                : decisiveFailure.Status;
            return Unavailable(declaration, status, decisiveFailure.RepositoryPath, diagnostics);
        }

        return Unavailable(declaration, LinkedProjectResolutionStatus.Unregistered, null, diagnostics);
    }

    private static CandidateFailure VerifyCandidate(string expectedProjectId, string repositoryPath)
    {
        var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        if (!Directory.Exists(canonicalPath))
            return CandidateFailure.Fail(LinkedProjectResolutionStatus.Missing, "missing_project_path",
                "The repository path does not exist.", canonicalPath);
        if (!Directory.Exists(Path.Combine(canonicalPath, GlobalConfig.PmDirName)))
            return CandidateFailure.Fail(LinkedProjectResolutionStatus.Invalid, "uninitialized_project",
                "The repository is not initialized as a PM project.", canonicalPath);
        if (!ProjectRoot.TryOpenExact(canonicalPath, out var project))
            return CandidateFailure.Fail(LinkedProjectResolutionStatus.Invalid, "invalid_project",
                "The PM project could not be opened.", canonicalPath);
        if (!project.TryReadProjectId(out var actualProjectId))
            return CandidateFailure.Fail(LinkedProjectResolutionStatus.Invalid, "invalid_project_id",
                "The PM project has no valid stable project ID.", canonicalPath);
        if (!string.Equals(actualProjectId, expectedProjectId, StringComparison.Ordinal))
            return CandidateFailure.Fail(LinkedProjectResolutionStatus.IdentityMismatch, "project_identity_mismatch",
                $"The repository identifies as {actualProjectId}, not {expectedProjectId}.", canonicalPath);

        return CandidateFailure.Ok(project, canonicalPath);
    }

    private static LinkedProjectResolution Available(
        LinkedProjectDeclaration declaration,
        ProjectRoot project,
        LinkedProjectResolutionSource source,
        bool writeTrusted,
        IReadOnlyList<LinkedProjectResolutionDiagnostic> diagnostics) =>
        new(declaration, LinkedProjectResolutionStatus.Available, source, project, project.RepositoryPath,
            writeTrusted, diagnostics);

    private static LinkedProjectResolution Unavailable(
        LinkedProjectDeclaration declaration,
        LinkedProjectResolutionStatus status,
        string? repositoryPath,
        IReadOnlyList<LinkedProjectResolutionDiagnostic> diagnostics,
        LinkedProjectRepairAction? repairAction = null) =>
        new(declaration, status, LinkedProjectResolutionSource.None, null, repositoryPath, false,
            diagnostics, repairAction);

    private sealed record CandidateFailure(
        bool Success,
        LinkedProjectResolutionStatus Status,
        string Code,
        string Message,
        string RepositoryPath,
        ProjectRoot? Project = null)
    {
        public static CandidateFailure Ok(ProjectRoot project, string repositoryPath) =>
            new(true, LinkedProjectResolutionStatus.Available, string.Empty, string.Empty, repositoryPath, project);

        public static CandidateFailure Fail(
            LinkedProjectResolutionStatus status,
            string code,
            string message,
            string repositoryPath) =>
            new(false, status, code, message, repositoryPath);
    }
}
