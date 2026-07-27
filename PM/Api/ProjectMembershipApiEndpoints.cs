using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;

namespace PM.Api;

public sealed record ProjectRoleRequest(string Role);
public sealed record AcceptProjectInvitationRequest(string Token);
public sealed record ProjectMemberResponse(
    string UserId, string DisplayName, string PublicKey, string Fingerprint, string Role, bool IsLocal);
public sealed record ProjectMembersResponse(
    string ProjectId, string CurrentUserId, string CurrentRole, bool Authenticated,
    IReadOnlyList<ProjectMemberResponse> Members);
public sealed record ProjectInvitationResponse(
    string InvitationId, string Role, string CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record ProjectInvitationsResponse(IReadOnlyList<ProjectInvitationResponse> Invitations);
public sealed record CreatedProjectInvitationResponse(ProjectInvitationResponse Invitation, string Token);
public sealed record LocalIdentityResponse(string UserId, string DisplayName, string PublicKey, string Fingerprint);

public static class ProjectMembershipApiEndpoints
{
    public static RouteGroupBuilder MapProjectMembershipApi(
        this RouteGroupBuilder api,
        IProjectMembershipService membership)
    {
        api.MapGet("/project/identity", () =>
            {
                var result = membership.GetLocalIdentity();
                return result.Success
                    ? Results.Ok(new LocalIdentityResponse(result.Payload!.UserId, result.Payload.DisplayName,
                        result.Payload.PublicKey, result.Payload.Fingerprint))
                    : ApiResults.Failure(result.ErrorCode, result.Message);
            })
            .WithName("GetLocalIdentity")
            .WithSummary("Get the shareable local PM identity")
            .Produces<LocalIdentityResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

        api.MapGet("/project/members", async (HttpRequest request, CancellationToken cancellationToken) =>
            Result(await membership.ListMembers(cancellationToken), request.Path))
            .WithName("ListProjectMembers")
            .WithSummary("List remote project members")
            .Produces<ProjectMembersResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        api.MapGet("/project/invitations", async (HttpRequest request, CancellationToken cancellationToken) =>
            Result(await membership.ListInvitations(cancellationToken), request.Path))
            .WithName("ListProjectInvitations")
            .WithSummary("List active pending project invitations")
            .Produces<ProjectInvitationsResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");

        api.MapPost("/project/invitations", async (ProjectRoleRequest body, HttpRequest request,
                CancellationToken cancellationToken) =>
            Result(await membership.CreateInvitation(body.Role, cancellationToken), request.Path))
            .WithName("CreateProjectInvitation")
            .WithSummary("Create a 24-hour single-use project invitation")
            .Produces<CreatedProjectInvitationResponse>(StatusCodes.Status200OK)
            .Produces<ApiProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");

        api.MapPost("/project/invitations/accept", async (AcceptProjectInvitationRequest body,
                HttpRequest request, CancellationToken cancellationToken) =>
            Result(await membership.AcceptInvitation(body.Token, cancellationToken), request.Path))
            .WithName("AcceptProjectInvitation")
            .WithSummary("Accept a project invitation using the local identity")
            .Produces<ProjectMemberResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json");

        api.MapDelete("/project/invitations/{invitationId}", async (string invitationId,
                HttpRequest request, CancellationToken cancellationToken) =>
            Result(await membership.RevokeInvitation(invitationId, cancellationToken), request.Path))
            .WithName("RevokeProjectInvitation")
            .WithSummary("Revoke an active project invitation")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPatch("/project/members/{userId}", async (string userId, ProjectRoleRequest body,
                HttpRequest request, CancellationToken cancellationToken) =>
            Result(await membership.UpdateMemberRole(userId, body.Role, cancellationToken), request.Path))
            .WithName("UpdateProjectMemberRole")
            .WithSummary("Update a project member role")
            .Produces<ProjectMemberResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        api.MapDelete("/project/members/{userId}", async (string userId, HttpRequest request,
                CancellationToken cancellationToken) =>
            Result(await membership.RemoveMember(userId, cancellationToken), request.Path))
            .WithName("RemoveProjectMember")
            .WithSummary("Remove a project member")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        return api;
    }

    private static IResult Result(AppResult<ProjectMembers> result, PathString path) => result.Success
        ? Results.Ok(new ProjectMembersResponse(result.Payload!.ProjectId, result.Payload.CurrentUserId,
            result.Payload.CurrentRole, result.Payload.Authenticated, result.Payload.Members.Select(Member).ToList()))
        : ApiResults.Failure(result.ErrorCode, result.Message, path);

    private static IResult Result(AppResult<ProjectInvitations> result, PathString path) => result.Success
        ? Results.Ok(new ProjectInvitationsResponse(result.Payload!.Invitations.Select(Invitation).ToList()))
        : ApiResults.Failure(result.ErrorCode, result.Message, path);

    private static IResult Result(AppResult<CreatedProjectInvitation> result, PathString path) => result.Success
        ? Results.Ok(new CreatedProjectInvitationResponse(Invitation(result.Payload!.Invitation), result.Payload.Token))
        : ApiResults.Failure(result.ErrorCode, result.Message, path);

    private static IResult Result(AppResult<ProjectMember> result, PathString path) => result.Success
        ? Results.Ok(Member(result.Payload!))
        : ApiResults.Failure(result.ErrorCode, result.Message, path);

    private static IResult Result(AppResult result, PathString path) => result.Success
        ? Results.NoContent()
        : ApiResults.Failure(result.ErrorCode, result.Message, path);

    private static ProjectMemberResponse Member(ProjectMember value) =>
        new(value.UserId, value.DisplayName, value.PublicKey, value.Fingerprint, value.Role, value.IsLocal);

    private static ProjectInvitationResponse Invitation(ProjectInvitation value) =>
        new(value.InvitationId, value.Role, value.CreatedByUserId, value.CreatedAt, value.ExpiresAt);
}
