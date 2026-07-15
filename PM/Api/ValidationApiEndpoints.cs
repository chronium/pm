using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;

namespace PM.Api;

public sealed record ValidationIssueResponse(
    string Severity,
    string Code,
    string Message,
    string? Path,
    string? TaskId,
    string? WikiPath,
    string? State);
public sealed record ValidationResponse(bool Valid, IReadOnlyList<ValidationIssueResponse> Issues);

public static class ValidationApiEndpoints
{
    public static void MapValidationApi(this RouteGroupBuilder api, ProjectValidationService validationService)
    {
        api.MapGet("/validation", (HttpRequest request) =>
            {
                var result = validationService.ValidateProject();
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var validation = result.Payload!;
                return Results.Ok(new ValidationResponse(validation.Valid, validation.Issues.Select(issue =>
                    new ValidationIssueResponse(issue.Severity, issue.Code, issue.Message, issue.Path,
                        issue.TaskId, issue.WikiPath, issue.State)).ToList()));
            })
            .WithName("GetValidation")
            .WithSummary("Validate the project")
            .Produces<ValidationResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");
    }
}
