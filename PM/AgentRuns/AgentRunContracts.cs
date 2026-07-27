using System.Text.Json;
using System.Text.Json.Serialization;

namespace PM.AgentRuns;

public static class AgentRunProtocol
{
    public static readonly AgentRunProtocolVersion Current = new(1, 0);

    public static bool IsCompatible(AgentRunProtocolVersion requested, AgentRunProtocolVersion supported) =>
        requested.Major == supported.Major && requested.Minor <= supported.Minor;
}

[JsonConverter(typeof(AgentRunProtocolVersionJsonConverter))]
public readonly record struct AgentRunProtocolVersion(int Major, int Minor)
{
    public override string ToString() => $"{Major}.{Minor}";

    public static bool TryParse(string? value, out AgentRunProtocolVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            major < 0 || minor < 0)
            return false;

        version = new AgentRunProtocolVersion(major, minor);
        return true;
    }
}

public sealed class AgentRunProtocolVersionJsonConverter : JsonConverter<AgentRunProtocolVersion>
{
    public override AgentRunProtocolVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!AgentRunProtocolVersion.TryParse(value, out var version))
            throw new JsonException("Agent run protocol versions must use major.minor format.");
        return version;
    }

    public override void Write(Utf8JsonWriter writer, AgentRunProtocolVersion value,
        JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

public sealed record AgentRunRequest(string SpecificationHash, AgentRunSpecification Specification);

public sealed record AgentRunSpecification(
    AgentRunProtocolVersion ProtocolVersion,
    string RunId,
    DateTimeOffset RequestedAt,
    AgentRunProject Project,
    AgentRunTask Task,
    AgentRunRepository Repository,
    AgentRunAgent Agent,
    AgentRunRuntime Runtime);

public sealed record AgentRunProject(string ProjectId, string Name);

public sealed record AgentRunTask(string TaskId, string Title, string Revision);

public sealed record AgentRunRepository(string Remote, string BaseCommit);

public sealed record AgentRunAgent(
    string ProviderId,
    string ModelId,
    string EffortId,
    string PromptProfileId);

public sealed record AgentRunRuntime(string RunnerId, AgentRunRuntimeProfile Profile);

public sealed record AgentRunRuntimeProfile(
    string ProfileId,
    string Revision,
    string ImageReference,
    AgentRunResourceLimits Limits,
    string NetworkProfileId,
    IReadOnlyList<AgentRunValidationStep> Validation,
    AgentRunOutputPolicy Output);

public sealed record AgentRunResourceLimits(
    int CpuMillicores,
    long MemoryBytes,
    int Pids,
    long DiskBytes,
    int TimeoutSeconds);

public sealed record AgentRunValidationStep(
    string StepId,
    string DisplayName,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    int TimeoutSeconds);

public sealed record AgentRunOutputPolicy(
    AgentRunOutputMode Mode,
    long MaxPatchBytes,
    bool IncludeEventLog);

[JsonConverter(typeof(JsonStringEnumConverter<AgentRunOutputMode>))]
public enum AgentRunOutputMode
{
    [JsonStringEnumMemberName("patch")]
    Patch,
}

[JsonConverter(typeof(JsonStringEnumConverter<AgentRunState>))]
public enum AgentRunState
{
    [JsonStringEnumMemberName("requested")]
    Requested,

    [JsonStringEnumMemberName("accepted")]
    Accepted,

    [JsonStringEnumMemberName("queued")]
    Queued,

    [JsonStringEnumMemberName("preparing_workspace")]
    PreparingWorkspace,

    [JsonStringEnumMemberName("starting_runtime")]
    StartingRuntime,

    [JsonStringEnumMemberName("starting_agent")]
    StartingAgent,

    [JsonStringEnumMemberName("running")]
    Running,

    [JsonStringEnumMemberName("validating")]
    Validating,

    [JsonStringEnumMemberName("collecting_artifacts")]
    CollectingArtifacts,

    [JsonStringEnumMemberName("completed")]
    Completed,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled,
}

public sealed record AgentRunEvent(
    AgentRunProtocolVersion ProtocolVersion,
    string RunId,
    long Sequence,
    DateTimeOffset Timestamp,
    string Type,
    AgentRunState? State,
    string Summary,
    JsonElement? Data);

public sealed record AgentRunStateTransition(
    AgentRunState PreviousState,
    AgentRunState NextState,
    string? Reason = null);

public sealed record AgentRunArtifact(
    string ArtifactId,
    string Kind,
    string FileName,
    string MediaType,
    long ByteLength,
    string Sha256,
    DateTimeOffset CreatedAt);

public sealed record AgentRunnerCapabilities(
    string RunnerId,
    string DisplayName,
    IReadOnlyList<AgentRunProtocolVersion> ProtocolVersions,
    string OperatingSystem,
    string Architecture,
    bool DockerAvailable,
    AgentRunnerCapacity Capacity,
    IReadOnlyList<AgentRunnerProviderCapability> AgentProviders,
    IReadOnlyList<AgentRunRuntimeProfile> RuntimeProfiles);

public sealed record AgentRunnerCapacity(int MaximumRuns, int ActiveRuns, long MemoryBytes);

public sealed record AgentRunnerProviderCapability(
    string ProviderId,
    IReadOnlyList<string> ModelIds,
    string? DefaultModelId,
    IReadOnlyList<string> EffortIds,
    string? DefaultEffortId);

public enum AgentRunnerConnectivity
{
    Unknown,
    Connecting,
    Online,
    Unreachable,
    Unauthorized,
    Incompatible,
}

public static class AgentRunJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
