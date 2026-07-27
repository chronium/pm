using System.Net;
using Microsoft.Extensions.Options;
using PM.Auth;
using PM.Files;
using PM.Project;
using PM.Worker;
using Spectre.Console;

namespace PM.Tasks;

public sealed record ProjectRegistration(string ProjectId, string? RecoveryKey);

public interface INextIdService
{
    Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default);
    Task<int> PeekNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default);
    Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default);
    Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot, CancellationToken cancellationToken = default);
    Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default);
}

public sealed class NextIdServiceOptions
{
    public bool WriteFailuresToConsole { get; set; } = true;
}

public class NextIdService : INextIdService
{
    private readonly IPmWorkerClient _worker;
    private readonly IIdentityService _identityService;
    private readonly NextIdServiceOptions _options;

    public NextIdService(IPmWorkerClient worker, IIdentityService identityService,
        IOptions<NextIdServiceOptions>? options = null)
    {
        _worker = worker;
        _identityService = identityService;
        _options = options?.Value ?? new NextIdServiceOptions();
    }

    public async Task<int> GetNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var project = await EnsureProject(projectRoot, cancellationToken);
        return await ReadId(projectRoot.Config!, project.ProjectId, track, "nextid", cancellationToken);
    }

    public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken) =>
        _worker.Healthy(config, cancellationToken);

    public async Task<int> PeekNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var project = await EnsureProject(projectRoot, cancellationToken);
        return await ReadId(projectRoot.Config!, project.ProjectId, track, "peekid", cancellationToken);
    }

    public async Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var projectId = ReadProjectId(projectRoot);
        return projectId == null
            ? null
            : await ReadId(projectRoot.Config!, projectId, track, "peekid", cancellationToken);
    }

    public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
        CancellationToken cancellationToken = default) => EnsureProject(projectRoot, cancellationToken);

    private async Task<ProjectRegistration> EnsureProject(ProjectRoot projectRoot, CancellationToken cancellationToken)
    {
        var existingProjectId = ReadProjectId(projectRoot);
        if (existingProjectId != null) return new ProjectRegistration(existingProjectId, null);

        var legacyKey = ReadLegacyNextIdKey(projectRoot);
        try
        {
            var identity = _identityService.GetOrCreateIdentity();
            var projectId = RequestSigning.GeneratePublicId("prj");
            var recoveryKey = RequestSigning.GenerateRecoveryKey();
            object payload = legacyKey == null
                ? new CreateProjectRequest(projectId, identity.UserId, identity.DisplayName, identity.PublicKey,
                    RequestSigning.Sha256Hex(recoveryKey))
                : new ClaimLegacyProjectRequest(projectId, legacyKey, identity.UserId, identity.DisplayName,
                    identity.PublicKey, RequestSigning.Sha256Hex(recoveryKey));
            var path = legacyKey == null ? "/projects" : "/legacy-projects/claim";
            var response = await _worker.Send<ProjectResponse>(projectRoot.Config!, HttpMethod.Post, path,
                identity, payload, cancellationToken);
            var registered = Require(response);
            FileSystem.WriteAllText(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile),
                registered.ProjectId + Environment.NewLine);
            return new ProjectRegistration(registered.ProjectId, recoveryKey);
        }
        catch
        {
            if (_options.WriteFailuresToConsole)
                AnsiConsole.MarkupLine("[red]Next ID project could not be registered.[/]");
            throw;
        }
    }

    private async Task<int> ReadId(ProjectConfig config, string projectId, string track, string operation,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetOrCreateIdentity();
        var path = $"/projects/{Uri.EscapeDataString(projectId)}/tracks/{Uri.EscapeDataString(track)}/{operation}";
        var response = await _worker.Send<NextIdResponse>(config, HttpMethod.Get, path, identity,
            cancellationToken: cancellationToken);
        return Require(response).Id;
    }

    private static T Require<T>(WorkerResponse<T> response)
    {
        if (response.Success && response.Payload != null) return response.Payload;
        throw new WorkerClientException(response.ErrorCode ?? "worker_error",
            response.Message ?? "The Worker request failed.", response.StatusCode);
    }

    private static string? ReadProjectId(ProjectRoot projectRoot)
    {
        var path = Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile);
        return File.Exists(path) ? FileSystem.ReadAllText(path).Trim() : null;
    }

    private static string? ReadLegacyNextIdKey(ProjectRoot projectRoot)
    {
        var path = Path.Combine(projectRoot.RootPath, GlobalConfig.LegacyNextIdFile);
        return File.Exists(path) ? FileSystem.ReadAllText(path).Trim() : null;
    }

    private sealed record CreateProjectRequest(string ProjectId, string UserId, string DisplayName,
        string PublicKey, string RecoveryKeyHash);
    private sealed record ClaimLegacyProjectRequest(string ProjectId, string LegacyKey, string UserId,
        string DisplayName, string PublicKey, string RecoveryKeyHash);
    private sealed record ProjectResponse(string ProjectId);
    private sealed record NextIdResponse(int Id);
}
