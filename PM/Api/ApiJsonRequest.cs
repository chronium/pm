using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace PM.Api;

public static class ApiJsonRequest
{
    public static async Task<(T? Value, IResult? Error)> Read<T>(HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await request.ReadFromJsonAsync<T>(cancellationToken);
            return value == null
                ? (default, Invalid(request))
                : (value, null);
        }
        catch (JsonException)
        {
            return (default, Invalid(request));
        }
        catch (BadHttpRequestException)
        {
            return (default, Invalid(request));
        }
    }

    private static IResult Invalid(HttpRequest request) => ApiResults.Problem(
        StatusCodes.Status400BadRequest,
        "invalid_json",
        "The request body must contain valid JSON.",
        request.Path);
}
