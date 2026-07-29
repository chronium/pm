using PM.Application;
using System.Text.Json.Serialization;

namespace PM.AgentRuns;

public sealed record AgentRunSelection(
    string TaskId,
    string RunnerId,
    string ProfileId,
    string ProviderId,
    string ModelId,
    string EffortId);

[JsonConverter(typeof(JsonStringEnumConverter<AgentRunPreflightCheckStatus>))]
public enum AgentRunPreflightCheckStatus
{
    [JsonStringEnumMemberName("passed")]
    Passed,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("skipped")]
    Skipped,
}

public sealed record AgentRunPreflightCheck(
    string Id,
    string Label,
    AgentRunPreflightCheckStatus Status,
    string Summary);

public sealed record AgentRunPreflightResult(
    bool Ready,
    string? RunId,
    string? Revision,
    AgentRunRequest? Request,
    IReadOnlyList<AgentRunPreflightCheck> Checks);

public sealed record AgentRunInspection(
    AgentRunnerRun Run,
    bool TaskChanged,
    string? CurrentTaskRevision,
    string Revision);

public sealed record AgentRunGitSnapshot(
    string RepositoryRoot,
    string Branch,
    string RemoteName,
    string RemoteUrl,
    string UpstreamReference,
    string HeadCommit,
    string TaskRevision);

public sealed record AgentRunGitInspection(
    AgentRunGitSnapshot? Snapshot,
    IReadOnlyList<AgentRunPreflightCheck> Checks)
{
    public bool Ready => Snapshot != null && Checks.All(check => check.Status != AgentRunPreflightCheckStatus.Failed);
}

public interface IAgentRunGitInspector
{
    Task<AppResult<AgentRunGitInspection>> Inspect(
        string projectDirectory,
        string taskId,
        CancellationToken cancellationToken = default);
}

public interface IAgentRunService
{
    Task<AppResult<AgentRunPreflightResult>> Preflight(
        AgentRunSelection selection,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunRemoteStart>> Start(
        string runId,
        string expectedRevision,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunInspection>> Inspect(
        string runId,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunnerRunPage>> ActiveRuns(
        string runnerId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunEventPage>> Events(
        string runId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default);
    Task<AppResult<IAgentRunnerEventStream>> OpenEventStream(
        string runId,
        long afterSequence,
        CancellationToken cancellationToken = default);
    Task<AppResult> AdvanceSequence(string runId, long sequence);
    Task<AppResult<AgentRunCancellation>> Cancel(
        string runId,
        CancellationToken cancellationToken = default);
    Task<AppResult<IReadOnlyList<AgentRunArtifact>>> Artifacts(
        string runId,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunArtifact>> Artifact(
        string runId,
        string artifactId,
        CancellationToken cancellationToken = default);
}
