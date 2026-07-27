using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PM.Auth;
using PM.Project;

namespace PM.Worker;

public sealed record WorkerResponse<T>(
    bool Success,
    HttpStatusCode StatusCode,
    T? Payload = default,
    string? ErrorCode = null,
    string? Message = null);

public sealed class WorkerClientException(string errorCode, string message, HttpStatusCode statusCode)
    : HttpRequestException(message, null, statusCode)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IPmWorkerClient
{
    Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default);
    Task<WorkerResponse<T>> Send<T>(ProjectConfig config, HttpMethod method, string path,
        PmIdentity identity, object? payload = null, CancellationToken cancellationToken = default);
}

public sealed class PmWorkerClient(HttpClient httpClient) : IPmWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            return (await httpClient.GetAsync(BuildUri(config, "/health"), cancellationToken)).IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<WorkerResponse<T>> Send<T>(ProjectConfig config, HttpMethod method, string path,
        PmIdentity identity, object? payload = null, CancellationToken cancellationToken = default)
    {
        var body = payload == null ? string.Empty : JsonSerializer.Serialize(payload, JsonOptions);
        var uri = BuildUri(config, path);
        using var request = new HttpRequestMessage(method, uri);
        var headers = RequestSigning.Sign(identity, method, uri, body);
        request.Headers.Add("PM-User-Id", headers.UserId);
        request.Headers.Add("PM-Timestamp", headers.Timestamp);
        request.Headers.Add("PM-Nonce", headers.Nonce);
        request.Headers.Add("PM-Signature", headers.Signature);
        request.Headers.Add("PM-Public-Key", headers.PublicKey);
        if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new WorkerResponse<T>(false, HttpStatusCode.ServiceUnavailable,
                ErrorCode: "worker_unavailable", Message: "The project membership service could not be reached.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return new WorkerResponse<T>(true, response.StatusCode);
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return new WorkerResponse<T>(true, response.StatusCode, value);
            }

            WorkerError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<WorkerError>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
            }

            return new WorkerResponse<T>(false, response.StatusCode,
                ErrorCode: error?.ErrorCode ?? DefaultError(response.StatusCode),
                Message: error?.Message ?? DefaultMessage(response.StatusCode));
        }
    }

    private static Uri BuildUri(ProjectConfig config, string path)
    {
        var baseUri = config.NextIdServiceUrl.EndsWith('/')
            ? new Uri(config.NextIdServiceUrl)
            : new Uri($"{config.NextIdServiceUrl}/");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private static string DefaultError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "unauthorized",
        HttpStatusCode.Forbidden => "admin_required",
        HttpStatusCode.NotFound => "not_found",
        HttpStatusCode.Conflict => "conflict",
        HttpStatusCode.TooManyRequests => "rate_limited",
        _ => "worker_error",
    };

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Authentication failed.",
        HttpStatusCode.Forbidden => "Project admin access is required.",
        HttpStatusCode.TooManyRequests => "Too many requests. Try again later.",
        _ => "The project membership request failed.",
    };

    private sealed record WorkerError(
        [property: JsonPropertyName("errorCode")] string ErrorCode,
        [property: JsonPropertyName("message")] string Message);
}
