using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;

namespace PM.Api;

public sealed record LinkedProjectMemberResponse(
    string ProjectId,
    string Name,
    string? Alias,
    string Relationship,
    string Status,
    string Source,
    bool Readable,
    bool WriteTrusted);

public sealed record LinkedProjectWarningResponse(
    string Code,
    string Message,
    string DeclaringProjectId,
    string TargetProjectId,
    string? Alias,
    string Status,
    string? RepairCommand);

public sealed record LinkedProjectFamilyResponse(
    string ActiveProjectId,
    IReadOnlyList<LinkedProjectMemberResponse> Members,
    IReadOnlyList<LinkedProjectWarningResponse> Warnings);

public static class LinkedProjectApiEndpoints
{
    public static void MapLinkedProjectApi(
        this RouteGroupBuilder api,
        LinkedProjectFamilyService familyService)
    {
        api.MapGet("/project/links", async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var result = await familyService.ResolveAsync(cancellationToken);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var family = result.Payload!;
                return Results.Ok(new LinkedProjectFamilyResponse(
                    family.ActiveProjectId,
                    family.Members.Select(member => new LinkedProjectMemberResponse(
                        member.ProjectId,
                        member.Name,
                        member.Alias,
                        LinkedProjectFamilyService.Format(member.Relationship),
                        LinkedProjectFamilyService.Format(member.Status),
                        LinkedProjectFamilyService.Format(member.Source),
                        member.Readable,
                        member.WriteTrusted)).ToList(),
                    family.Warnings.Select(warning => new LinkedProjectWarningResponse(
                        warning.Code,
                        warning.Message,
                        warning.DeclaringProjectId,
                        warning.TargetProjectId,
                        warning.Alias,
                        LinkedProjectFamilyService.Format(warning.Status),
                        warning.RepairAction?.DisplayCommand)).ToList()));
            })
            .WithName("GetLinkedProjects")
            .WithSummary("Get linked-project family health")
            .Produces<LinkedProjectFamilyResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");
    }
}
