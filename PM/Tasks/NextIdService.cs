using System.Text.Json;
using System.Text.Json.Serialization;
using PM.Files;
using PM.Project;
using Spectre.Console;

namespace PM.Tasks;

public interface INextIdService
{
    Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default);
    Task<int> PeekNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default);
    Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default);

    Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default);
}

public class NextIdService(HttpClient httpClient) : INextIdService
{
    public async Task<int> GetNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var key = await GetNextIdKey(projectRoot, cancellationToken);

        return await GetNextId(projectRoot.Config!, key, track, cancellationToken);
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
        var key = await GetNextIdKey(projectRoot, cancellationToken);
        return await PeekNextId(projectRoot.Config!, key, track, cancellationToken);
    }

    public async Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
        CancellationToken cancellationToken = default)
    {
        var key = ReadNextIdKey(projectRoot);
        return key == null ? null : await PeekNextId(projectRoot.Config!, key, track, cancellationToken);
    }

    private async Task<string> GetNextIdKey(ProjectRoot projectRoot, CancellationToken cancellationToken)
    {
        var existingKey = ReadNextIdKey(projectRoot);
        if (existingKey == null)
            try
            {
                var newKey = await CreateProjectKey(projectRoot.Config!, cancellationToken);
                var nextIdPath = Path.Combine(projectRoot.RootPath, GlobalConfig.NextIdFile);
                FileSystem.WriteAllText(nextIdPath, newKey);
                return newKey;
            }
            catch
            {
                AnsiConsole.WriteLine("[red]Next ID project key could not be created.[/]");
                throw;
            }

        return existingKey;
    }

    private static string? ReadNextIdKey(ProjectRoot projectRoot)
    {
        var nextIdPath = Path.Combine(projectRoot.RootPath, GlobalConfig.NextIdFile);
        if (!File.Exists(nextIdPath)) return null;

        var key = FileSystem.ReadAllText(nextIdPath);
        return key;
    }

    private async Task<string> CreateProjectKey(ProjectConfig config, CancellationToken cancellationToken)
    {
        var projectKeyResponse = await httpClient.PostAsync(BuildUri(config, "/projects"), new StringContent(""),
            cancellationToken);
        projectKeyResponse.EnsureSuccessStatusCode();

        var json = await projectKeyResponse.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<CreateProjectKeyResponse>(json)!.Key;
    }

    private async Task<int> GetNextId(ProjectConfig config, string key, string track, CancellationToken cancellationToken)
    {
        var nextIdResponse = await httpClient.GetAsync(
            BuildUri(config,
                $"/projects/{Uri.EscapeDataString(key)}/tracks/{Uri.EscapeDataString(track)}/nextid"),
            cancellationToken);
        nextIdResponse.EnsureSuccessStatusCode();

        var json = await nextIdResponse.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<NextIdResponse>(json)!.Id;
    }

    private async Task<int> PeekNextId(ProjectConfig config, string key, string track, CancellationToken cancellationToken)
    {
        var peekIdResponse = await httpClient.GetAsync(
            BuildUri(config,
                $"/projects/{Uri.EscapeDataString(key)}/tracks/{Uri.EscapeDataString(track)}/peekid"),
            cancellationToken);
        peekIdResponse.EnsureSuccessStatusCode();

        var json = await peekIdResponse.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<NextIdResponse>(json)!.Id;
    }

    private static Uri BuildUri(ProjectConfig config, string path)
    {
        var baseUri = config.NextIdServiceUrl.EndsWith('/')
            ? new Uri(config.NextIdServiceUrl)
            : new Uri($"{config.NextIdServiceUrl}/");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private class CreateProjectKeyResponse
    {
        [JsonPropertyName("key")] public required string Key { get; init; }
    }

    private class NextIdResponse
    {
        [JsonPropertyName("id")] public required int Id { get; init; }
    }
}
