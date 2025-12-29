using System.Text.Json;
using System.Text.Json.Serialization;
using PM.Files;
using PM.Project;
using Spectre.Console;

namespace PM.Tasks;

public interface INextIdService
{
    Task<int> GetNextId(ProjectRoot projectRoot, CancellationToken cancellationToken = default);

    Task<bool> Healthy(CancellationToken cancellationToken = default);
}

public class NextIdService(HttpClient httpClient) : INextIdService
{
    public async Task<int> GetNextId(ProjectRoot projectRoot, CancellationToken cancellationToken)
    {
        var nextIdPath = Path.Combine(projectRoot.RootPath, GlobalConfig.NextIdFile);
        if (!File.Exists(nextIdPath))
        {
            try
            {
                var newKey = await CreateProjectKey(cancellationToken);
                FileSystem.WriteFileWithText(nextIdPath, newKey);
            }
            catch
            {
                AnsiConsole.WriteLine("[red]Next ID project key could not be created.[/]");
                throw;
            }

            return 0;
        }

        var key = FileSystem.ReadAllText(nextIdPath);

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

    private async Task<string> CreateProjectKey(CancellationToken cancellationToken)
    {
        var projectKeyResponse = await httpClient.PostAsync("/projects", new StringContent(""), cancellationToken);
        projectKeyResponse.EnsureSuccessStatusCode();

        var json = await projectKeyResponse.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<CreateProjectKeyResponse>(json)!.Key;
    }

    private async Task<int> GetNextId(string key, CancellationToken cancellationToken)
    {
        var nextIdResponse = await httpClient.GetAsync($"/projects/{key}/next_id", cancellationToken);
        nextIdResponse.EnsureSuccessStatusCode();

        var json = await nextIdResponse.Content.ReadAsStringAsync(cancellationToken);
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