using System.Text.Json;
using PM.Auth;
using PM.Project;

namespace PM.Application;

public sealed class LinkedProjectRegistryStoreOptions
{
    public string? RootPath { get; init; }
}

public sealed record LinkedProjectBinding(
    string ProjectId,
    string RepositoryPath,
    DateTimeOffset VerifiedAt,
    bool WriteTrusted = false);

public sealed class LinkedProjectRegistryStore(
    LinkedProjectRegistryStoreOptions? options = null,
    TimeProvider? timeProvider = null)
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
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public AppResult<IReadOnlyList<LinkedProjectBinding>> List()
    {
        var prepared = PrepareRoot(create: false);
        if (!prepared.Success)
            return AppResult<IReadOnlyList<LinkedProjectBinding>>.Fail(prepared.ErrorCode!, prepared.Message!);
        if (!Directory.Exists(_rootPath))
            return AppResult<IReadOnlyList<LinkedProjectBinding>>.Ok([]);

        var bindings = new List<LinkedProjectBinding>();
        foreach (var path in Directory.EnumerateFiles(_rootPath, "*.json").Order(StringComparer.Ordinal))
        {
            var stored = Read(path);
            if (!stored.Success)
                return AppResult<IReadOnlyList<LinkedProjectBinding>>.Fail(stored.ErrorCode!, stored.Message!);
            bindings.Add(ToBinding(stored.Payload!));
        }

        return AppResult<IReadOnlyList<LinkedProjectBinding>>.Ok(bindings);
    }

    public AppResult<LinkedProjectBinding> Get(string projectId)
    {
        var normalized = projectId?.Trim() ?? string.Empty;
        if (!ProjectIdentifiers.IsValid(normalized))
            return AppResult<LinkedProjectBinding>.Fail("invalid_project_id", "Project ID is invalid.");

        var prepared = PrepareRoot(create: false);
        if (!prepared.Success)
            return AppResult<LinkedProjectBinding>.Fail(prepared.ErrorCode!, prepared.Message!);

        var path = BindingPath(normalized);
        if (!File.Exists(path))
            return AppResult<LinkedProjectBinding>.Fail(
                "project_not_registered", $"Project {normalized} is not registered on this machine.");

        var stored = Read(path);
        return stored.Success
            ? AppResult<LinkedProjectBinding>.Ok(ToBinding(stored.Payload!))
            : AppResult<LinkedProjectBinding>.Fail(stored.ErrorCode!, stored.Message!);
    }

    public AppResult<LinkedProjectBinding> Bind(string projectId, string repositoryPath, bool replace = false)
    {
        var normalizedProjectId = projectId?.Trim() ?? string.Empty;
        if (!ProjectIdentifiers.IsValid(normalizedProjectId))
            return AppResult<LinkedProjectBinding>.Fail("invalid_project_id", "Project ID is invalid.");

        var opened = OpenAndVerify(normalizedProjectId, repositoryPath);
        if (!opened.Success)
            return AppResult<LinkedProjectBinding>.Fail(opened.ErrorCode!, opened.Message!);

        var existing = Get(normalizedProjectId);
        if (existing.Success &&
            !PathsEqual(existing.Payload!.RepositoryPath, opened.Payload!.RepositoryPath) && !replace)
            return AppResult<LinkedProjectBinding>.Fail(
                "project_binding_exists",
                $"Project {normalizedProjectId} is already bound to another repository. Use --replace to change it.");
        if (!existing.Success && existing.ErrorCode is not "project_not_registered")
            return AppResult<LinkedProjectBinding>.Fail(existing.ErrorCode!, existing.Message!);

        return Save(new StoredLinkedProjectBinding(
            SchemaVersion,
            normalizedProjectId,
            opened.Payload!.RepositoryPath,
            _timeProvider.GetUtcNow(),
            false));
    }

    public AppResult<LinkedProjectBinding> GrantWriteTrust(string projectId)
    {
        var existing = Get(projectId);
        if (!existing.Success)
            return AppResult<LinkedProjectBinding>.Fail(existing.ErrorCode!, existing.Message!);

        var opened = OpenAndVerify(existing.Payload!.ProjectId, existing.Payload.RepositoryPath);
        if (!opened.Success)
            return AppResult<LinkedProjectBinding>.Fail(opened.ErrorCode!, opened.Message!);

        return Save(new StoredLinkedProjectBinding(
            SchemaVersion,
            existing.Payload.ProjectId,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(opened.Payload!.RepositoryPath)),
            _timeProvider.GetUtcNow(),
            true));
    }

    public AppResult<LinkedProjectBinding> RevokeWriteTrust(string projectId)
    {
        var existing = Get(projectId);
        if (!existing.Success)
            return AppResult<LinkedProjectBinding>.Fail(existing.ErrorCode!, existing.Message!);

        return Save(new StoredLinkedProjectBinding(
            SchemaVersion,
            existing.Payload!.ProjectId,
            existing.Payload.RepositoryPath,
            existing.Payload.VerifiedAt,
            false));
    }

    public AppResult<ProjectRoot> OpenWriteTrusted(string projectId)
    {
        var existing = Get(projectId);
        if (!existing.Success)
            return AppResult<ProjectRoot>.Fail(existing.ErrorCode!, existing.Message!);
        if (!existing.Payload!.WriteTrusted)
            return AppResult<ProjectRoot>.Fail(
                "linked_project_write_untrusted",
                $"Project {existing.Payload.ProjectId} is not trusted for local writes.");

        return OpenAndVerify(existing.Payload.ProjectId, existing.Payload.RepositoryPath);
    }

    public AppResult<LinkedProjectBinding> Remember(ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists || !projectRoot.TryReadProjectId(out var projectId))
            return AppResult<LinkedProjectBinding>.Fail(
                "missing_project_id", "The active project does not have a valid stable project ID.");

        return Save(new StoredLinkedProjectBinding(
            SchemaVersion,
            projectId,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot.RepositoryPath)),
            _timeProvider.GetUtcNow(),
            false));
    }

    public AppResult Remove(string projectId)
    {
        var existing = Get(projectId);
        if (!existing.Success) return AppResult.Fail(existing.ErrorCode!, existing.Message!);

        try
        {
            File.Delete(BindingPath(existing.Payload!.ProjectId));
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("project_registry_write_failed", "The project binding could not be removed.");
        }
    }

    private static AppResult<ProjectRoot> OpenAndVerify(string projectId, string repositoryPath)
    {
        string canonicalPath;
        try
        {
            canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return AppResult<ProjectRoot>.Fail("invalid_project_path", "The repository path is invalid.");
        }

        if (!Directory.Exists(canonicalPath))
            return AppResult<ProjectRoot>.Fail("missing_project_path", "The repository path does not exist.");
        if (!ProjectRoot.TryOpenExact(canonicalPath, out var root))
            return AppResult<ProjectRoot>.Fail(
                "invalid_project_root", "The repository path is not an initialized, readable PM project root.");
        if (!root.TryReadProjectId(out var actualProjectId))
            return AppResult<ProjectRoot>.Fail("missing_project_id", "The repository has no valid stable project ID.");
        if (!string.Equals(actualProjectId, projectId, StringComparison.Ordinal))
            return AppResult<ProjectRoot>.Fail(
                "project_identity_mismatch",
                $"The repository identifies as {actualProjectId}, not {projectId}.");

        return AppResult<ProjectRoot>.Ok(root);
    }

    private AppResult<LinkedProjectBinding> Save(StoredLinkedProjectBinding binding)
    {
        var validation = Validate(binding);
        if (!validation.Success)
            return AppResult<LinkedProjectBinding>.Fail(validation.ErrorCode!, validation.Message!);
        var prepared = PrepareRoot(create: true);
        if (!prepared.Success)
            return AppResult<LinkedProjectBinding>.Fail(prepared.ErrorCode!, prepared.Message!);

        var path = BindingPath(binding.ProjectId);
        if (File.Exists(path))
        {
            var secure = AssertPrivateFile(path);
            if (!secure.Success)
                return AppResult<LinkedProjectBinding>.Fail(secure.ErrorCode!, secure.Message!);
        }

        var temporaryPath = Path.Combine(_rootPath, $".{binding.ProjectId}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(binding, JsonOptions));
            SetPrivateFileMode(temporaryPath);
            File.Move(temporaryPath, path, true);
            SetPrivateFileMode(path);
            return AppResult<LinkedProjectBinding>.Ok(ToBinding(binding));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult<LinkedProjectBinding>.Fail(
                "project_registry_write_failed", "The project binding could not be saved.");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private AppResult<StoredLinkedProjectBinding> Read(string path)
    {
        var secure = AssertPrivateFile(path);
        if (!secure.Success)
            return AppResult<StoredLinkedProjectBinding>.Fail(secure.ErrorCode!, secure.Message!);

        try
        {
            var binding = JsonSerializer.Deserialize<StoredLinkedProjectBinding>(File.ReadAllBytes(path), JsonOptions);
            var validation = binding == null
                ? AppResult.Fail("invalid_project_binding", "A project binding is invalid.")
                : Validate(binding);
            return validation.Success
                ? AppResult<StoredLinkedProjectBinding>.Ok(binding!)
                : AppResult<StoredLinkedProjectBinding>.Fail(validation.ErrorCode!, validation.Message!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppResult<StoredLinkedProjectBinding>.Fail(
                "invalid_project_binding", "A project binding could not be read.");
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
                return AppResult.Fail("insecure_project_registry", "The project registry cannot be a symbolic link.");
            if (!OperatingSystem.IsWindows() &&
                (File.GetUnixFileMode(_rootPath) & ~PrivateDirectoryMode) != 0)
                return AppResult.Fail("insecure_project_registry", "The project registry must be owner-only.");
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("project_registry_unavailable", "Project registry storage is unavailable.");
        }
    }

    private static AppResult AssertPrivateFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || !File.Exists(path))
                return AppResult.Fail("insecure_project_registry", "Project bindings must be regular files.");
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & ~PrivateFileMode) != 0)
                return AppResult.Fail("insecure_project_registry", "Project bindings must be owner-only.");
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("project_registry_unavailable", "Project registry storage is unavailable.");
        }
    }

    private static AppResult Validate(StoredLinkedProjectBinding binding)
    {
        if (binding.SchemaVersion != SchemaVersion || !ProjectIdentifiers.IsValid(binding.ProjectId) ||
            string.IsNullOrWhiteSpace(binding.RepositoryPath) || !Path.IsPathFullyQualified(binding.RepositoryPath))
            return AppResult.Fail("invalid_project_binding", "A project binding is invalid.");
        return AppResult.Ok();
    }

    private string BindingPath(string projectId) => Path.Combine(_rootPath, $"{projectId}.json");

    private static LinkedProjectBinding ToBinding(StoredLinkedProjectBinding stored) =>
        new(stored.ProjectId, stored.RepositoryPath, stored.VerifiedAt, stored.WriteTrusted);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string ResolveRootPath(string? configured)
    {
        var value = configured ?? Environment.GetEnvironmentVariable("PM_PROJECT_REGISTRY_PATH")
            ?? Path.Combine(UserConfigurationPaths.GetPmDirectory(), "projects");
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
}

internal sealed record StoredLinkedProjectBinding(
    int SchemaVersion,
    string ProjectId,
    string RepositoryPath,
    DateTimeOffset VerifiedAt,
    bool WriteTrusted);
