using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PM.Auth;
using PM.Files;
using PM.Project;
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

public class NextIdService(
    HttpClient httpClient,
    IIdentityService identityService,
    IOptions<NextIdServiceOptions>? options = null) : INextIdService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly NextIdServiceOptions _options = options?.Value ?? new NextIdServiceOptions();

    public async Task<int> GetNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var project = await EnsureProject(projectRoot, cancellationToken);
        return await GetNextId(projectRoot.Config!, project.ProjectId, track, cancellationToken);
    }

    public async Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync(BuildUri(config, "/health"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> PeekNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var project = await EnsureProject(projectRoot, cancellationToken);
        return await PeekNextId(projectRoot.Config!, project.ProjectId, track, cancellationToken);
    }

    public async Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var projectId = ReadProjectId(projectRoot);
        return projectId == null ? null : await PeekNextId(projectRoot.Config!, projectId, track, cancellationToken);
    }

    public async Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot, CancellationToken cancellationToken = default)
    {
        return await EnsureProject(projectRoot, cancellationToken);
    }

    private async Task<ProjectRegistration> EnsureProject(ProjectRoot projectRoot, CancellationToken cancellationToken)
    {
        var existingProjectId = ReadProjectId(projectRoot);
        if (existingProjectId != null) return new ProjectRegistration(existingProjectId, null);

        var legacyKey = ReadLegacyNextIdKey(projectRoot);
        try
        {
            var registration = legacyKey == null
                ? await CreateProject(projectRoot.Config!, cancellationToken)
                : await ClaimLegacyProject(projectRoot.Config!, legacyKey, cancellationToken);

            FileSystem.WriteAllText(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile),
                registration.ProjectId + Environment.NewLine);
            return registration;
        }
        catch
        {
            if (_options.WriteFailuresToConsole)
                AnsiConsole.WriteLine("[red]Next ID project could not be registered.[/]");
            throw;
        }
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

    private async Task<ProjectRegistration> CreateProject(ProjectConfig config, CancellationToken cancellationToken)
    {
        var identity = identityService.GetOrCreateIdentity();
        var projectId = RequestSigning.GeneratePublicId("prj");
        var recoveryKey = RequestSigning.GenerateRecoveryKey();
        var payload = new CreateProjectRequest(
            projectId,
            identity.UserId,
            identity.DisplayName,
            identity.PublicKey,
            RequestSigning.Sha256Hex(recoveryKey));

        var response = await SendSignedJson(config, HttpMethod.Post, "/projects", identity, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ProjectResponse>(JsonOptions, cancellationToken);
        return new ProjectRegistration(body!.ProjectId, recoveryKey);
    }

    private async Task<ProjectRegistration> ClaimLegacyProject(ProjectConfig config, string legacyKey, CancellationToken cancellationToken)
    {
        var identity = identityService.GetOrCreateIdentity();
        var projectId = RequestSigning.GeneratePublicId("prj");
        var recoveryKey = RequestSigning.GenerateRecoveryKey();
        var payload = new ClaimLegacyProjectRequest(
            projectId,
            legacyKey,
            identity.UserId,
            identity.DisplayName,
            identity.PublicKey,
            RequestSigning.Sha256Hex(recoveryKey));

        var response = await SendSignedJson(config, HttpMethod.Post, "/legacy-projects/claim", identity, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ProjectResponse>(JsonOptions, cancellationToken);
        return new ProjectRegistration(body!.ProjectId, recoveryKey);
    }

    private async Task<int> GetNextId(ProjectConfig config, string projectId, string track, CancellationToken cancellationToken)
    {
        var response = await SendSigned(config, HttpMethod.Get,
            $"/projects/{Uri.EscapeDataString(projectId)}/tracks/{Uri.EscapeDataString(track)}/nextid",
            identityService.GetOrCreateIdentity(), string.Empty, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<NextIdResponse>(JsonOptions, cancellationToken);
        return body!.Id;
    }

    private async Task<int> PeekNextId(ProjectConfig config, string projectId, string track, CancellationToken cancellationToken)
    {
        var response = await SendSigned(config, HttpMethod.Get,
            $"/projects/{Uri.EscapeDataString(projectId)}/tracks/{Uri.EscapeDataString(track)}/peekid",
            identityService.GetOrCreateIdentity(), string.Empty, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<NextIdResponse>(JsonOptions, cancellationToken);
        return body!.Id;
    }

    private async Task<HttpResponseMessage> SendSignedJson(
        ProjectConfig config,
        HttpMethod method,
        string path,
        PmIdentity identity,
        object payload,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        return await SendSigned(config, method, path, identity, body, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendSigned(
        ProjectConfig config,
        HttpMethod method,
        string path,
        PmIdentity identity,
        string body,
        CancellationToken cancellationToken)
    {
        var uri = BuildUri(config, path);
        using var request = new HttpRequestMessage(method, uri);
        var headers = RequestSigning.Sign(identity, method, uri, body);
        request.Headers.Add("PM-User-Id", headers.UserId);
        request.Headers.Add("PM-Timestamp", headers.Timestamp);
        request.Headers.Add("PM-Nonce", headers.Nonce);
        request.Headers.Add("PM-Signature", headers.Signature);
        request.Headers.Add("PM-Public-Key", headers.PublicKey);

        if (!string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static Uri BuildUri(ProjectConfig config, string path)
    {
        var baseUri = config.NextIdServiceUrl.EndsWith('/')
            ? new Uri(config.NextIdServiceUrl)
            : new Uri($"{config.NextIdServiceUrl}/");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private sealed record CreateProjectRequest(
        string ProjectId,
        string UserId,
        string DisplayName,
        string PublicKey,
        string RecoveryKeyHash);

    private sealed record ClaimLegacyProjectRequest(
        string ProjectId,
        string LegacyKey,
        string UserId,
        string DisplayName,
        string PublicKey,
        string RecoveryKeyHash);

    private sealed class ProjectResponse
    {
        [JsonPropertyName("projectId")] public required string ProjectId { get; init; }
    }

    private sealed class NextIdResponse
    {
        [JsonPropertyName("id")] public required int Id { get; init; }
    }
}
