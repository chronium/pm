using System.Net;
using System.Text.Json.Serialization;

namespace PM.AgentRuns;

public sealed record AgentRunnerRegistration(
    string RunnerId,
    string DisplayName,
    Uri Endpoint,
    string TlsFingerprint,
    AgentRunProtocolVersion ProtocolVersion,
    string ClientId,
    string ClientFingerprint,
    DateTimeOffset PairedAt);

public sealed record AgentRunnerPairingRequest(
    Uri Endpoint,
    string ExpectedRunnerId,
    string ExpectedTlsFingerprint,
    string PairingCode,
    bool ReplaceExisting = false);

public sealed record AgentRunnerHealth(
    string RunnerId,
    string Status,
    AgentRunProtocolVersion ProtocolVersion,
    DateTimeOffset Timestamp);

[JsonConverter(typeof(JsonStringEnumConverter<AgentRunRemoteStartDisposition>))]
public enum AgentRunRemoteStartDisposition
{
    [JsonStringEnumMemberName("new")]
    New,

    [JsonStringEnumMemberName("existing")]
    Existing,
}

public sealed record AgentRunRemoteStart(
    AgentRunRemoteStartDisposition Disposition,
    AgentRunnerRun Run);

public sealed record AgentRunnerRun(
    string RunId,
    string SpecificationHash,
    AgentRunSpecification Specification,
    AgentRunState State,
    long LastEventSequence,
    DateTimeOffset AcceptedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? TerminalAt,
    DateTimeOffset? CancellationRequestedAt,
    string? AgentThreadId);

public sealed record AgentRunnerRunSummary(
    string RunId,
    string TaskId,
    string TaskTitle,
    AgentRunState State,
    long LastEventSequence,
    DateTimeOffset AcceptedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CancellationRequestedAt);

public sealed record AgentRunnerRunPage(
    IReadOnlyList<AgentRunnerRunSummary> Runs,
    string? NextCursor,
    bool HasMore);

public sealed record AgentRunEventPage(
    IReadOnlyList<AgentRunEvent> Events,
    long NextAfterSequence,
    bool HasMore,
    bool Terminal);

public sealed record AgentRunStreamEnd(AgentRunState State, long LastSequence);

public sealed record AgentRunStreamMessage(AgentRunEvent? Event, AgentRunStreamEnd? End)
{
    public static AgentRunStreamMessage Durable(AgentRunEvent runEvent) => new(runEvent, null);
    public static AgentRunStreamMessage Terminal(AgentRunStreamEnd end) => new(null, end);
}

public sealed record AgentRunCancellation(string Disposition, AgentRunnerRun Run);

public sealed record AgentRunnerTransportFailure(
    string ErrorCode,
    string Message,
    HttpStatusCode? StatusCode = null);

public sealed class AgentRunnerStreamException(string errorCode, string message, Exception? inner = null)
    : IOException(message, inner)
{
    public string ErrorCode { get; } = errorCode;
}
