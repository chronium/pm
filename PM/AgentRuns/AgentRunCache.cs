using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PM.Application;
using PM.Auth;
using PM.Project;

namespace PM.AgentRuns;

public sealed class AgentRunCacheOptions
{
    public string? RootPath { get; init; }
    public TimeSpan DraftLifetime { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan TerminalLifetime { get; init; } = TimeSpan.FromDays(30);
}

public sealed record AgentRunCacheRecord(
    int SchemaVersion,
    string RunId,
    string ProjectId,
    AgentRunSelection Selection,
    AgentRunRequest Request,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AgentRunnerRun? RemoteRun,
    long LastObservedSequence,
    AgentRunPatchCollectionResult? PatchCollection = null);

public sealed class AgentRunTemporaryFile(string path) : IAsyncDisposable
{
    public string Path { get; } = path;

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return ValueTask.CompletedTask;
    }
}

public sealed partial class AgentRunCache(
    ProjectRoot projectRoot,
    TimeProvider timeProvider,
    AgentRunCacheOptions? options = null)
{
    private const int SchemaVersion = 1;
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                                      UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly JsonSerializerOptions JsonOptions = new(AgentRunJson.Options)
    {
        PropertyNameCaseInsensitive = false,
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly AgentRunCacheOptions _options = options ?? new AgentRunCacheOptions();

    public async Task<AppResult> SaveDraft(AgentRunSelection selection, AgentRunRequest request)
    {
        var context = Context();
        if (!context.Success) return AppResult.Fail(context.ErrorCode!, context.Message!);
        var now = CanonicalNow();
        return await Write(context.Payload!.Directory, new AgentRunCacheRecord(
            SchemaVersion, request.Specification.RunId, context.Payload.ProjectId, selection,
            request, now, now, null, 0), replace: false);
    }

    public async Task<AppResult<AgentRunCacheRecord>> Get(string runId)
    {
        if (!RunIdPattern().IsMatch(runId ?? string.Empty))
            return AppResult<AgentRunCacheRecord>.Fail("invalid_run_id", "Run ID is invalid.");
        var context = Context();
        if (!context.Success)
            return AppResult<AgentRunCacheRecord>.Fail(context.ErrorCode!, context.Message!);
        await _gate.WaitAsync();
        try
        {
            var prepared = PrepareDirectory(context.Payload!.Directory, create: false);
            if (!prepared.Success)
                return AppResult<AgentRunCacheRecord>.Fail(prepared.ErrorCode!, prepared.Message!);
            var path = Path.Combine(context.Payload.Directory, $"{runId}.json");
            if (!File.Exists(path))
                return AppResult<AgentRunCacheRecord>.Fail("missing_run", $"Run {runId} was not found.");
            return Read(path, context.Payload.ProjectId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppResult> UpdateRemote(string runId, AgentRunnerRun run)
    {
        await _mutationGate.WaitAsync();
        try
        {
            var current = await Get(runId);
            if (!current.Success) return AppResult.Fail(current.ErrorCode!, current.Message!);
            if (run.RunId != runId || run.SpecificationHash != current.Payload!.Request.SpecificationHash)
                return AppResult.Fail("invalid_runner_response", "The runner returned a mismatched run.");
            return await Replace(current.Payload with
            {
                RemoteRun = run,
                UpdatedAt = CanonicalNow(),
                LastObservedSequence = Math.Max(current.Payload.LastObservedSequence, run.LastEventSequence),
            });
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<AppResult> AdvanceSequence(string runId, long sequence)
    {
        if (sequence < 0) return AppResult.Fail("invalid_event_sequence", "Event sequence cannot be negative.");
        await _mutationGate.WaitAsync();
        try
        {
            var current = await Get(runId);
            if (!current.Success) return AppResult.Fail(current.ErrorCode!, current.Message!);
            if (sequence < current.Payload!.LastObservedSequence)
                return AppResult.Fail("invalid_event_sequence", "Event sequence cannot move backwards.");
            return await Replace(current.Payload with
            {
                LastObservedSequence = sequence,
                UpdatedAt = CanonicalNow(),
            });
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<AppResult> RecordPatchCollection(string runId, AgentRunPatchCollectionResult collection)
    {
        await _mutationGate.WaitAsync();
        try
        {
            var current = await Get(runId);
            if (!current.Success) return AppResult.Fail(current.ErrorCode!, current.Message!);
            if (current.Payload!.PatchCollection != null)
                return AppResult.Fail("patch_already_collected", "This run's patch was already collected.");
            if (collection.RunId != runId || collection.ArtifactId.Length == 0 ||
                collection.ArtifactSha256.Length != 64 || collection.BaseCommit.Length != 40 ||
                collection.HeadCommit.Length != 40 || collection.Paths.Count == 0 ||
                collection.Paths.Any(path => path.Length == 0 || path.Any(char.IsControl) ||
                                             Path.IsPathFullyQualified(path) ||
                                             path.Split('/').Any(segment => segment is "." or "..")))
                return AppResult.Fail("invalid_patch_collection", "The patch collection result is invalid.");
            return await Replace(current.Payload with
            {
                PatchCollection = collection,
                UpdatedAt = CanonicalNow(),
            });
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public AppResult<AgentRunTemporaryFile> CreateTemporaryFile(string runId, string purpose)
    {
        if (!RunIdPattern().IsMatch(runId ?? string.Empty) ||
            !TemporaryPurposePattern().IsMatch(purpose ?? string.Empty))
            return AppResult<AgentRunTemporaryFile>.Fail("invalid_run_id",
                "Run temporary file input is invalid.");
        var context = Context();
        if (!context.Success)
            return AppResult<AgentRunTemporaryFile>.Fail(context.ErrorCode!, context.Message!);
        try
        {
            var prepared = PrepareDirectory(context.Payload!.Directory, create: true);
            if (!prepared.Success)
                return AppResult<AgentRunTemporaryFile>.Fail(prepared.ErrorCode!, prepared.Message!);
            var path = Path.Combine(context.Payload.Directory,
                $".{runId}.{purpose}.{Guid.NewGuid():N}.tmp");
            using (File.Create(path)) { }
            SetPrivateFileMode(path);
            return AppResult<AgentRunTemporaryFile>.Ok(new AgentRunTemporaryFile(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult<AgentRunTemporaryFile>.Fail("run_cache_unavailable",
                "A private temporary run file could not be created.");
        }
    }

    public async Task<AppResult> Prune()
    {
        var context = Context();
        if (!context.Success) return AppResult.Fail(context.ErrorCode!, context.Message!);
        await _gate.WaitAsync();
        try
        {
            var prepared = PrepareDirectory(context.Payload!.Directory, create: false);
            if (!prepared.Success || !Directory.Exists(context.Payload.Directory)) return prepared;
            var now = timeProvider.GetUtcNow();
            foreach (var path in Directory.EnumerateFiles(context.Payload.Directory, "*.json"))
            {
                var record = Read(path, context.Payload.ProjectId);
                if (!record.Success) continue;
                var value = record.Payload!;
                var expiredDraft = value.RemoteRun == null && now - value.CreatedAt > _options.DraftLifetime;
                var expiredTerminal = value.RemoteRun != null && AgentRunLifecycle.IsTerminal(value.RemoteRun.State) &&
                                      now - value.UpdatedAt > _options.TerminalLifetime;
                if (expiredDraft || expiredTerminal) File.Delete(path);
            }
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("run_cache_unavailable", "The private run cache is unavailable.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppResult> Replace(AgentRunCacheRecord record)
    {
        var context = Context();
        if (!context.Success) return AppResult.Fail(context.ErrorCode!, context.Message!);
        return await Write(context.Payload!.Directory, record, replace: true);
    }

    private async Task<AppResult> Write(string directory, AgentRunCacheRecord record, bool replace)
    {
        var validation = Validate(record, record.ProjectId);
        if (!validation.Success) return validation;
        await _gate.WaitAsync();
        try
        {
            var prepared = PrepareDirectory(directory, create: true);
            if (!prepared.Success) return prepared;
            var path = Path.Combine(directory, $"{record.RunId}.json");
            if (File.Exists(path) && !replace)
                return AppResult.Fail("duplicate_run", $"Run {record.RunId} already exists.");
            if (File.Exists(path))
            {
                var secure = AssertPrivateFile(path);
                if (!secure.Success) return secure;
            }
            var temporary = Path.Combine(directory, $".{record.RunId}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions));
                SetPrivateFileMode(temporary);
                File.Move(temporary, path, true);
                SetPrivateFileMode(path);
                return AppResult.Ok();
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("run_cache_unavailable", "The private run cache is unavailable.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private AppResult<AgentRunCacheRecord> Read(string path, string projectId)
    {
        var secure = AssertPrivateFile(path);
        if (!secure.Success)
            return AppResult<AgentRunCacheRecord>.Fail(secure.ErrorCode!, secure.Message!);
        try
        {
            var record = JsonSerializer.Deserialize<AgentRunCacheRecord>(File.ReadAllBytes(path), JsonOptions);
            var validation = record == null
                ? AppResult.Fail("invalid_run_cache", "A cached run is invalid.")
                : Validate(record, projectId);
            return validation.Success
                ? AppResult<AgentRunCacheRecord>.Ok(record!)
                : AppResult<AgentRunCacheRecord>.Fail(validation.ErrorCode!, validation.Message!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppResult<AgentRunCacheRecord>.Fail("invalid_run_cache", "A cached run could not be read.");
        }
    }

    private static AppResult Validate(AgentRunCacheRecord record, string projectId)
    {
        if (record.Request?.Specification == null || record.Selection == null)
            return AppResult.Fail("invalid_run_cache", "A cached run is invalid.");
        if (record.SchemaVersion != SchemaVersion || record.ProjectId != projectId ||
            !RunIdPattern().IsMatch(record.RunId ?? string.Empty) ||
            record.RunId != record.Request.Specification.RunId || record.LastObservedSequence < 0 ||
            record.Request.Specification.Project.ProjectId != projectId ||
            record.Selection.RunnerId != record.Request.Specification.Runtime.RunnerId ||
            record.Selection.TaskId != record.Request.Specification.Task.TaskId ||
            record.Selection.ProfileId != record.Request.Specification.Runtime.Profile.ProfileId ||
            record.Selection.ProviderId != record.Request.Specification.Agent.ProviderId ||
            record.Selection.ModelId != record.Request.Specification.Agent.ModelId ||
            record.Selection.EffortId != record.Request.Specification.Agent.EffortId ||
            record.RemoteRun != null && (record.RemoteRun.RunId != record.RunId ||
                                         record.RemoteRun.SpecificationHash != record.Request.SpecificationHash) ||
            record.PatchCollection != null && (record.PatchCollection.RunId != record.RunId ||
                                                record.PatchCollection.ArtifactId.Length == 0 ||
                                                record.PatchCollection.ArtifactSha256.Length != 64 ||
                                                record.PatchCollection.BaseCommit.Length != 40 ||
                                                record.PatchCollection.HeadCommit.Length != 40 ||
                                                record.PatchCollection.Paths.Count == 0 ||
                                                record.PatchCollection.Paths.Any(path => path.Length == 0 ||
                                                    path.Any(char.IsControl) || Path.IsPathFullyQualified(path))) ||
            !AgentRunContractValidator.ValidateRequest(record.Request).Success)
            return AppResult.Fail("invalid_run_cache", "A cached run is invalid.");
        return AppResult.Ok();
    }

    private AppResult<CacheContext> Context()
    {
        if (!projectRoot.Exists || projectRoot.RootPath == null)
            return AppResult<CacheContext>.Fail("missing_project", "Project not found. Run pm init first.");
        try
        {
            var projectId = File.ReadAllText(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile)).Trim();
            if (projectId.Length == 0 || projectId.Length > 256 || projectId.Any(char.IsControl))
                return AppResult<CacheContext>.Fail("invalid_project_id", "The project ID is invalid.");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectId))).ToLowerInvariant();
            var root = _options.RootPath ?? Path.Combine(UserConfigurationPaths.GetPmDirectory(), "runs");
            return AppResult<CacheContext>.Ok(new CacheContext(projectId, Path.Combine(Path.GetFullPath(root), hash)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult<CacheContext>.Fail("missing_project_id", "The project ID could not be read.");
        }
    }

    private static AppResult PrepareDirectory(string path, bool create)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                if (!create) return AppResult.Ok();
                Directory.CreateDirectory(path);
                SetPrivateDirectoryMode(path);
            }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & ~PrivateDirectoryMode) != 0)
                return AppResult.Fail("insecure_run_cache", "The run cache directory must be owner-only and cannot be a symbolic link.");
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("run_cache_unavailable", "The private run cache is unavailable.");
        }
    }

    private static AppResult AssertPrivateFile(string path)
    {
        try
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & ~PrivateFileMode) != 0)
                return AppResult.Fail("insecure_run_cache", "Run cache files must be owner-only regular files.");
            return AppResult.Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult.Fail("run_cache_unavailable", "The private run cache is unavailable.");
        }
    }

    private DateTimeOffset CanonicalNow()
    {
        var now = timeProvider.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second,
            now.Millisecond, TimeSpan.Zero);
    }

    private static void SetPrivateDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, PrivateDirectoryMode);
    }

    private static void SetPrivateFileMode(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, PrivateFileMode);
    }

    private sealed record CacheContext(string ProjectId, string Directory);

    [GeneratedRegex("^run-[A-Za-z0-9._-]{1,124}$", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex TemporaryPurposePattern();
}
