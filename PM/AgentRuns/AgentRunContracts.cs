using System.Text.Json;
using System.Text.Json.Serialization;

namespace PM.AgentRuns;

public static class AgentRunProtocol
{
    public static readonly AgentRunProtocolVersion Version10 = new(1, 0);
    public static readonly AgentRunProtocolVersion Version11 = new(1, 1);
    public static readonly AgentRunProtocolVersion Current = new(1, 2);
    public static IReadOnlyList<AgentRunProtocolVersion> Supported { get; } = [Current, Version11, Version10];

    public static bool IsCompatible(AgentRunProtocolVersion requested, AgentRunProtocolVersion supported) =>
        requested.Major == supported.Major && requested.Minor <= supported.Minor;

    public static AgentRunProtocolVersion? HighestCommon(IEnumerable<AgentRunProtocolVersion> versions) =>
        Supported.FirstOrDefault(candidate => versions.Contains(candidate)) is var selected && selected != default
            ? selected
            : null;
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
    AgentRunRuntime Runtime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AgentRunLinkedContext>? LinkedContexts = null);

public sealed record AgentRunProject(string ProjectId, string Name);

public sealed record AgentRunTask(string TaskId, string Title, string Revision);

public sealed record AgentRunRepository(string Remote, string BaseCommit);

public sealed record AgentRunLinkedContext(
    string ProjectId,
    string Name,
    string Alias,
    AgentRunRepository Repository,
    AgentRunLinkedContextRequirement Requirement,
    IReadOnlyList<AgentRunLinkedContextScope> Scopes);

[JsonConverter(typeof(JsonStringEnumConverter<AgentRunLinkedContextRequirement>))]
public enum AgentRunLinkedContextRequirement
{
    [JsonStringEnumMemberName("required")]
    Required,

    [JsonStringEnumMemberName("optional")]
    Optional,
}

[JsonConverter(typeof(JsonStringEnumConverter<AgentRunLinkedContextScope>))]
public enum AgentRunLinkedContextScope
{
    [JsonStringEnumMemberName("wiki")]
    Wiki,
}

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
    AgentRunNetworkPolicy Network,
    AgentRunContainerPolicy Container,
    IReadOnlyList<AgentRunValidationStep> Validation,
    AgentRunOutputPolicy Output);

public sealed record AgentRunNetworkPolicy(string ProfileId, AgentRunNetworkMode Mode);

[JsonConverter(typeof(JsonStringEnumConverter<AgentRunNetworkMode>))]
public enum AgentRunNetworkMode
{
    [JsonStringEnumMemberName("offline")]
    Offline,

    [JsonStringEnumMemberName("open")]
    Open,
}

public sealed record AgentRunContainerPolicy(
    string WorkspacePath,
    string CodexHomePath,
    string TemporaryPath,
    long TemporaryBytes,
    IReadOnlyList<string> EnvironmentAllowlist,
    IReadOnlyList<AgentRunCacheMount> ReadOnlyCaches,
    AgentRunContainerSecurityPolicy Security);

public sealed record AgentRunCacheMount(string CacheId, string ContainerPath);

public sealed record AgentRunContainerSecurityPolicy(
    bool ReadOnlyRootFilesystem,
    string UserNamespace,
    bool NoNewPrivileges,
    bool DropAllCapabilities,
    bool PrivateNamespaces,
    string SeccompProfile,
    string LsmProfile);

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
    AgentContainerRuntimeCapability ContainerRuntime,
    AgentRunnerCapacity Capacity,
    IReadOnlyList<AgentRunnerProviderCapability> AgentProviders,
    IReadOnlyList<AgentRunRuntimeProfile> RuntimeProfiles);

public sealed record AgentContainerRuntimeCapability(
    string EngineId,
    string Version,
    bool Rootless,
    string CgroupVersion,
    string CgroupManager,
    bool SeccompEnabled,
    bool SelinuxEnabled,
    bool AppArmorEnabled);

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
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new AgentRunTimestampJsonConverter());
        return options;
    }
}

public sealed class AgentRunTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : throw new JsonException("Agent run timestamps must use ISO 8601 format.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            System.Globalization.CultureInfo.InvariantCulture));
}
