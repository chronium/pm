using System.Text.Json;
using System.Text.RegularExpressions;
using PM.Application;
using PM.Auth;

namespace PM.AgentRuns;

public sealed class AgentRunnerRegistrationStoreOptions
{
    public string? RootPath { get; init; }
}

public sealed partial class AgentRunnerRegistrationStore(AgentRunnerRegistrationStoreOptions? options = null)
{
    private const int SchemaVersion = 1;
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                                      UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _rootPath = ResolveRootPath(options?.RootPath);

    public AppResult<IReadOnlyList<AgentRunnerRegistration>> List()
    {
        var prepared = PrepareRoot(create: false);
        if (!prepared.Success) return AppResult<IReadOnlyList<AgentRunnerRegistration>>.Fail(
            prepared.ErrorCode!, prepared.Message!);
        if (!Directory.Exists(_rootPath))
            return AppResult<IReadOnlyList<AgentRunnerRegistration>>.Ok([]);

        var registrations = new List<AgentRunnerRegistration>();
        foreach (var path in Directory.EnumerateFiles(_rootPath, "*.json").Order(StringComparer.Ordinal))
        {
            var stored = Read(path);
            if (!stored.Success)
                return AppResult<IReadOnlyList<AgentRunnerRegistration>>.Fail(stored.ErrorCode!, stored.Message!);
            registrations.Add(ToSafe(stored.Payload!));
        }

        return AppResult<IReadOnlyList<AgentRunnerRegistration>>.Ok(registrations);
    }

    public AppResult<AgentRunnerRegistration> Get(string runnerId)
    {
        var stored = GetStored(runnerId);
        return stored.Success
            ? AppResult<AgentRunnerRegistration>.Ok(ToSafe(stored.Payload!))
            : AppResult<AgentRunnerRegistration>.Fail(stored.ErrorCode!, stored.Message!);
    }

    internal AppResult<StoredAgentRunnerRegistration> GetStored(string runnerId)
    {
        var normalizedRunnerId = runnerId ?? string.Empty;
        if (!RunnerIdPattern().IsMatch(normalizedRunnerId))
            return AppResult<StoredAgentRunnerRegistration>.Fail("invalid_runner_id", "Runner ID is invalid.");
        var prepared = PrepareRoot(create: false);
        if (!prepared.Success)
            return AppResult<StoredAgentRunnerRegistration>.Fail(prepared.ErrorCode!, prepared.Message!);
        var path = RegistrationPath(normalizedRunnerId);
        return File.Exists(path)
            ? Read(path)
            : AppResult<StoredAgentRunnerRegistration>.Fail("runner_not_registered", $"Runner {runnerId} is not registered.");
    }

    internal AppResult Save(StoredAgentRunnerRegistration registration, bool replace)
    {
        var validation = Validate(registration);
        if (!validation.Success) return validation;
        var prepared = PrepareRoot(create: true);
        if (!prepared.Success) return prepared;
        var path = RegistrationPath(registration.RunnerId);
        if (File.Exists(path) && !replace)
            return AppResult.Fail("runner_already_registered", $"Runner {registration.RunnerId} is already registered.");
        if (File.Exists(path))
        {
            var secure = AssertPrivateFile(path);
            if (!secure.Success) return secure;
        }

        var temporaryPath = Path.Combine(_rootPath, $".{registration.RunnerId}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(registration, JsonOptions));
            SetPrivateFileMode(temporaryPath);
            File.Move(temporaryPath, path, true);
            SetPrivateFileMode(path);
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("runner_registration_write_failed", "The runner registration could not be saved.");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal AppResult Remove(string runnerId)
    {
        var existing = GetStored(runnerId);
        if (!existing.Success) return AppResult.Fail(existing.ErrorCode!, existing.Message!);
        try
        {
            File.Delete(RegistrationPath(runnerId));
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("runner_registration_write_failed", "The runner registration could not be removed.");
        }
    }

    private AppResult<StoredAgentRunnerRegistration> Read(string path)
    {
        var secure = AssertPrivateFile(path);
        if (!secure.Success)
            return AppResult<StoredAgentRunnerRegistration>.Fail(secure.ErrorCode!, secure.Message!);
        try
        {
            var registration = JsonSerializer.Deserialize<StoredAgentRunnerRegistration>(
                File.ReadAllBytes(path), JsonOptions);
            var validation = registration == null
                ? AppResult.Fail("invalid_runner_registration", "A runner registration is invalid.")
                : Validate(registration);
            return validation.Success
                ? AppResult<StoredAgentRunnerRegistration>.Ok(registration!)
                : AppResult<StoredAgentRunnerRegistration>.Fail(validation.ErrorCode!, validation.Message!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppResult<StoredAgentRunnerRegistration>.Fail(
                "invalid_runner_registration", "A runner registration could not be read.");
        }
    }

    private AppResult PrepareRoot(bool create)
    {
        try
        {
            if (!Directory.Exists(_rootPath))
            {
                if (!create) return AppResult.Ok();
                Directory.CreateDirectory(_rootPath);
                SetPrivateDirectoryMode(_rootPath);
            }

            var attributes = File.GetAttributes(_rootPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return AppResult.Fail("insecure_runner_storage", "The runner registration directory cannot be a symbolic link.");
            if (!OperatingSystem.IsWindows() &&
                (File.GetUnixFileMode(_rootPath) & ~PrivateDirectoryMode) != 0)
                return AppResult.Fail("insecure_runner_storage", "The runner registration directory must be owner-only.");
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("runner_registration_unavailable", "Runner registration storage is unavailable.");
        }
    }

    private static AppResult AssertPrivateFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || !File.Exists(path))
                return AppResult.Fail("insecure_runner_storage", "Runner registration files must be regular files.");
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & ~PrivateFileMode) != 0)
                return AppResult.Fail("insecure_runner_storage", "Runner registration files must be owner-only.");
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("runner_registration_unavailable", "Runner registration storage is unavailable.");
        }
    }

    private static AppResult Validate(StoredAgentRunnerRegistration registration)
    {
        if (registration.SchemaVersion != SchemaVersion ||
            !RunnerIdPattern().IsMatch(registration.RunnerId ?? string.Empty) ||
            string.IsNullOrWhiteSpace(registration.DisplayName) || registration.DisplayName.Length > 512 ||
            !TryNormalizeEndpoint(registration.Endpoint, out var endpoint) || endpoint.AbsoluteUri != registration.Endpoint ||
            !FingerprintPattern().IsMatch(registration.TlsFingerprint ?? string.Empty) ||
            registration.ProtocolVersion != AgentRunProtocol.Current ||
            registration.Credential == null ||
            string.IsNullOrWhiteSpace(registration.Credential.ClientId) ||
            registration.Credential.ClientId.Length > 256 ||
            string.IsNullOrWhiteSpace(registration.Credential.DisplayName) ||
            registration.Credential.DisplayName.Length > 512 ||
            string.IsNullOrWhiteSpace(registration.Credential.PrivateKey) ||
            string.IsNullOrWhiteSpace(registration.Credential.PublicKey) ||
            !FingerprintPattern().IsMatch(registration.ClientFingerprint ?? string.Empty) ||
            !AgentRunnerRequestSigning.CredentialMatches(registration.Credential) ||
            registration.Credential.Fingerprint != registration.ClientFingerprint)
            return AppResult.Fail("invalid_runner_registration", "A runner registration is invalid.");
        return AppResult.Ok();
    }

    internal static bool TryNormalizeEndpoint(string? value, out Uri endpoint)
    {
        endpoint = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment) || parsed.AbsolutePath != "/")
            return false;
        endpoint = new UriBuilder(parsed) { Path = "/", Query = string.Empty, Fragment = string.Empty }.Uri;
        return true;
    }

    internal static StoredAgentRunnerRegistration CreateStored(
        string runnerId,
        string displayName,
        Uri endpoint,
        string fingerprint,
        AgentRunProtocolVersion protocolVersion,
        AgentRunnerCredential credential,
        string clientFingerprint,
        DateTimeOffset pairedAt) =>
        new(SchemaVersion, runnerId, displayName, endpoint.AbsoluteUri, fingerprint,
            protocolVersion, credential, clientFingerprint, pairedAt);

    private static AgentRunnerRegistration ToSafe(StoredAgentRunnerRegistration registration) =>
        new(registration.RunnerId, registration.DisplayName, new Uri(registration.Endpoint),
            registration.TlsFingerprint, registration.ProtocolVersion, registration.Credential.ClientId,
            registration.ClientFingerprint, registration.PairedAt);

    private string RegistrationPath(string runnerId) => Path.Combine(_rootPath, $"{runnerId}.json");

    private static string ResolveRootPath(string? configured)
    {
        var value = configured ?? Environment.GetEnvironmentVariable("PM_RUNNERS_PATH")
            ?? Path.Combine(UserConfigurationPaths.GetPmDirectory(), "runners");
        return Path.GetFullPath(value);
    }

    private static void SetPrivateDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, PrivateDirectoryMode);
    }

    private static void SetPrivateFileMode(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, PrivateFileMode);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunnerIdPattern();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();
}

internal sealed record StoredAgentRunnerRegistration(
    int SchemaVersion,
    string RunnerId,
    string DisplayName,
    string Endpoint,
    string TlsFingerprint,
    AgentRunProtocolVersion ProtocolVersion,
    AgentRunnerCredential Credential,
    string ClientFingerprint,
    DateTimeOffset PairedAt);
