using System.Security.Cryptography;
using System.Text.Json;
using PM.Application;

namespace PM.AgentRuns;

public sealed partial class AgentRunService
{
    private const string PatchArtifactId = "changes-patch";
    private const long MaximumPatchBytes = 64L * 1024 * 1024;
    private readonly SemaphoreSlim _patchCollectionGate = new(1, 1);

    public async Task<AppResult<AgentRunPatchPreflightResult>> PreflightPatchCollection(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PreparePatch(runId, cancellationToken);
        if (!prepared.Success)
            return AppResult<AgentRunPatchPreflightResult>.Fail(prepared.ErrorCode!, prepared.Message!);
        await using var value = prepared.Payload!;
        return AppResult<AgentRunPatchPreflightResult>.Ok(value.Preflight);
    }

    public async Task<AppResult<AgentRunPatchCollectionResult>> CollectPatch(
        string runId,
        string expectedRevision,
        string expectedArtifactSha256,
        CancellationToken cancellationToken = default)
    {
        if (expectedRevision?.Length != 64 || expectedArtifactSha256?.Length != 64)
            return AppResult<AgentRunPatchCollectionResult>.Fail("invalid_patch_collection",
                "A preflight revision and artifact digest are required.");

        await _patchCollectionGate.WaitAsync(cancellationToken);
        try
        {
            var prepared = await PreparePatch(runId, cancellationToken);
            if (!prepared.Success)
                return AppResult<AgentRunPatchCollectionResult>.Fail(prepared.ErrorCode!, prepared.Message!);
            await using var value = prepared.Payload!;
            if (!string.Equals(expectedRevision, value.Preflight.Revision, StringComparison.Ordinal) ||
                !string.Equals(expectedArtifactSha256, value.Preflight.ArtifactSha256, StringComparison.Ordinal))
                return AppResult<AgentRunPatchCollectionResult>.Fail("stale_patch_preflight",
                    "The repository, task, or retained artifact changed after preflight. Review it again.");
            if (!value.Preflight.Ready)
                return AppResult<AgentRunPatchCollectionResult>.Fail("patch_preflight_failed",
                    "The patch did not pass collection preflight.");

            var applied = await AgentRunPatchGit.Apply(value.RepositoryRoot, value.Temporary.Path,
                cancellationToken);
            if (!applied.Success)
                return AppResult<AgentRunPatchCollectionResult>.Fail(applied.ErrorCode!, applied.Message!);

            var result = new AgentRunPatchCollectionResult(
                runId,
                value.Preflight.ArtifactId,
                value.Preflight.ArtifactSha256,
                value.Preflight.BaseCommit,
                value.Preflight.CurrentHead,
                value.Preflight.Paths.Select(path => path.Path).Distinct(StringComparer.Ordinal).ToArray(),
                CanonicalNow());
            var recorded = await cache.RecordPatchCollection(runId, result);
            return recorded.Success
                ? AppResult<AgentRunPatchCollectionResult>.Ok(result)
                : AppResult<AgentRunPatchCollectionResult>.Fail(recorded.ErrorCode!, recorded.Message!);
        }
        finally
        {
            _patchCollectionGate.Release();
        }
    }

    private async Task<AppResult<PreparedPatch>> PreparePatch(
        string runId,
        CancellationToken cancellationToken)
    {
        var cached = await cache.Get(runId);
        if (!cached.Success)
            return AppResult<PreparedPatch>.Fail(cached.ErrorCode!, cached.Message!);
        if (cached.Payload!.PatchCollection != null)
            return AppResult<PreparedPatch>.Fail("patch_already_collected",
                "This run's patch was already collected.");
        var remote = await runners.Inspect(cached.Payload.Selection.RunnerId, runId, cancellationToken);
        if (!remote.Success)
            return AppResult<PreparedPatch>.Fail(remote.ErrorCode!, remote.Message!);
        var remoteUpdate = await cache.UpdateRemote(runId, remote.Payload!);
        if (!remoteUpdate.Success)
            return AppResult<PreparedPatch>.Fail(remoteUpdate.ErrorCode!, remoteUpdate.Message!);
        if (remote.Payload!.State != AgentRunState.Completed)
            return AppResult<PreparedPatch>.Fail("patch_not_ready",
                "Only a completed run can be collected.");

        var artifacts = await runners.Artifacts(cached.Payload.Selection.RunnerId, runId, cancellationToken);
        if (!artifacts.Success)
            return AppResult<PreparedPatch>.Fail(artifacts.ErrorCode!, artifacts.Message!);
        var patchArtifacts = artifacts.Payload!.Where(item => item.ArtifactId == PatchArtifactId).ToArray();
        if (patchArtifacts.Length == 0)
            return AppResult<PreparedPatch>.Fail("artifact_not_found",
                "The completed run did not retain a changes.patch artifact.");
        if (patchArtifacts.Length != 1)
            return AppResult<PreparedPatch>.Fail("artifact_invalid",
                "The completed run returned duplicate patch metadata.");
        var artifact = patchArtifacts[0];
        if (artifact.Kind is not ("patch" or "git_patch") || artifact.FileName != "changes.patch" ||
            artifact.MediaType != "text/x-diff" || artifact.ByteLength <= 0 ||
            artifact.ByteLength > MaximumPatchBytes)
            return AppResult<PreparedPatch>.Fail("artifact_invalid",
                "The retained patch metadata is not safe for collection.");

        var temporary = cache.CreateTemporaryFile(runId, "patch");
        if (!temporary.Success)
            return AppResult<PreparedPatch>.Fail(temporary.ErrorCode!, temporary.Message!);
        var cleanup = temporary.Payload!;
        var downloaded = await DownloadPatch(cached.Payload.Selection.RunnerId, runId, artifact,
            cleanup.Path, cancellationToken);
        if (!downloaded.Success)
        {
            await cleanup.DisposeAsync();
            return AppResult<PreparedPatch>.Fail(downloaded.ErrorCode!, downloaded.Message!);
        }

        var repositoryRoot = projectRoot.RootPath == null
            ? null
            : Directory.GetParent(projectRoot.RootPath)?.FullName;
        if (repositoryRoot == null)
        {
            await cleanup.DisposeAsync();
            return AppResult<PreparedPatch>.Fail("missing_repository", "The project repository was not found.");
        }

        var specification = cached.Payload.Request.Specification;
        var analysis = await AgentRunPatchGit.Analyze(repositoryRoot,
            specification.Repository.Remote, specification.Repository.BaseCommit,
            cleanup.Path, cancellationToken);
        if (!analysis.Success)
        {
            await cleanup.DisposeAsync();
            return AppResult<PreparedPatch>.Fail(analysis.ErrorCode!, analysis.Message!);
        }

        var currentTaskRevision = CurrentTaskRevision(specification.Task.TaskId);
        var warnings = analysis.Payload!.Warnings.ToList();
        var checks = new List<AgentRunPreflightCheck>
        {
            new("run_complete", "Completed run", AgentRunPreflightCheckStatus.Passed,
                "The runner completed this immutable run."),
            new("artifact_integrity", "Artifact integrity", AgentRunPreflightCheckStatus.Passed,
                "The retained patch length and SHA-256 digest were verified."),
        };
        checks.AddRange(analysis.Payload.Checks);
        var taskChanged = currentTaskRevision == null ||
                          !string.Equals(currentTaskRevision, specification.Task.Revision,
                              StringComparison.Ordinal);
        checks.Add(new AgentRunPreflightCheck("task_revision", "Task revision",
            AgentRunPreflightCheckStatus.Passed,
            taskChanged
                ? "The task changed after launch; the original run revision remains explicit."
                : "The task still matches the revision used by the run."));
        if (taskChanged)
            warnings.Add("The task changed after launch. Review this patch against the original task revision.");

        var ready = analysis.Payload.Ready;
        var revision = PatchRevision(runId, artifact, specification.Repository.BaseCommit,
            analysis.Payload, currentTaskRevision);
        var preflight = new AgentRunPatchPreflightResult(
            ready,
            revision,
            artifact.ArtifactId,
            artifact.Sha256,
            specification.Repository.BaseCommit,
            analysis.Payload.CurrentHead,
            specification.Task.Revision,
            currentTaskRevision,
            checks,
            warnings,
            analysis.Payload.Paths,
            analysis.Payload.Statistics);
        return AppResult<PreparedPatch>.Ok(new PreparedPatch(repositoryRoot, cleanup, preflight));
    }

    private async Task<AppResult> DownloadPatch(
        string runnerId,
        string runId,
        AgentRunArtifact artifact,
        string destination,
        CancellationToken cancellationToken)
    {
        var content = await runners.ArtifactContent(runnerId, runId, artifact.ArtifactId,
            cancellationToken);
        if (!content.Success) return AppResult.Fail(content.ErrorCode!, content.Message!);
        await using var value = content.Payload!;
        if (value.Artifact != artifact)
            return AppResult.Fail("artifact_invalid", "The runner returned mismatched patch metadata.");

        try
        {
            await using var output = new FileStream(destination, FileMode.Truncate, FileAccess.Write,
                FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await value.Content.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > artifact.ByteLength || total > MaximumPatchBytes)
                    return AppResult.Fail("artifact_corrupt", "The patch exceeded its retained length.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return total == artifact.ByteLength &&
                   string.Equals(actual, artifact.Sha256, StringComparison.Ordinal)
                ? AppResult.Ok()
                : AppResult.Fail("artifact_corrupt", "The patch did not match its retained SHA-256 digest.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("run_cache_unavailable", "The verified patch could not be stored privately.");
        }
    }

    private static string PatchRevision(
        string runId,
        AgentRunArtifact artifact,
        string baseCommit,
        AgentRunPatchGitAnalysis analysis,
        string? currentTaskRevision)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            RunId = runId,
            ArtifactId = artifact.ArtifactId,
            ArtifactSha256 = artifact.Sha256,
            BaseCommit = baseCommit,
            analysis.CurrentHead,
            analysis.WorktreeFingerprint,
            CurrentTaskRevision = currentTaskRevision,
            Paths = analysis.Paths,
            analysis.Statistics,
        }, AgentRunJson.Options);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record PreparedPatch(
        string RepositoryRoot,
        AgentRunTemporaryFile Temporary,
        AgentRunPatchPreflightResult Preflight) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Temporary.DisposeAsync();
    }
}
