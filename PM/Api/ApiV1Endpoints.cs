using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using PM.Application;

namespace PM.Api;

public sealed record ProjectResponse(string Name);

public sealed class ApiProblemDetails : ProblemDetails
{
    public required string ErrorCode { get; init; }
}

public static class ApiV1Endpoints
{
    public const string Prefix = "/api/v1";
    public const string ClientHeader = "X-PM-Client";

    public static RouteGroupBuilder MapApiV1(
        this IEndpointRouteBuilder endpoints,
        ProjectConfigService configService,
        Action<RouteGroupBuilder>? configure = null)
    {
        var api = endpoints.MapGroup(Prefix)
            .AddEndpointFilter(WriteRequestFilter);

        api.MapGet("/project", (HttpRequest request) =>
            {
                var result = configService.GetSettings();
                return result.Success
                    ? Results.Ok(new ProjectResponse(result.Payload!.ProjectName))
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("GetProject")
            .WithSummary("Get project metadata")
            .Produces<ProjectResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

        configure?.Invoke(api);
        return api;
    }

    private static async ValueTask<object?> WriteRequestFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method) &&
            !HttpMethods.IsPut(request.Method) &&
            !HttpMethods.IsPatch(request.Method) &&
            !HttpMethods.IsDelete(request.Method))
            return await Execute(next, context);

        if (!request.Headers.TryGetValue(ClientHeader, out var client) ||
            string.IsNullOrWhiteSpace(client.ToString()))
            return ApiResults.Problem(
                StatusCodes.Status400BadRequest,
                "missing_client_header",
                $"A nonempty {ClientHeader} header is required.",
                request.Path);

        if (!request.HasJsonContentType())
            return ApiResults.Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "unsupported_media_type",
                "Mutation requests must use application/json.",
                request.Path);

        return await Execute(next, context);
    }

    private static async ValueTask<object?> Execute(
        EndpointFilterDelegate next,
        EndpointFilterInvocationContext context)
    {
        try
        {
            return await next(context);
        }
        catch
        {
            return ApiResults.Problem(
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "An unexpected error occurred.",
                context.HttpContext.Request.Path);
        }
    }
}

public static class ApiResults
{
    public static IResult Failure(string? errorCode, string? detail, PathString instance = default)
    {
        var code = string.IsNullOrWhiteSpace(errorCode) ? "internal_error" : errorCode;
        return Problem(StatusFor(code), code,
            StatusFor(code) == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : detail ?? "The request could not be completed.",
            instance);
    }

    public static int StatusFor(string errorCode)
    {
        if (errorCode == "next_id_unavailable") return StatusCodes.Status503ServiceUnavailable;
        if (errorCode.StartsWith("missing_", StringComparison.Ordinal)) return StatusCodes.Status404NotFound;
        if (errorCode.StartsWith("invalid_", StringComparison.Ordinal)) return StatusCodes.Status400BadRequest;
        if (errorCode.StartsWith("duplicate_", StringComparison.Ordinal) ||
            errorCode.EndsWith("_in_use", StringComparison.Ordinal) ||
            errorCode is "project_exists" or "last_status" or "last_track" or "stale_wiki_page" or
                "changed_task_id" or "status_directory_not_empty")
            return StatusCodes.Status409Conflict;

        return StatusCodes.Status500InternalServerError;
    }

    public static IResult Problem(int status, string errorCode, string detail, PathString instance = default)
    {
        var problem = new ApiProblemDetails
        {
            Type = $"https://pm.dev/problems/{errorCode}",
            Title = ReasonPhrases.GetReasonPhrase(status),
            Status = status,
            Detail = detail,
            Instance = instance.HasValue ? instance.Value : null,
            ErrorCode = errorCode,
        };

        return Results.Json(problem, statusCode: status, contentType: "application/problem+json");
    }
}
