using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PM.Application;

namespace PM.AgentRuns;

public enum AgentRunStartDisposition
{
    New,
    Existing,
}

public static partial class AgentRunContractValidator
{
    public static AppResult ValidateRequest(AgentRunRequest request)
    {
        if (request.Specification == null)
            return Invalid("A run specification is required.");

        var specification = ValidateSpecification(request.Specification);
        if (!specification.Success) return specification;
        if (!IsSha256(request.SpecificationHash))
            return Invalid("The specification hash must be a lowercase SHA-256 value.");

        var expected = AgentRunCanonicalJson.ComputeSpecificationHash(request.Specification);
        if (!FixedTimeEquals(expected, request.SpecificationHash))
            return AppResult.Fail("specification_hash_mismatch",
                "The specification hash does not match the canonical run specification.");

        return AppResult.Ok();
    }

    public static AppResult ValidateSpecification(AgentRunSpecification specification)
    {
        if (!AgentRunProtocol.IsCompatible(specification.ProtocolVersion, AgentRunProtocol.Current))
            return AppResult.Fail("incompatible_protocol",
                $"Protocol {specification.ProtocolVersion} is not compatible with {AgentRunProtocol.Current}.");
        if (!RunIdPattern().IsMatch(specification.RunId ?? string.Empty))
            return Invalid("Run IDs must be 1 to 128 URL-safe characters.");
        if (!IsCanonicalTimestamp(specification.RequestedAt))
            return Invalid("Run request timestamps must be UTC with millisecond precision.");

        if (!IsText(specification.Project?.ProjectId, 256) || !IsText(specification.Project?.Name, 512))
            return Invalid("Project ID and name are required.");
        if (!IsText(specification.Task?.TaskId, 256) || !IsText(specification.Task?.Title, 1024) ||
            !IsSha256(specification.Task?.Revision))
            return Invalid("Task ID, title, and lowercase SHA-256 revision are required.");
        if (!IsText(specification.Repository?.Remote, 2048) ||
            !GitCommitPattern().IsMatch(specification.Repository?.BaseCommit ?? string.Empty))
            return Invalid("A repository remote and lowercase 40- or 64-character commit are required.");
        if (!IsIdentifier(specification.Agent?.ProviderId) || !IsIdentifier(specification.Agent?.ModelId) ||
            !IsIdentifier(specification.Agent?.EffortId) || !IsIdentifier(specification.Agent?.PromptProfileId))
            return Invalid("Agent provider, model, effort, and prompt profile IDs are required.");
        var runtime = specification.Runtime;
        if (runtime == null || !IsIdentifier(runtime.RunnerId))
            return Invalid("A runner ID is required.");

        return ValidateProfile(runtime.Profile);
    }

    public static AppResult ValidateProfile(AgentRunRuntimeProfile profile)
    {
        if (profile == null || !IsIdentifier(profile.ProfileId) || !IsSha256(profile.Revision) ||
            !ImageDigestPattern().IsMatch(profile.ImageReference ?? string.Empty))
            return Invalid("Runtime profile identity, revision, image, and network profile are required.");

        var limits = profile.Limits;
        if (limits == null || limits.CpuMillicores <= 0 || limits.MemoryBytes <= 0 || limits.Pids <= 0 ||
            limits.DiskBytes <= 0 || limits.TimeoutSeconds <= 0)
            return Invalid("Runtime resource limits must be greater than zero.");
        if (profile.Network == null || !IsIdentifier(profile.Network.ProfileId) ||
            profile.Network.Mode is not (AgentRunNetworkMode.Offline or AgentRunNetworkMode.Open))
            return Invalid("Runtime network policy is invalid.");
        var container = profile.Container;
        if (container == null || !IsContainerPath(container.WorkspacePath) ||
            !IsContainerPath(container.CodexHomePath) || !IsContainerPath(container.TemporaryPath) ||
            container.TemporaryBytes <= 0 || container.TemporaryBytes > limits.MemoryBytes ||
            container.EnvironmentAllowlist == null || container.EnvironmentAllowlist.Count > 32 ||
            container.EnvironmentAllowlist.Any(name => name == null || !EnvironmentNamePattern().IsMatch(name) ||
                SensitiveEnvironmentNamePattern().IsMatch(name) ||
                name is not ("CODEX_HOME" or "HOME" or "PATH" or "TMPDIR")) ||
            container.EnvironmentAllowlist.Distinct(StringComparer.Ordinal).Count() !=
            container.EnvironmentAllowlist.Count || container.ReadOnlyCaches == null ||
            container.ReadOnlyCaches.Count > 16 || container.Security == null)
            return Invalid("Runtime container policy is invalid.");
        if (container.Security.ReadOnlyRootFilesystem != true ||
            container.Security.UserNamespace != "keep-id" ||
            container.Security.NoNewPrivileges != true ||
            container.Security.DropAllCapabilities != true ||
            container.Security.PrivateNamespaces != true ||
            container.Security.SeccompProfile != "runtime-default" ||
            container.Security.LsmProfile != "none")
            return Invalid("Runtime security policy cannot weaken the protocol 1.0 baseline.");
        var cacheIds = new HashSet<string>(StringComparer.Ordinal);
        var mountPaths = new List<string>
            { container.WorkspacePath, container.CodexHomePath, container.TemporaryPath };
        foreach (var cache in container.ReadOnlyCaches)
        {
            if (cache == null || !IsIdentifier(cache.CacheId) || !cacheIds.Add(cache.CacheId) ||
                !IsContainerPath(cache.ContainerPath))
                return Invalid("Runtime read-only caches are invalid.");
            mountPaths.Add(cache.ContainerPath);
        }
        if (mountPaths.SelectMany((path, index) => mountPaths.Where((_, other) => other != index)
                .Select(other => PathsOverlap(path, other))).Any(overlaps => overlaps))
            return Invalid("Runtime container paths must not overlap.");
        if (profile.Validation == null || profile.Validation.Count > 64)
            return Invalid("Runtime profiles support at most 64 validation steps.");

        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in profile.Validation)
        {
            if (step == null || !IsIdentifier(step.StepId) || !stepIds.Add(step.StepId) ||
                !IsText(step.DisplayName, 512) || !IsText(step.Executable, 1024) ||
                step.Arguments == null || step.Arguments.Count > 128 ||
                step.Arguments.Any(argument => !IsSafeValue(argument, 4096)) ||
                !IsRelativeWorkingDirectory(step.WorkingDirectory) || step.TimeoutSeconds <= 0)
                return Invalid("Runtime validation steps must be unique, bounded, and use relative working directories.");
        }

        if (profile.Output == null || profile.Output.Mode != AgentRunOutputMode.Patch ||
            profile.Output.MaxPatchBytes <= 0)
            return Invalid("V1 run output must use a positive patch-only policy.");

        var expectedRevision = AgentRunCanonicalJson.ComputeProfileRevision(profile);
        if (!FixedTimeEquals(expectedRevision, profile.Revision))
            return AppResult.Fail("profile_revision_mismatch",
                "The runtime profile revision does not match its canonical snapshot.");

        return AppResult.Ok();
    }

    public static AppResult ValidateCapabilities(AgentRunnerCapabilities capabilities)
    {
        if (capabilities == null || !IsIdentifier(capabilities.RunnerId) ||
            !IsText(capabilities.DisplayName, 512) || !IsIdentifier(capabilities.OperatingSystem) ||
            !IsIdentifier(capabilities.Architecture) || capabilities.ProtocolVersions == null ||
            capabilities.ProtocolVersions.Count == 0 ||
            capabilities.ProtocolVersions.Any(version => version.Major < 0 || version.Minor < 0) ||
            capabilities.ProtocolVersions.Distinct().Count() != capabilities.ProtocolVersions.Count)
            return AppResult.Fail("invalid_runner_capabilities", "Runner identity and protocol versions are invalid.");

        var capacity = capabilities.Capacity;
        if (capacity == null || capacity.MaximumRuns <= 0 || capacity.ActiveRuns < 0 ||
            capacity.ActiveRuns > capacity.MaximumRuns || capacity.MemoryBytes <= 0)
            return AppResult.Fail("invalid_runner_capabilities", "Runner capacity is invalid.");

        var runtime = capabilities.ContainerRuntime;
        if (runtime == null || runtime.EngineId != "podman" || !IsText(runtime.Version, 64) ||
            !runtime.Rootless || runtime.CgroupVersion != "v2" || runtime.CgroupManager != "systemd" ||
            !runtime.SeccompEnabled)
            return AppResult.Fail("invalid_runner_capabilities",
                "A rootless Podman runtime with cgroup v2 and seccomp is required.");

        if (capabilities.AgentProviders == null || capabilities.AgentProviders.Count == 0 ||
            capabilities.RuntimeProfiles == null || capabilities.RuntimeProfiles.Count == 0)
            return AppResult.Fail("invalid_runner_capabilities",
                "At least one agent provider and runtime profile must be advertised.");

        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in capabilities.AgentProviders)
        {
            if (provider == null || !IsIdentifier(provider.ProviderId) || !providerIds.Add(provider.ProviderId) ||
                !IsDistinctIdentifiers(provider.ModelIds) || !IsDistinctIdentifiers(provider.EffortIds) ||
                provider.ModelIds.Count == 0 || provider.EffortIds.Count == 0 ||
                provider.DefaultModelId != null && !provider.ModelIds.Contains(provider.DefaultModelId, StringComparer.Ordinal) ||
                provider.DefaultEffortId != null && !provider.EffortIds.Contains(provider.DefaultEffortId, StringComparer.Ordinal))
                return AppResult.Fail("invalid_runner_capabilities", "Agent provider capabilities are invalid.");
        }

        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in capabilities.RuntimeProfiles)
        {
            if (!profileIds.Add(profile.ProfileId))
                return AppResult.Fail("invalid_runner_capabilities", "Runtime profile IDs must be unique.");
            var profileResult = ValidateProfile(profile);
            if (!profileResult.Success)
                return AppResult.Fail("invalid_runner_capabilities", profileResult.Message!);
        }

        return AppResult.Ok();
    }

    public static AppResult ValidateEvent(AgentRunEvent runEvent)
    {
        if (runEvent == null ||
            !AgentRunProtocol.IsCompatible(runEvent.ProtocolVersion, AgentRunProtocol.Current) ||
            !RunIdPattern().IsMatch(runEvent.RunId ?? string.Empty) || runEvent.Sequence <= 0 ||
            !IsCanonicalTimestamp(runEvent.Timestamp) || !IsEventType(runEvent.Type) ||
            !IsText(runEvent.Summary, 4096))
            return AppResult.Fail("invalid_run_event", "The durable run event envelope is invalid.");
        return AppResult.Ok();
    }

    public static AppResult ValidateArtifact(AgentRunArtifact artifact)
    {
        if (artifact == null || !IsIdentifier(artifact.ArtifactId) || !IsIdentifier(artifact.Kind) ||
            !IsText(artifact.FileName, 512) || Path.GetFileName(artifact.FileName) != artifact.FileName ||
            !IsText(artifact.MediaType, 256) || artifact.ByteLength < 0 || !IsSha256(artifact.Sha256) ||
            !IsCanonicalTimestamp(artifact.CreatedAt))
            return AppResult.Fail("invalid_run_artifact", "The run artifact metadata is invalid.");
        return AppResult.Ok();
    }

    public static AppResult<AgentRunStartDisposition> EvaluateStart(
        string? existingSpecificationHash,
        AgentRunRequest request)
    {
        var validation = ValidateRequest(request);
        if (!validation.Success)
            return AppResult<AgentRunStartDisposition>.Fail(validation.ErrorCode!, validation.Message!);
        if (existingSpecificationHash == null)
            return AppResult<AgentRunStartDisposition>.Ok(AgentRunStartDisposition.New);
        if (IsSha256(existingSpecificationHash) && FixedTimeEquals(existingSpecificationHash, request.SpecificationHash))
            return AppResult<AgentRunStartDisposition>.Ok(AgentRunStartDisposition.Existing);

        return AppResult<AgentRunStartDisposition>.Fail("run_id_conflict",
            $"Run ID {request.Specification.RunId} already exists with a different specification.");
    }

    private static bool IsCanonicalTimestamp(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value.Ticks % TimeSpan.TicksPerMillisecond == 0;

    private static bool IsIdentifier(string? value) => IsText(value, 256);

    private static bool IsEventType(string? value) =>
        IsText(value, 256) && value!.Contains('.', StringComparison.Ordinal);

    private static bool IsDistinctIdentifiers(IReadOnlyList<string>? values) =>
        values != null && values.All(IsIdentifier) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value == value.Trim() && IsSafeValue(value, maximumLength);

    private static bool IsSafeValue(string? value, int maximumLength) =>
        value != null && value.Length <= maximumLength && !value.Any(char.IsControl);

    private static bool IsRelativeWorkingDirectory(string? value)
    {
        if (!IsText(value, 1024) || Path.IsPathRooted(value)) return false;
        return !value!.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");
    }

    private static bool IsContainerPath(string? value)
    {
        if (!IsText(value, 1024) || !value!.StartsWith('/') || value == "/" ||
            value.EndsWith('/') || value.Contains("//", StringComparison.Ordinal)) return false;
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or "..")) return false;
        return !new[] { "/proc", "/sys", "/dev", "/run" }
            .Any(path => value == path || value.StartsWith(path + "/", StringComparison.Ordinal));
    }

    private static bool PathsOverlap(string left, string right) =>
        left == right || left.StartsWith(right + "/", StringComparison.Ordinal) ||
        right.StartsWith(left + "/", StringComparison.Ordinal);

    private static bool IsSha256(string? value) => Sha256Pattern().IsMatch(value ?? string.Empty);

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private static AppResult Invalid(string message) => AppResult.Fail("invalid_run_specification", message);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$", RegexOptions.CultureInvariant)]
    private static partial Regex GitCommitPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[^\\s@]+@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageDigestPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentNamePattern();

    [GeneratedRegex("(?:auth|cookie|credential|password|private|secret|signature|token|api.?key)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveEnvironmentNamePattern();
}
