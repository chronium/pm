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
        LinkedProjectFamilyService familyService,
        LinkedProjectRegistryStore registry)
    {
        api.MapGet("/project/links", async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var result = await familyService.ResolveAsync(cancellationToken);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                return Results.Ok(ToResponse(result.Payload!));
            })
            .WithName("GetLinkedProjects")
            .WithSummary("Get linked-project family health")
            .Produces<LinkedProjectFamilyResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

        api.MapPost("/project/links/{projectId}/write-trust", async (
                HttpRequest request,
                string projectId,
                CancellationToken cancellationToken) =>
            {
                var family = await familyService.ResolveAsync(cancellationToken);
                if (!family.Success)
                    return ApiResults.Failure(family.ErrorCode, family.Message, request.Path);
                var member = family.Payload!.Members.FirstOrDefault(candidate =>
                    string.Equals(candidate.ProjectId, projectId, StringComparison.Ordinal));
                if (member == null || member.Relationship == LinkedProjectRelationship.Current)
                    return ApiResults.Failure(
                        "unknown_linked_project", "Only declared linked projects may be trusted.", request.Path);

                var trusted = registry.GrantWriteTrust(member.ProjectId);
                if (!trusted.Success)
                    return ApiResults.Failure(trusted.ErrorCode, trusted.Message, request.Path);
                var refreshed = await familyService.ResolveAsync(cancellationToken);
                return refreshed.Success
                    ? Results.Ok(ToResponse(refreshed.Payload!))
                    : ApiResults.Failure(refreshed.ErrorCode, refreshed.Message, request.Path);
            })
            .WithName("TrustLinkedProjectWrites")
            .WithSummary("Grant private local write trust to a linked project")
            .WithClientHeaderMetadata()
            .Produces<LinkedProjectFamilyResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        api.MapDelete("/project/links/{projectId}/write-trust", async (
                HttpRequest request,
                string projectId,
                CancellationToken cancellationToken) =>
            {
                var family = await familyService.ResolveAsync(cancellationToken);
                if (!family.Success)
                    return ApiResults.Failure(family.ErrorCode, family.Message, request.Path);
                var member = family.Payload!.Members.FirstOrDefault(candidate =>
                    string.Equals(candidate.ProjectId, projectId, StringComparison.Ordinal));
                if (member == null || member.Relationship == LinkedProjectRelationship.Current)
                    return ApiResults.Failure(
                        "unknown_linked_project", "Only declared linked projects may be untrusted.", request.Path);

                var revoked = registry.RevokeWriteTrust(member.ProjectId);
                if (!revoked.Success)
                    return ApiResults.Failure(revoked.ErrorCode, revoked.Message, request.Path);
                var refreshed = await familyService.ResolveAsync(cancellationToken);
                return refreshed.Success
                    ? Results.Ok(ToResponse(refreshed.Payload!))
                    : ApiResults.Failure(refreshed.ErrorCode, refreshed.Message, request.Path);
            })
            .WithName("UntrustLinkedProjectWrites")
            .WithSummary("Revoke private local write trust from a linked project")
            .WithClientHeaderMetadata()
            .Produces<LinkedProjectFamilyResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");
    }

    private static LinkedProjectFamilyResponse ToResponse(LinkedProjectFamily family) =>
        new(
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
                warning.RepairAction?.DisplayCommand)).ToList());
}
