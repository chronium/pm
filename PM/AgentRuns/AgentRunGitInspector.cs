using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PM.Application;

namespace PM.AgentRuns;

public sealed partial class AgentRunGitInspector : IAgentRunGitInspector
{
    private static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(30);

    public async Task<AppResult<AgentRunGitInspection>> Inspect(
        string projectDirectory,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            return AppResult<AgentRunGitInspection>.Fail("missing_repository", "The project repository was not found.");
        if (!TaskIdPattern().IsMatch(taskId ?? string.Empty))
            return AppResult<AgentRunGitInspection>.Fail("invalid_task", "Task ID is invalid.");

        var checks = new List<AgentRunPreflightCheck>();
        var root = await Git(projectDirectory, LocalTimeout, cancellationToken,
            "rev-parse", "--show-toplevel");
        if (!root.Success)
            return ReadyFailure(checks, "repository", "Git repository", "The project is not inside a Git repository.");
        var repositoryRoot = Text(root).Trim();
        var prefix = await Git(projectDirectory, LocalTimeout, cancellationToken,
            "rev-parse", "--show-prefix");
        if (!prefix.Success || Text(prefix).Trim().Length != 0)
            return ReadyFailure(checks, "repository", "Git repository",
                "The PM project must be located at the Git worktree root.");
        Passed(checks, "repository", "Git repository", "The PM project is at the worktree root.");

        var branchResult = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "symbolic-ref", "--quiet", "--short", "HEAD");
        if (!branchResult.Success)
            return ReadyFailure(checks, "branch", "Named branch", "HEAD must be attached to a named branch.");
        var branch = Text(branchResult).Trim();
        Passed(checks, "branch", "Named branch", $"Using branch {branch}.");

        var status = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "status", "--porcelain=v1", "--untracked-files=normal");
        if (!status.Success || status.StandardOutput.Length != 0)
            return ReadyFailure(checks, "worktree", "Clean worktree",
                "Commit or remove all tracked and untracked changes before starting a run.");
        Passed(checks, "worktree", "Clean worktree", "The worktree is clean.");

        var relativeTaskPath = $".pm/tasks/{taskId}.md";
        var relativeConfigPath = $".pm/{GlobalConfig.PmConfigFile}";
        var tracked = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "ls-files", "--error-unmatch", "--", relativeConfigPath, ".pm/project_id.txt", relativeTaskPath);
        if (!tracked.Success)
            return ReadyFailure(checks, "tracked_inputs", "Tracked PM inputs",
                "Project configuration, project identity, and the selected task must be tracked by Git.");
        Passed(checks, "tracked_inputs", "Tracked PM inputs", "Required PM inputs are tracked.");

        var remoteNameResult = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "config", "--get", $"branch.{branch}.remote");
        var mergeResult = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "config", "--get", $"branch.{branch}.merge");
        if (!remoteNameResult.Success || !mergeResult.Success)
            return ReadyFailure(checks, "upstream", "Configured upstream",
                "The current branch must have a configured remote upstream.");
        var remoteName = Text(remoteNameResult).Trim();
        var upstreamReference = Text(mergeResult).Trim();
        if (remoteName.Length == 0 || remoteName == "." || !upstreamReference.StartsWith("refs/heads/", StringComparison.Ordinal))
            return ReadyFailure(checks, "upstream", "Configured upstream",
                "The current branch must track a remote branch.");

        var remoteUrlResult = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "config", "--get", $"remote.{remoteName}.url");
        var remoteUrl = Text(remoteUrlResult).Trim();
        if (!remoteUrlResult.Success || !IsSafeRemote(remoteUrl))
            return ReadyFailure(checks, "upstream", "Configured upstream",
                "The configured upstream must use a credential-free HTTPS or SSH remote.");
        Passed(checks, "upstream", "Configured upstream", $"Using {remoteName}/{upstreamReference["refs/heads/".Length..]}.");

        var head = await Git(repositoryRoot, LocalTimeout, cancellationToken, "rev-parse", "HEAD");
        if (!head.Success || !CommitPattern().IsMatch(Text(head).Trim()))
            return ReadyFailure(checks, "base_commit", "Committed base", "The current commit could not be resolved.");
        var headCommit = Text(head).Trim();

        var live = await Git(repositoryRoot, RemoteTimeout, cancellationToken,
            "ls-remote", "--exit-code", remoteName, upstreamReference);
        var liveLine = Text(live).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var upstreamCommit = liveLine?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!live.Success || upstreamCommit == null || !CommitPattern().IsMatch(upstreamCommit))
            return ReadyFailure(checks, "remote_reachable", "Remote reachability",
                "The configured upstream could not be reached.");
        Passed(checks, "remote_reachable", "Remote reachability", "The configured upstream is reachable.");

        var objectExists = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "cat-file", "-e", $"{upstreamCommit}^{{commit}}");
        if (!objectExists.Success)
        {
            var fetched = await Git(repositoryRoot, RemoteTimeout, cancellationToken,
                "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", remoteName, upstreamReference);
            if (!fetched.Success)
                return ReadyFailure(checks, "base_available", "Remote base availability",
                    "The upstream commit could not be made available locally.");
        }

        var ancestor = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "merge-base", "--is-ancestor", headCommit, upstreamCommit);
        if (!ancestor.Success)
            return ReadyFailure(checks, "base_published", "Published base",
                "The current commit is not published to the configured upstream.");
        Passed(checks, "base_published", "Published base", "The exact base commit is available upstream.");

        var task = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "show", $"{headCommit}:{relativeTaskPath}");
        if (!task.Success)
            return ReadyFailure(checks, "task_revision", "Committed task revision",
                "The selected task could not be read from the base commit.");
        var taskRevision = Convert.ToHexString(SHA256.HashData(task.StandardOutput)).ToLowerInvariant();
        Passed(checks, "task_revision", "Committed task revision", "The task revision was captured from Git.");

        return AppResult<AgentRunGitInspection>.Ok(new AgentRunGitInspection(
            new AgentRunGitSnapshot(repositoryRoot, branch, remoteName, remoteUrl,
                upstreamReference, headCommit, taskRevision), checks));
    }

    private static AppResult<AgentRunGitInspection> ReadyFailure(
        List<AgentRunPreflightCheck> checks,
        string id,
        string label,
        string summary)
    {
        checks.Add(new AgentRunPreflightCheck(id, label, AgentRunPreflightCheckStatus.Failed, summary));
        return AppResult<AgentRunGitInspection>.Ok(new AgentRunGitInspection(null, checks));
    }

    private static void Passed(List<AgentRunPreflightCheck> checks, string id, string label, string summary) =>
        checks.Add(new AgentRunPreflightCheck(id, label, AgentRunPreflightCheckStatus.Passed, summary));

    private static bool IsSafeRemote(string value)
    {
        if (value.StartsWith("git@", StringComparison.Ordinal) && value.Contains(':')) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme is "https" or "ssh" && string.IsNullOrEmpty(uri.UserInfo) && !uri.IsLoopback;
    }

    private static string Text(GitResult result) => Encoding.UTF8.GetString(result.StandardOutput);

    private static async Task<GitResult> Git(
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_SSH_COMMAND"] = "ssh -oBatchMode=yes";

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return new GitResult(false, []);
            var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var discard = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                await copy;
                await discard;
                return new GitResult(process.ExitCode == 0, output.ToArray());
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                return new GitResult(false, []);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new GitResult(false, []);
        }
    }

    private sealed record GitResult(bool Success, byte[] StandardOutput);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskIdPattern();

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}
