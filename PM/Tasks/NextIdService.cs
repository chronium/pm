using System.Text.Json;
using System.Text.Json.Serialization;
using PM.Files;
using PM.Project;
using Spectre.Console;

namespace PM.Tasks;

public interface INextIdService
{
    Task<int> GetNextId(ProjectRoot projectRoot, CancellationToken cancellationToken = default);
    Task<int> PeekNextId(ProjectRoot projectRoot, CancellationToken cancellationToken = default);

    Task<bool> Healthy(CancellationToken cancellationToken = default);
}

public class NextIdService(HttpClient httpClient) : INextIdService
{
    public async Task<int> GetNextId(ProjectRoot projectRoot, CancellationToken cancellationToken)
    {
        var key = await GetNextIdKey(projectRoot, cancellationToken);

        return await GetNextId(key, cancellationToken);
    }

    public async Task<bool> Healthy(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> PeekNextId(ProjectRoot projectRoot, CancellationToken cancellationToken)
    {
        var key = await GetNextIdKey(projectRoot, cancellationToken);
        return await PeekNextId(key, cancellationToken);
    }

    private async Task<string> GetNextIdKey(ProjectRoot projectRoot, CancellationToken cancellationToken)
    {
        var nextIdPath = Path.Combine(projectRoot.RootPath, GlobalConfig.NextIdFile);
        if (!File.Exists(nextIdPath))
            try
            {
                var newKey = await CreateProjectKey(cancellationToken);
                FileSystem.WriteAllText(nextIdPath, newKey);
                return newKey;
            }
            catch
            {
                AnsiConsole.WriteLine("[red]Next ID project key could not be created.[/]");
                throw;
            }

        var key = FileSystem.ReadAllText(nextIdPath);
        return key;
    }

    private async Task<string> CreateProjectKey(CancellationToken cancellationToken)
    {
        var projectKeyResponse = await httpClient.PostAsync("/projects", new StringContent(""), cancellationToken);
        projectKeyResponse.EnsureSuccessStatusCode();

        var json = await projectKeyResponse.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<CreateProjectKeyResponse>(json)!.Key;
    }

    private async Task<int> GetNextId(string key, CancellationToken cancellationToken)
    {
        var nextIdResponse = await httpClient.GetAsync($"/projects/{key}/nextid", cancellationToken);
        nextIdResponse.EnsureSuccessStatusCode();

        var json = await nextIdResponse.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<NextIdResponse>(json)!.Id;
    }

    private async Task<int> PeekNextId(string key, CancellationToken cancellationToken)
    {
        var peekIdResponse = await httpClient.GetAsync($"/projects/{key}/peekid", cancellationToken);
        peekIdResponse.EnsureSuccessStatusCode();

        var json = await peekIdResponse.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<NextIdResponse>(json)!.Id;
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