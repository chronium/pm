using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PM.Application;

namespace PM.AgentRuns;

internal sealed record AgentRunPatchGitAnalysis(
    string CurrentHead,
    string WorktreeFingerprint,
    IReadOnlyList<AgentRunPreflightCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AgentRunPatchPath> Paths,
    AgentRunPatchStatistics Statistics)
{
    public bool Ready => Checks.All(check => check.Status != AgentRunPreflightCheckStatus.Failed);
}

internal static class AgentRunPatchGit
{
    private const int MaximumChangedPaths = 5_000;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<AppResult<AgentRunPatchGitAnalysis>> Analyze(
        string repositoryRoot,
        string expectedRemote,
        string baseCommit,
        string patchPath,
        CancellationToken cancellationToken)
    {
        var checks = new List<AgentRunPreflightCheck>();
        var warnings = new List<string>();
        var emptyStatistics = new AgentRunPatchStatistics(0, 0, 0, 0);

        var root = await Git(repositoryRoot, cancellationToken, null, "rev-parse", "--show-toplevel");
        if (!root.Success)
            return Failure("repository_identity", "Repository identity",
                "The current project is not inside a Git worktree.");
        var resolvedRoot = Decode(root.StandardOutput).Trim();
        if (!SamePath(repositoryRoot, resolvedRoot))
            return Failure("repository_identity", "Repository identity",
                "The PM project is not at the Git worktree root.");

        var remotes = await Git(repositoryRoot, cancellationToken, null, "remote");
        if (!remotes.Success)
            return Failure("repository_identity", "Repository identity",
                "Repository remotes could not be inspected.");
        var remoteMatches = false;
        foreach (var remote in Decode(remotes.StandardOutput)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var urls = await Git(repositoryRoot, cancellationToken, null,
                "remote", "get-url", "--all", remote);
            if (urls.Success && Decode(urls.StandardOutput)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(url => string.Equals(url, expectedRemote, StringComparison.Ordinal)))
            {
                remoteMatches = true;
                break;
            }
        }
        if (!remoteMatches)
            return Failure("repository_identity", "Repository identity",
                "The run belongs to a different repository remote.");
        checks.Add(Passed("repository_identity", "Repository identity",
            "The local worktree matches the run repository."));

        var headResult = await Git(repositoryRoot, cancellationToken, null, "rev-parse", "HEAD");
        if (!headResult.Success)
            return Failure("base_commit", "Exact base commit", "The current Git HEAD could not be resolved.");
        var head = Decode(headResult.StandardOutput).Trim();
        if (!string.Equals(head, baseCommit, StringComparison.Ordinal))
        {
            checks.Add(Failed("base_commit", "Exact base commit",
                $"Current HEAD {Short(head)} does not match run base {Short(baseCommit)}."));
            return AppResult<AgentRunPatchGitAnalysis>.Ok(new AgentRunPatchGitAnalysis(
                head, Fingerprint(head, []), checks, warnings, [], emptyStatistics));
        }
        checks.Add(Passed("base_commit", "Exact base commit", $"Current HEAD matches {Short(baseCommit)}."));

        var status = await Git(repositoryRoot, cancellationToken, null,
            "status", "--porcelain=v1", "-z", "--untracked-files=all");
        if (!status.Success)
            return AppResult<AgentRunPatchGitAnalysis>.Fail("git_unavailable",
                "The local worktree could not be inspected.");
        var dirtyPaths = ParseWorktreePaths(status.StandardOutput);
        if (!dirtyPaths.Success)
            return AppResult<AgentRunPatchGitAnalysis>.Fail(dirtyPaths.ErrorCode!, dirtyPaths.Message!);
        var worktreeFingerprint = Fingerprint(head, status.StandardOutput);

        var indexPath = patchPath + ".index";
        try
        {
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = indexPath };
            var readTree = await Git(repositoryRoot, cancellationToken, environment, "read-tree", "HEAD");
            if (!readTree.Success)
                return AppResult<AgentRunPatchGitAnalysis>.Fail("git_unavailable",
                    "A temporary Git index could not be prepared.");
            var cachedCheck = await Git(repositoryRoot, cancellationToken, environment,
                "apply", "--cached", "--check", "--whitespace=nowarn", patchPath);
            if (!cachedCheck.Success)
                return PatchFailure("patch_valid", "Patch validity",
                    "The retained artifact is not a valid patch for the immutable base.");
            var cachedApply = await Git(repositoryRoot, cancellationToken, environment,
                "apply", "--cached", "--whitespace=nowarn", patchPath);
            if (!cachedApply.Success)
                return PatchFailure("patch_valid", "Patch validity",
                    "The retained artifact could not be analyzed safely.");

            var nameStatus = await Git(repositoryRoot, cancellationToken, environment,
                "diff", "--cached", "--name-status", "-z", "HEAD");
            var numstat = await Git(repositoryRoot, cancellationToken, environment,
                "diff", "--cached", "--numstat", "-z", "HEAD");
            if (!nameStatus.Success || !numstat.Success)
                return AppResult<AgentRunPatchGitAnalysis>.Fail("git_unavailable",
                    "Patch paths and statistics could not be inspected.");

            var changes = ParseNameStatus(nameStatus.StandardOutput);
            var numbers = ParseNumstat(numstat.StandardOutput);
            if (!changes.Success || !numbers.Success)
                return AppResult<AgentRunPatchGitAnalysis>.Fail("invalid_patch_paths",
                    "Patch paths could not be decoded safely.");
            if (changes.Payload!.Count == 0)
                return PatchFailure("patch_valid", "Patch validity", "The retained patch contains no changes.");
            var physicalPaths = changes.Payload.SelectMany(change => change.Paths).Distinct(StringComparer.Ordinal)
                .ToArray();
            if (physicalPaths.Length > MaximumChangedPaths)
                return PatchFailure("patch_size", "Patch size",
                    $"The patch changes more than {MaximumChangedPaths} paths.");

            var safety = await ValidatePaths(repositoryRoot, environment, physicalPaths, cancellationToken);
            if (!safety.Success)
                return PatchFailure("patch_safety", "Patch safety", safety.Message!);
            checks.Add(Passed("patch_safety", "Patch safety",
                "Changed paths, file modes, and symlink targets stay within the repository."));

            var overlap = physicalPaths.Intersect(dirtyPaths.Payload!, PathComparer()).Order().ToArray();
            if (overlap.Length != 0)
            {
                checks.Add(Failed("worktree_overlap", "Local worktree overlap",
                    $"Local changes overlap {overlap.Length} patch path(s): {string.Join(", ", overlap.Take(5))}."));
            }
            else
            {
                checks.Add(Passed("worktree_overlap", "Local worktree overlap",
                    dirtyPaths.Payload!.Count == 0
                        ? "The local worktree is clean."
                        : "Existing local changes do not overlap the patch."));
                if (dirtyPaths.Payload.Count != 0)
                    warnings.Add($"The worktree has {dirtyPaths.Payload.Count} non-overlapping changed path(s). They will be preserved.");
            }

            var workingCheck = await Git(repositoryRoot, cancellationToken, null,
                "apply", "--check", "--whitespace=nowarn", patchPath);
            if (!workingCheck.Success)
                checks.Add(Failed("patch_apply", "Patch application",
                    "Git cannot apply the patch to the current worktree without conflicts."));
            else
                checks.Add(Passed("patch_apply", "Patch application",
                    "Git confirms that the patch applies to the current worktree."));

            var numberEntries = numbers.Payload!;
            var paths = BuildPaths(changes.Payload, numberEntries);
            var statistics = new AgentRunPatchStatistics(
                physicalPaths.Length,
                numberEntries.Sum(item => item.Insertions ?? 0),
                numberEntries.Sum(item => item.Deletions ?? 0),
                numberEntries.Count(item => item.Binary));
            checks.Add(Passed("patch_valid", "Patch validity",
                $"The verified patch changes {statistics.FilesChanged} path(s)."));
            return AppResult<AgentRunPatchGitAnalysis>.Ok(new AgentRunPatchGitAnalysis(
                head, worktreeFingerprint, checks, warnings, paths, statistics));
        }
        catch (DecoderFallbackException)
        {
            return AppResult<AgentRunPatchGitAnalysis>.Fail("invalid_patch_paths",
                "Patch paths must use valid UTF-8.");
        }
        finally
        {
            try { File.Delete(indexPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        AppResult<AgentRunPatchGitAnalysis> Failure(string id, string label, string summary)
        {
            checks.Add(Failed(id, label, summary));
            return AppResult<AgentRunPatchGitAnalysis>.Ok(new AgentRunPatchGitAnalysis(
                string.Empty, Fingerprint(string.Empty, []), checks, warnings, [], emptyStatistics));
        }

        AppResult<AgentRunPatchGitAnalysis> PatchFailure(string id, string label, string summary)
        {
            checks.Add(Failed(id, label, summary));
            return AppResult<AgentRunPatchGitAnalysis>.Ok(new AgentRunPatchGitAnalysis(
                head, worktreeFingerprint, checks, warnings, [], emptyStatistics));
        }
    }

    public static async Task<AppResult> Apply(
        string repositoryRoot,
        string patchPath,
        CancellationToken cancellationToken)
    {
        var result = await Git(repositoryRoot, cancellationToken, null,
            "apply", "--whitespace=nowarn", patchPath);
        return result.Success
            ? AppResult.Ok()
            : AppResult.Fail("patch_apply_failed", "Git rejected the patch without modifying the worktree.");
    }

    private static async Task<AppResult> ValidatePaths(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> physicalPaths,
        CancellationToken cancellationToken)
    {
        foreach (var path in physicalPaths)
        {
            if (!SafeRelativePath(repositoryRoot, path))
                return AppResult.Fail("unsafe_patch_path", $"Patch path '{path}' is unsafe.");
            if (IsPmAuthorityPath(path))
                return AppResult.Fail("patch_authority_violation",
                    $"Patch path '{path}' is controlled by PM and cannot be collected from an agent.");
            if (HasSymlinkParent(repositoryRoot, path))
                return AppResult.Fail("unsafe_patch_symlink",
                    $"Patch path '{path}' traverses an existing symbolic link.");
        }

        var modes = await Git(repositoryRoot, cancellationToken, environment,
            ["ls-files", "--stage", "-z", "--", .. physicalPaths]);
        var baseModes = await Git(repositoryRoot, cancellationToken, null,
            ["ls-tree", "-r", "-z", "HEAD", "--", .. physicalPaths]);
        if (!modes.Success || !baseModes.Success)
            return AppResult.Fail("git_unavailable", "Resulting file modes could not be inspected.");
        var resultingEntries = ParseIndexModes(modes.StandardOutput);
        foreach (var entry in resultingEntries.Concat(ParseTreeModes(baseModes.StandardOutput)))
        {
            if (entry.Mode == "160000")
                return AppResult.Fail("patch_gitlink", $"Patch path '{entry.Path}' is a Git submodule.");
            if (entry.Mode is not ("100644" or "100755" or "120000"))
                return AppResult.Fail("patch_special_file", $"Patch path '{entry.Path}' has an unsupported file mode.");
            if (entry.Mode == "120000")
            {
                var target = await Git(repositoryRoot, cancellationToken, environment,
                    "cat-file", "blob", entry.ObjectId);
                if (!target.Success || !SafeSymlinkTarget(repositoryRoot, entry.Path, Decode(target.StandardOutput)))
                    return AppResult.Fail("unsafe_patch_symlink",
                        $"Symbolic link '{entry.Path}' would escape the repository.");
            }
            else if (new FileInfo(ToFullPath(repositoryRoot, entry.Path)).LinkTarget != null)
            {
                return AppResult.Fail("unsafe_patch_symlink",
                    $"Patch path '{entry.Path}' resolves to an existing symbolic link.");
            }
        }

        return AppResult.Ok();
    }

    private static IReadOnlyList<AgentRunPatchPath> BuildPaths(
        IReadOnlyList<ChangeEntry> changes,
        IReadOnlyList<NumstatEntry> numbers)
    {
        var byPath = numbers.ToDictionary(item => item.Path, StringComparer.Ordinal);
        var result = new List<AgentRunPatchPath>();
        foreach (var change in changes)
        {
            for (var index = 0; index < change.Paths.Count; index++)
            {
                var path = change.Paths[index];
                byPath.TryGetValue(path, out var number);
                var status = change.Paths.Count == 2
                    ? index == 0 ? "renamed_from" : "renamed_to"
                    : StatusLabel(change.Status);
                result.Add(new AgentRunPatchPath(path, status, number?.Insertions,
                    number?.Deletions, number?.Binary ?? false));
            }
        }
        return result;
    }

    private static string StatusLabel(string status) => status[0] switch
    {
        'A' => "added",
        'D' => "deleted",
        'M' => "modified",
        'T' => "type_changed",
        'C' => "copied",
        _ => "changed",
    };

    private static AppResult<IReadOnlySet<string>> ParseWorktreePaths(byte[] bytes)
    {
        try
        {
            var fields = SplitNull(bytes);
            var paths = new HashSet<string>(PathComparer());
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field.Length < 4 || field[2] != ' ')
                    return AppResult<IReadOnlySet<string>>.Fail("invalid_git_status", "Git status output is invalid.");
                paths.Add(field[3..]);
                if ((field[0] is 'R' or 'C' || field[1] is 'R' or 'C') && ++index < fields.Count)
                    paths.Add(fields[index]);
            }
            return AppResult<IReadOnlySet<string>>.Ok(paths);
        }
        catch (DecoderFallbackException)
        {
            return AppResult<IReadOnlySet<string>>.Fail("invalid_git_status",
                "Worktree paths must use valid UTF-8.");
        }
    }

    private static AppResult<IReadOnlyList<ChangeEntry>> ParseNameStatus(byte[] bytes)
    {
        try
        {
            var fields = SplitNull(bytes);
            var entries = new List<ChangeEntry>();
            for (var index = 0; index < fields.Count;)
            {
                var status = fields[index++];
                var pathCount = status.StartsWith('R') || status.StartsWith('C') ? 2 : 1;
                if (status.Length == 0 || index + pathCount > fields.Count)
                    return AppResult<IReadOnlyList<ChangeEntry>>.Fail("invalid_patch_paths",
                        "Patch path metadata is invalid.");
                entries.Add(new ChangeEntry(status, fields.Skip(index).Take(pathCount).ToArray()));
                index += pathCount;
            }
            return AppResult<IReadOnlyList<ChangeEntry>>.Ok(entries);
        }
        catch (DecoderFallbackException)
        {
            return AppResult<IReadOnlyList<ChangeEntry>>.Fail("invalid_patch_paths",
                "Patch paths must use valid UTF-8.");
        }
    }

    private static AppResult<IReadOnlyList<NumstatEntry>> ParseNumstat(byte[] bytes)
    {
        try
        {
            var fields = SplitNull(bytes);
            var entries = new List<NumstatEntry>();
            for (var index = 0; index < fields.Count; index++)
            {
                var parts = fields[index].Split('\t', 3);
                if (parts.Length != 3)
                    return AppResult<IReadOnlyList<NumstatEntry>>.Fail("invalid_patch_paths",
                        "Patch statistics are invalid.");
                var path = parts[2];
                if (path.Length == 0)
                {
                    if (index + 2 >= fields.Count)
                        return AppResult<IReadOnlyList<NumstatEntry>>.Fail("invalid_patch_paths",
                            "Rename statistics are invalid.");
                    index++;
                    _ = fields[index];
                    path = fields[++index];
                }
                var binary = parts[0] == "-" && parts[1] == "-";
                if (!binary && (!long.TryParse(parts[0], out _) || !long.TryParse(parts[1], out _)))
                    return AppResult<IReadOnlyList<NumstatEntry>>.Fail("invalid_patch_paths",
                        "Patch statistics are invalid.");
                entries.Add(new NumstatEntry(path,
                    binary ? null : long.Parse(parts[0]), binary ? null : long.Parse(parts[1]), binary));
            }
            return AppResult<IReadOnlyList<NumstatEntry>>.Ok(entries);
        }
        catch (DecoderFallbackException)
        {
            return AppResult<IReadOnlyList<NumstatEntry>>.Fail("invalid_patch_paths",
                "Patch paths must use valid UTF-8.");
        }
    }

    private static IReadOnlyList<IndexEntry> ParseIndexModes(byte[] bytes)
    {
        var result = new List<IndexEntry>();
        foreach (var field in SplitNull(bytes))
        {
            var tab = field.IndexOf('\t');
            var metadata = tab < 0 ? [] : field[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tab < 0 || metadata.Length < 3) throw new DecoderFallbackException();
            result.Add(new IndexEntry(metadata[0], metadata[1], field[(tab + 1)..]));
        }
        return result;
    }

    private static IReadOnlyList<IndexEntry> ParseTreeModes(byte[] bytes)
    {
        var result = new List<IndexEntry>();
        foreach (var field in SplitNull(bytes))
        {
            var tab = field.IndexOf('\t');
            var metadata = tab < 0 ? [] : field[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tab < 0 || metadata.Length < 3) throw new DecoderFallbackException();
            result.Add(new IndexEntry(metadata[0], metadata[2], field[(tab + 1)..]));
        }
        return result;
    }

    private static IReadOnlyList<string> SplitNull(byte[] bytes) => StrictUtf8.GetString(bytes)
        .Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static bool SafeRelativePath(string root, string path)
    {
        if (path.Length == 0 || path.Contains('\\') || path.Any(char.IsControl) ||
            Path.IsPathFullyQualified(path) || path.Split('/').Any(segment => segment is "" or "." or "..") ||
            path.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) return false;
        var full = ToFullPath(root, path);
        return IsWithin(root, full);
    }

    private static bool IsPmAuthorityPath(string path) =>
        path.StartsWith(".pm/states/", StringComparison.Ordinal) ||
        path.Equals(".pm/task_order.yaml", StringComparison.Ordinal) ||
        path.Equals(".pm/project_id.txt", StringComparison.Ordinal);

    private static bool HasSymlinkParent(string root, string path)
    {
        var current = Path.GetFullPath(root);
        foreach (var segment in path.Split('/').SkipLast(1))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget != null) return true;
        }
        return false;
    }

    private static bool SafeSymlinkTarget(string root, string path, string target)
    {
        if (target.Length == 0 || target.Contains('\0') || Path.IsPathFullyQualified(target)) return false;
        var parent = Path.GetDirectoryName(ToFullPath(root, path))!;
        return IsWithin(root, Path.GetFullPath(Path.Combine(parent, target)));
    }

    private static string ToFullPath(string root, string path) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

    private static bool IsWithin(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return candidate.Equals(canonicalRoot, comparison) ||
               candidate.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IEqualityComparer<string> PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Fingerprint(string head, byte[] status) =>
        Convert.ToHexString(SHA256.HashData([.. Encoding.UTF8.GetBytes(head), 0, .. status])).ToLowerInvariant();

    private static string Short(string value) => value.Length <= 12 ? value : value[..12];

    private static string Decode(byte[] value) => StrictUtf8.GetString(value);

    private static AgentRunPreflightCheck Passed(string id, string label, string summary) =>
        new(id, label, AgentRunPreflightCheckStatus.Passed, summary);

    private static AgentRunPreflightCheck Failed(string id, string label, string summary) =>
        new(id, label, AgentRunPreflightCheckStatus.Failed, summary);

    private static async Task<GitResult> Git(
        string directory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments) => await Git(directory, cancellationToken, environment,
            (IReadOnlyList<string>)arguments);

    private static async Task<GitResult> Git(
        string directory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment,
        IReadOnlyList<string> arguments)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment != null)
            foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        try
        {
            using var process = Process.Start(start);
            if (process == null) return new GitResult(false, [], "Git could not be started.");
            await using var output = new MemoryStream();
            var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(outputTask, process.WaitForExitAsync(timeout.Token));
            var error = await errorTask;
            return new GitResult(process.ExitCode == 0, output.ToArray(), error);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                           OperationCanceledException)
        {
            return new GitResult(false, [], "Git command failed.");
        }
    }

    private sealed record GitResult(bool Success, byte[] StandardOutput, string StandardError);
    private sealed record ChangeEntry(string Status, IReadOnlyList<string> Paths);
    private sealed record NumstatEntry(string Path, long? Insertions, long? Deletions, bool Binary);
    private sealed record IndexEntry(string Mode, string ObjectId, string Path);
}
