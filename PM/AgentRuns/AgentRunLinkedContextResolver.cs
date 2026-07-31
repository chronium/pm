using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PM.Application;

namespace PM.AgentRuns;

public sealed partial class AgentRunLinkedContextResolver(
    LinkedProjectFamilyService familyService) : IAgentRunLinkedContextResolver
{
    private static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(30);

    public async Task<AppResult<AgentRunLinkedContextResolution>> Resolve(
        IReadOnlyList<AgentRunLinkedContextSelection> selections,
        CancellationToken cancellationToken = default)
    {
        if (selections.Count == 0)
            return AppResult<AgentRunLinkedContextResolution>.Ok(
                new AgentRunLinkedContextResolution([], [], true));

        var family = await familyService.ResolveAsync(cancellationToken);
        if (!family.Success)
            return AppResult<AgentRunLinkedContextResolution>.Fail(family.ErrorCode!, family.Message!);

        var checks = new List<AgentRunPreflightCheck>();
        var contexts = new List<AgentRunLinkedContext>();
        var requiredUnavailable = false;
        foreach (var selection in selections.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            var member = family.Payload!.Members.SingleOrDefault(item =>
                string.Equals(item.ProjectId, selection.ProjectId, StringComparison.Ordinal));
            var checkId = $"linked_context_{selection.ProjectId}";
            if (member == null || member.Relationship == LinkedProjectRelationship.Current ||
                !member.Readable || member.RepositoryPath == null || member.Alias == null)
            {
                AddUnavailable(checks, selection, checkId,
                    $"Linked wiki context {selection.ProjectId} is not available in the active family.");
                requiredUnavailable |= selection.Requirement == AgentRunLinkedContextRequirement.Required;
                continue;
            }

            var snapshot = await InspectPublishedRepository(member.RepositoryPath, cancellationToken);
            if (!snapshot.Success)
            {
                AddUnavailable(checks, selection, checkId, snapshot.Message!);
                requiredUnavailable |= selection.Requirement == AgentRunLinkedContextRequirement.Required;
                continue;
            }

            contexts.Add(new AgentRunLinkedContext(
                member.ProjectId,
                member.Name,
                member.Alias,
                new AgentRunRepository(snapshot.Remote!, snapshot.Commit!),
                selection.Requirement,
                [AgentRunLinkedContextScope.Wiki]));
            checks.Add(Passed(checkId,
                $"Captured published wiki context {member.Alias} at {snapshot.Commit![..12]}."));
        }

        return AppResult<AgentRunLinkedContextResolution>.Ok(new AgentRunLinkedContextResolution(
            contexts, checks, !requiredUnavailable));
    }

    private static async Task<(bool Success, string? Remote, string? Commit, string? Message)>
        InspectPublishedRepository(string repositoryPath, CancellationToken cancellationToken)
    {
        var root = await Git(repositoryPath, LocalTimeout, cancellationToken, "rev-parse", "--show-toplevel");
        if (!root.Success)
            return (false, null, null, "The linked project is not inside a Git repository.");
        var repositoryRoot = Text(root).Trim();
        var status = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "status", "--porcelain=v1", "--untracked-files=normal");
        if (!status.Success || status.Output.Length != 0)
            return (false, null, null, "The linked project worktree must be clean before its wiki is attached.");

        var head = await Git(repositoryRoot, LocalTimeout, cancellationToken, "rev-parse", "HEAD");
        var commit = Text(head).Trim();
        if (!head.Success || !CommitPattern().IsMatch(commit))
            return (false, null, null, "The linked project commit could not be resolved.");

        var remote = await Git(repositoryRoot, LocalTimeout, cancellationToken,
            "config", "--get", "remote.origin.url");
        var remoteUrl = Text(remote).Trim();
        if (!remote.Success || !IsSafeRemote(remoteUrl))
            return (false, null, null, "The linked project must have a credential-free origin remote.");

        var published = await Git(repositoryRoot, RemoteTimeout, cancellationToken,
            "ls-remote", "--heads", "origin");
        if (!published.Success || !Text(published).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.StartsWith(commit + "\t", StringComparison.Ordinal)))
            return (false, null, null, "The exact linked project commit is not published to an origin branch.");

        return (true, remoteUrl, commit, null);
    }

    private static bool IsSafeRemote(string value)
    {
        if (value.StartsWith("git@", StringComparison.Ordinal) && value.Contains(':')) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme is "https" or "ssh" && string.IsNullOrEmpty(uri.UserInfo) && !uri.IsLoopback;
    }

    private static AgentRunPreflightCheck Passed(string id, string summary) =>
        new(id, "Linked wiki context", AgentRunPreflightCheckStatus.Passed, summary);

    private static AgentRunPreflightCheck Failed(string id, string summary) =>
        new(id, "Linked wiki context", AgentRunPreflightCheckStatus.Failed, summary);

    private static AgentRunPreflightCheck Skipped(string id, string summary) =>
        new(id, "Linked wiki context", AgentRunPreflightCheckStatus.Skipped, summary);

    private static void AddUnavailable(
        ICollection<AgentRunPreflightCheck> checks,
        AgentRunLinkedContextSelection selection,
        string id,
        string summary) =>
        checks.Add(selection.Requirement == AgentRunLinkedContextRequirement.Required
            ? Failed(id, summary)
            : Skipped(id, $"Optional context omitted: {summary}"));

    private static string Text(GitResult result) => Encoding.UTF8.GetString(result.Output);

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
            await process.WaitForExitAsync(timeoutSource.Token);
            await Task.WhenAll(copy, discard);
            return new GitResult(process.ExitCode == 0, output.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(true);
            return new GitResult(false, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidOperationException)
        {
            return new GitResult(false, []);
        }
    }

    private sealed record GitResult(bool Success, byte[] Output);

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}
