using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using PM.AgentRuns;
using PM.Application;
using PM.Project;

namespace PM.Api;

public sealed record ProjectResponse(string Name, string Revision);

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
        ProjectRoot projectRoot,
        ProjectConfigService configService,
        ProjectValidationService validationService,
        BoardService boardService,
        TaskService taskService,
        WikiService wikiService,
        ResourceRevisionService revisions,
        Action<RouteGroupBuilder>? configure = null,
        IProjectMembershipService? membershipService = null,
        IAgentRunService? agentRunService = null,
        IAgentRunnerClient? agentRunnerClient = null)
    {
        var api = endpoints.MapGroup(Prefix)
            .AddEndpointFilter((context, next) => ReloadProjectConfig(context, next, projectRoot))
            .AddEndpointFilter(WriteRequestFilter);

        api.MapGet("/project", (HttpRequest request) =>
            {
                var result = configService.GetSettings();
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var revisionResult = revisions.GetProjectConfigRevision();
                if (!revisionResult.Success)
                    return ApiResults.Failure(revisionResult.ErrorCode, revisionResult.Message, request.Path);

                var revision = revisionResult.Payload!;
                var notModified = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (notModified != null) return notModified;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(new ProjectResponse(result.Payload!.ProjectName, revision));
            })
            .WithName("GetProject")
            .WithSummary("Get project metadata")
            .Produces<ProjectResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

        api.MapBoardApi(boardService, revisions);
        api.MapTaskApi(boardService, taskService, revisions);
        api.MapWikiApi(wikiService, revisions);
        api.MapSettingsApi(configService, revisions);
        api.MapValidationApi(validationService);
        if (membershipService != null) api.MapProjectMembershipApi(membershipService);
        if (agentRunService != null && agentRunnerClient != null)
            api.MapAgentRunApi(agentRunService, agentRunnerClient);

        configure?.Invoke(api);
        return api;
    }

    private static ValueTask<object?> ReloadProjectConfig(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists)
            return Execute(next, context);
        if (!projectRoot.TryReloadConfig())
            return ValueTask.FromResult<object?>(ApiResults.Problem(
                StatusCodes.Status400BadRequest,
                "invalid_project",
                "The project configuration is invalid.",
                context.HttpContext.Request.Path));

        return Execute(next, context);
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

        var requiresJson = !HttpMethods.IsDelete(request.Method) ||
            request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding");
        if (requiresJson && !request.HasJsonContentType())
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

public static class ApiPreconditions
{
    public static string FormatETag(string revision) => $"\"{revision}\"";

    public static void SetETag(HttpResponse response, string revision) =>
        response.Headers.ETag = FormatETag(revision);

    public static IResult? EvaluateIfNoneMatch(HttpRequest request, string currentRevision)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var values)) return null;

        foreach (var candidate in Parse(values))
        {
            if (candidate.Wildcard || string.Equals(candidate.Tag, currentRevision, StringComparison.Ordinal))
            {
                SetETag(request.HttpContext.Response, currentRevision);
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
        }

        return null;
    }

    public static IResult? RequireIfMatch(HttpRequest request, string currentRevision)
    {
        if (!request.Headers.TryGetValue("If-Match", out var values) ||
            string.IsNullOrWhiteSpace(values.ToString()))
            return ApiResults.Problem(
                StatusCodes.Status428PreconditionRequired,
                "precondition_required",
                "An If-Match header is required.",
                request.Path);

        foreach (var candidate in Parse(values))
        {
            if (candidate.Wildcard ||
                (!candidate.Weak && string.Equals(candidate.Tag, currentRevision, StringComparison.Ordinal)))
                return null;
        }

        return ApiResults.Problem(
            StatusCodes.Status412PreconditionFailed,
            "precondition_failed",
            "The resource has changed. Refetch it and retry the request.",
            request.Path);
    }

    private static IEnumerable<EntityTag> Parse(IEnumerable<string?> headerValues)
    {
        foreach (var headerValue in headerValues)
        {
            if (headerValue == null) continue;
            foreach (var rawPart in headerValue.Split(','))
            {
                var part = rawPart.Trim();
                if (part == "*")
                {
                    yield return new EntityTag(true, false, null);
                    continue;
                }

                var weak = part.StartsWith("W/", StringComparison.OrdinalIgnoreCase);
                var tag = weak ? part[2..].TrimStart() : part;
                if (tag.Length < 2 || tag[0] != '"' || tag[^1] != '"') continue;
                var opaque = tag[1..^1];
                if (opaque.Contains('"') || opaque.Any(character => char.IsControl(character))) continue;
                yield return new EntityTag(false, weak, opaque);
            }
        }
    }

    private sealed record EntityTag(bool Wildcard, bool Weak, string? Tag);
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
        if (errorCode is "next_id_unavailable" or "worker_unavailable" or "runner_unavailable" or
            "runner_clock_skew") return StatusCodes.Status503ServiceUnavailable;
        if (errorCode is "unauthorized" or "runner_unauthorized") return StatusCodes.Status401Unauthorized;
        if (errorCode == "admin_required") return StatusCodes.Status403Forbidden;
        if (errorCode == "rate_limited") return StatusCodes.Status429TooManyRequests;
        if (errorCode is "member_not_found" or "invitation_not_found" or "runner_not_registered" or
            "missing_run") return StatusCodes.Status404NotFound;
        if (errorCode == "precondition_failed") return StatusCodes.Status412PreconditionFailed;
        if (errorCode.StartsWith("missing_", StringComparison.Ordinal)) return StatusCodes.Status404NotFound;
        if (errorCode.StartsWith("invalid_", StringComparison.Ordinal)) return StatusCodes.Status400BadRequest;
        if (errorCode.StartsWith("duplicate_", StringComparison.Ordinal) ||
            errorCode.EndsWith("_in_use", StringComparison.Ordinal) ||
            errorCode is "project_exists" or "last_status" or "last_track" or "stale_wiki_page" or
                "changed_task_id" or "status_directory_not_empty" or "final_admin" or
                "stale_run_preflight" or "run_id_conflict" or "runner_already_registered" or
                "runner_tls_mismatch")
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
