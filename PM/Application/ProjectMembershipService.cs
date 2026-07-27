using System.Security.Cryptography;
using PM.Auth;
using PM.Files;
using PM.Project;
using PM.Worker;

namespace PM.Application;

public sealed record LocalIdentity(
    string UserId,
    string DisplayName,
    string PublicKey,
    string Fingerprint);

public sealed record ProjectMember(
    string UserId,
    string DisplayName,
    string PublicKey,
    string Fingerprint,
    string Role,
    bool IsLocal);

public sealed record ProjectMembers(
    string ProjectId,
    string CurrentUserId,
    string CurrentRole,
    bool Authenticated,
    IReadOnlyList<ProjectMember> Members);

public sealed record ProjectInvitation(
    string InvitationId,
    string Role,
    string CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ProjectInvitations(IReadOnlyList<ProjectInvitation> Invitations);

public sealed record CreatedProjectInvitation(ProjectInvitation Invitation, string Token);

public interface IProjectMembershipService
{
    AppResult<LocalIdentity> GetLocalIdentity();
    Task<AppResult<ProjectMembers>> ListMembers(CancellationToken cancellationToken = default);
    Task<AppResult<ProjectInvitations>> ListInvitations(CancellationToken cancellationToken = default);
    Task<AppResult<CreatedProjectInvitation>> CreateInvitation(string role,
        CancellationToken cancellationToken = default);
    Task<AppResult<ProjectMember>> AcceptInvitation(string token,
        CancellationToken cancellationToken = default);
    Task<AppResult> RevokeInvitation(string invitationId, CancellationToken cancellationToken = default);
    Task<AppResult<ProjectMember>> UpdateMemberRole(string userId, string role,
        CancellationToken cancellationToken = default);
    Task<AppResult> RemoveMember(string userId, CancellationToken cancellationToken = default);
}

public sealed class ProjectMembershipService(
    ProjectRoot projectRoot,
    IIdentityService identityService,
    IPmWorkerClient worker) : IProjectMembershipService
{
    public AppResult<LocalIdentity> GetLocalIdentity()
    {
        try
        {
            var identity = identityService.GetOrCreateIdentity();
            return AppResult<LocalIdentity>.Ok(ToLocalIdentity(identity));
        }
        catch (Exception exception)
        {
            return AppResult<LocalIdentity>.Fail("invalid_identity", exception.Message);
        }
    }

    public async Task<AppResult<ProjectMembers>> ListMembers(CancellationToken cancellationToken = default)
    {
        var context = Context();
        if (!context.Success) return AppResult<ProjectMembers>.Fail(context.ErrorCode!, context.Message!);
        var (projectId, identity) = context.Payload!;
        var response = await worker.Send<MembersResponse>(projectRoot.Config!, HttpMethod.Get,
            $"/projects/{Uri.EscapeDataString(projectId)}/members", identity,
            cancellationToken: cancellationToken);
        if (!response.Success) return Failure<ProjectMembers>(response.ErrorCode, response.Message);

        var members = response.Payload!.Members.Select(member => new ProjectMember(
            member.UserId, member.DisplayName, member.PublicKey, Fingerprint(member.PublicKey), member.Role,
            member.UserId == identity.UserId)).ToList();
        return AppResult<ProjectMembers>.Ok(new ProjectMembers(projectId, response.Payload.CurrentUserId,
            response.Payload.CurrentRole, true, members));
    }

    public async Task<AppResult<ProjectInvitations>> ListInvitations(CancellationToken cancellationToken = default)
    {
        var context = Context();
        if (!context.Success) return AppResult<ProjectInvitations>.Fail(context.ErrorCode!, context.Message!);
        var response = await worker.Send<InvitationsResponse>(projectRoot.Config!, HttpMethod.Get,
            $"/projects/{Uri.EscapeDataString(context.Payload!.ProjectId)}/invitations", context.Payload.Identity,
            cancellationToken: cancellationToken);
        return response.Success
            ? AppResult<ProjectInvitations>.Ok(new ProjectInvitations(response.Payload!.Invitations))
            : Failure<ProjectInvitations>(response.ErrorCode, response.Message);
    }

    public async Task<AppResult<CreatedProjectInvitation>> CreateInvitation(string role,
        CancellationToken cancellationToken = default)
    {
        role = role.Trim().ToLowerInvariant();
        if (role is not ("admin" or "user"))
            return AppResult<CreatedProjectInvitation>.Fail("invalid_role", "Role must be admin or user.");
        var context = Context();
        if (!context.Success) return AppResult<CreatedProjectInvitation>.Fail(context.ErrorCode!, context.Message!);
        var response = await worker.Send<CreateInvitationResponse>(projectRoot.Config!, HttpMethod.Post,
            $"/projects/{Uri.EscapeDataString(context.Payload!.ProjectId)}/invitations", context.Payload.Identity,
            new { role }, cancellationToken);
        return response.Success
            ? AppResult<CreatedProjectInvitation>.Ok(new CreatedProjectInvitation(
                response.Payload!.Invitation, response.Payload.Token))
            : Failure<CreatedProjectInvitation>(response.ErrorCode, response.Message);
    }

    public async Task<AppResult<ProjectMember>> AcceptInvitation(string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return AppResult<ProjectMember>.Fail("invalid_invitation", "An invitation token is required.");
        var context = Context();
        if (!context.Success) return AppResult<ProjectMember>.Fail(context.ErrorCode!, context.Message!);
        var identity = context.Payload!.Identity;
        var response = await worker.Send<MemberResponse>(projectRoot.Config!, HttpMethod.Post,
            $"/projects/{Uri.EscapeDataString(context.Payload.ProjectId)}/invitations/accept", identity,
            new { token = token.Trim(), identity.UserId, identity.DisplayName, identity.PublicKey }, cancellationToken);
        return response.Success
            ? AppResult<ProjectMember>.Ok(ToProjectMember(response.Payload!.Member, identity.UserId))
            : Failure<ProjectMember>(response.ErrorCode, response.Message);
    }

    public async Task<AppResult> RevokeInvitation(string invitationId,
        CancellationToken cancellationToken = default)
    {
        var context = Context();
        if (!context.Success) return AppResult.Fail(context.ErrorCode!, context.Message!);
        var response = await worker.Send<object>(projectRoot.Config!, HttpMethod.Delete,
            $"/projects/{Uri.EscapeDataString(context.Payload!.ProjectId)}/invitations/{Uri.EscapeDataString(invitationId)}",
            context.Payload.Identity, cancellationToken: cancellationToken);
        return response.Success ? AppResult.Ok() : FailureResult(response);
    }

    public async Task<AppResult<ProjectMember>> UpdateMemberRole(string userId, string role,
        CancellationToken cancellationToken = default)
    {
        role = role.Trim().ToLowerInvariant();
        if (role is not ("admin" or "user"))
            return AppResult<ProjectMember>.Fail("invalid_role", "Role must be admin or user.");
        var context = Context();
        if (!context.Success) return AppResult<ProjectMember>.Fail(context.ErrorCode!, context.Message!);
        var response = await worker.Send<MemberResponse>(projectRoot.Config!, HttpMethod.Patch,
            $"/projects/{Uri.EscapeDataString(context.Payload!.ProjectId)}/members/{Uri.EscapeDataString(userId)}",
            context.Payload.Identity, new { role }, cancellationToken);
        return response.Success
            ? AppResult<ProjectMember>.Ok(ToProjectMember(response.Payload!.Member, context.Payload.Identity.UserId))
            : Failure<ProjectMember>(response.ErrorCode, response.Message);
    }

    public async Task<AppResult> RemoveMember(string userId, CancellationToken cancellationToken = default)
    {
        var context = Context();
        if (!context.Success) return AppResult.Fail(context.ErrorCode!, context.Message!);
        var response = await worker.Send<object>(projectRoot.Config!, HttpMethod.Delete,
            $"/projects/{Uri.EscapeDataString(context.Payload!.ProjectId)}/members/{Uri.EscapeDataString(userId)}",
            context.Payload.Identity, cancellationToken: cancellationToken);
        return response.Success ? AppResult.Ok() : FailureResult(response);
    }

    public static string Fingerprint(string publicKey)
    {
        try
        {
            var normalized = publicKey.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '='));
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (FormatException)
        {
            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(publicKey))).ToLowerInvariant();
        }
    }

    private AppResult<ProjectContext> Context()
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<ProjectContext>.Fail("missing_project", "Project not found. Run pm init first.");
        var path = Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile);
        if (!FileSystem.FileExists(path) || string.IsNullOrWhiteSpace(FileSystem.ReadAllText(path)))
            return AppResult<ProjectContext>.Fail("missing_project_id", "This project has no public Worker project ID.");
        try
        {
            return AppResult<ProjectContext>.Ok(new ProjectContext(FileSystem.ReadAllText(path).Trim(),
                identityService.GetOrCreateIdentity()));
        }
        catch (Exception exception)
        {
            return AppResult<ProjectContext>.Fail("invalid_identity", exception.Message);
        }
    }

    private static LocalIdentity ToLocalIdentity(PmIdentity value) =>
        new(value.UserId, value.DisplayName, value.PublicKey, Fingerprint(value.PublicKey));

    private static ProjectMember ToProjectMember(MemberDto value, string localUserId) =>
        new(value.UserId, value.DisplayName, value.PublicKey, Fingerprint(value.PublicKey), value.Role,
            value.UserId == localUserId);

    private static AppResult<T> Failure<T>(string? errorCode, string? message) =>
        AppResult<T>.Fail(errorCode ?? "worker_error", message ?? "The Worker request failed.");

    private static AppResult FailureResult<T>(WorkerResponse<T> response) =>
        AppResult.Fail(response.ErrorCode ?? "worker_error", response.Message ?? "The Worker request failed.");

    private sealed record ProjectContext(string ProjectId, PmIdentity Identity);
    private sealed record MemberDto(string UserId, string DisplayName, string PublicKey, string Role);
    private sealed record MembersResponse(string CurrentUserId, string CurrentRole, List<MemberDto> Members);
    private sealed record InvitationsResponse(List<ProjectInvitation> Invitations);
    private sealed record CreateInvitationResponse(ProjectInvitation Invitation, string Token);
    private sealed record MemberResponse(MemberDto Member);
}
