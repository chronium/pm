using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PM.AgentRuns;

public static class AgentRunCanonicalJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static string ComputeSpecificationHash(AgentRunSpecification specification) =>
        Hash(WriteSpecification(specification));

    public static string ComputeProfileRevision(AgentRunRuntimeProfile profile) =>
        Hash(WriteProfile(profile, includeRevision: false));

    public static byte[] WriteSpecification(AgentRunSpecification specification)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("protocolVersion", specification.ProtocolVersion.ToString());
            writer.WriteString("runId", specification.RunId);
            WriteTimestamp(writer, "requestedAt", specification.RequestedAt);

            writer.WritePropertyName("project");
            writer.WriteStartObject();
            writer.WriteString("projectId", specification.Project.ProjectId);
            writer.WriteString("name", specification.Project.Name);
            writer.WriteEndObject();

            writer.WritePropertyName("task");
            writer.WriteStartObject();
            writer.WriteString("taskId", specification.Task.TaskId);
            writer.WriteString("title", specification.Task.Title);
            writer.WriteString("revision", specification.Task.Revision);
            writer.WriteEndObject();

            writer.WritePropertyName("repository");
            writer.WriteStartObject();
            writer.WriteString("remote", specification.Repository.Remote);
            writer.WriteString("baseCommit", specification.Repository.BaseCommit);
            writer.WriteEndObject();

            if (specification.ProtocolVersion.Minor >= AgentRunProtocol.Current.Minor)
            {
                writer.WritePropertyName("linkedContexts");
                writer.WriteStartArray();
                foreach (var context in specification.LinkedContexts ?? [])
                {
                    writer.WriteStartObject();
                    writer.WriteString("projectId", context.ProjectId);
                    writer.WriteString("name", context.Name);
                    writer.WriteString("alias", context.Alias);
                    writer.WritePropertyName("repository");
                    writer.WriteStartObject();
                    writer.WriteString("remote", context.Repository.Remote);
                    writer.WriteString("baseCommit", context.Repository.BaseCommit);
                    writer.WriteEndObject();
                    writer.WriteString("requirement", context.Requirement == AgentRunLinkedContextRequirement.Required
                        ? "required"
                        : "optional");
                    writer.WritePropertyName("scopes");
                    writer.WriteStartArray();
                    foreach (var scope in context.Scopes)
                        writer.WriteStringValue(scope == AgentRunLinkedContextScope.Wiki ? "wiki" : null);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WritePropertyName("agent");
            writer.WriteStartObject();
            writer.WriteString("providerId", specification.Agent.ProviderId);
            writer.WriteString("modelId", specification.Agent.ModelId);
            writer.WriteString("effortId", specification.Agent.EffortId);
            writer.WriteString("promptProfileId", specification.Agent.PromptProfileId);
            writer.WriteEndObject();

            writer.WritePropertyName("runtime");
            writer.WriteStartObject();
            writer.WriteString("runnerId", specification.Runtime.RunnerId);
            writer.WritePropertyName("profile");
            WriteProfile(writer, specification.Runtime.Profile, includeRevision: true);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] WriteProfile(AgentRunRuntimeProfile profile, bool includeRevision)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            WriteProfile(writer, profile, includeRevision);
        return stream.ToArray();
    }

    private static void WriteProfile(Utf8JsonWriter writer, AgentRunRuntimeProfile profile, bool includeRevision)
    {
        writer.WriteStartObject();
        writer.WriteString("profileId", profile.ProfileId);
        if (includeRevision) writer.WriteString("revision", profile.Revision);
        writer.WriteString("imageReference", profile.ImageReference);

        writer.WritePropertyName("limits");
        writer.WriteStartObject();
        writer.WriteNumber("cpuMillicores", profile.Limits.CpuMillicores);
        writer.WriteNumber("memoryBytes", profile.Limits.MemoryBytes);
        writer.WriteNumber("pids", profile.Limits.Pids);
        writer.WriteNumber("diskBytes", profile.Limits.DiskBytes);
        writer.WriteNumber("timeoutSeconds", profile.Limits.TimeoutSeconds);
        writer.WriteEndObject();

        writer.WritePropertyName("network");
        writer.WriteStartObject();
        writer.WriteString("profileId", profile.Network.ProfileId);
        writer.WriteString("mode", profile.Network.Mode == AgentRunNetworkMode.Offline ? "offline" : "open");
        writer.WriteEndObject();

        writer.WritePropertyName("container");
        writer.WriteStartObject();
        writer.WriteString("workspacePath", profile.Container.WorkspacePath);
        writer.WriteString("codexHomePath", profile.Container.CodexHomePath);
        writer.WriteString("temporaryPath", profile.Container.TemporaryPath);
        writer.WriteNumber("temporaryBytes", profile.Container.TemporaryBytes);
        writer.WritePropertyName("environmentAllowlist");
        writer.WriteStartArray();
        foreach (var name in profile.Container.EnvironmentAllowlist) writer.WriteStringValue(name);
        writer.WriteEndArray();
        writer.WritePropertyName("readOnlyCaches");
        writer.WriteStartArray();
        foreach (var cache in profile.Container.ReadOnlyCaches)
        {
            writer.WriteStartObject();
            writer.WriteString("cacheId", cache.CacheId);
            writer.WriteString("containerPath", cache.ContainerPath);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("security");
        writer.WriteStartObject();
        writer.WriteBoolean("readOnlyRootFilesystem", profile.Container.Security.ReadOnlyRootFilesystem);
        writer.WriteString("userNamespace", profile.Container.Security.UserNamespace);
        writer.WriteBoolean("noNewPrivileges", profile.Container.Security.NoNewPrivileges);
        writer.WriteBoolean("dropAllCapabilities", profile.Container.Security.DropAllCapabilities);
        writer.WriteBoolean("privateNamespaces", profile.Container.Security.PrivateNamespaces);
        writer.WriteString("seccompProfile", profile.Container.Security.SeccompProfile);
        writer.WriteString("lsmProfile", profile.Container.Security.LsmProfile);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WritePropertyName("validation");
        writer.WriteStartArray();
        foreach (var step in profile.Validation)
        {
            writer.WriteStartObject();
            writer.WriteString("stepId", step.StepId);
            writer.WriteString("displayName", step.DisplayName);
            writer.WriteString("executable", step.Executable);
            writer.WritePropertyName("arguments");
            writer.WriteStartArray();
            foreach (var argument in step.Arguments) writer.WriteStringValue(argument);
            writer.WriteEndArray();
            writer.WriteString("workingDirectory", step.WorkingDirectory);
            writer.WriteNumber("timeoutSeconds", step.TimeoutSeconds);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("output");
        writer.WriteStartObject();
        writer.WriteString("mode", "patch");
        writer.WriteNumber("maxPatchBytes", profile.Output.MaxPatchBytes);
        writer.WriteBoolean("includeEventLog", profile.Output.IncludeEventLog);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTimestamp(Utf8JsonWriter writer, string propertyName, DateTimeOffset value) =>
        writer.WriteString(propertyName, value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
