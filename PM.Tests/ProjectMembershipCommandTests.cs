using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class ProjectMembershipCommandTests
{
    [Fact]
    public async Task JoinReadsInvitationOnlyFromSecurePromptAbstraction()
    {
        var membership = new CommandMembershipService();
        var prompts = new CommandPrompts { Token = "pmi_from_stdin_or_secret_prompt" };
        var command = new ProjectJoinCommand(membership, prompts);

        var exitCode = await command.ExecuteAsync(null!, new CommonSettings(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(prompts.Token, membership.AcceptedToken);
        Assert.Empty(typeof(ProjectJoinCommand).GetNestedTypes());
    }

    [Fact]
    public async Task AdminInvitationsRequireExplicitConfirmation()
    {
        var membership = new CommandMembershipService();
        var prompts = new CommandPrompts { Confirmation = false };
        var command = new ProjectInviteCommand(membership, prompts);

        var exitCode = await command.ExecuteAsync(null!,
            new ProjectInviteCommand.Settings { Role = "admin" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Null(membership.CreatedRole);
        Assert.Contains("grants project administration", prompts.LastConfirmation);
    }

    [Fact]
    public async Task DemotionAndRemovalRequireConfirmation()
    {
        var membership = new CommandMembershipService();
        var prompts = new CommandPrompts { Confirmation = false };

        var demotion = await new ProjectSetRoleCommand(membership, prompts).ExecuteAsync(null!,
            new ProjectSetRoleCommand.Settings { UserId = "usr_admin", Role = "user" }, CancellationToken.None);
        var removal = await new ProjectRemoveMemberCommand(membership, prompts).ExecuteAsync(null!,
            new ProjectRemoveMemberCommand.Settings { UserId = "usr_admin" }, CancellationToken.None);

        Assert.Equal(1, demotion);
        Assert.Equal(1, removal);
        Assert.Equal(0, membership.RoleUpdates);
        Assert.Equal(0, membership.Removals);
    }

    private sealed class CommandPrompts : IProjectCommandPrompts
    {
        public string Token { get; init; } = "pmi_token";
        public bool Confirmation { get; init; }
        public string? LastConfirmation { get; private set; }
        public string ReadInvitationToken() => Token;
        public bool Confirm(string prompt)
        {
            LastConfirmation = prompt;
            return Confirmation;
        }
    }

    private sealed class CommandMembershipService : IProjectMembershipService
    {
        private static readonly ProjectMember Member = new(
            "usr_local", "Local", "public-key", new string('a', 64), "user", true);
        private static readonly ProjectInvitation Invitation = new(
            "pminv_1", "user", "usr_admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24));

        public string? AcceptedToken { get; private set; }
        public string? CreatedRole { get; private set; }
        public int RoleUpdates { get; private set; }
        public int Removals { get; private set; }

        public AppResult<LocalIdentity> GetLocalIdentity() => AppResult<LocalIdentity>.Ok(
            new LocalIdentity(Member.UserId, Member.DisplayName, Member.PublicKey, Member.Fingerprint));
        public Task<AppResult<ProjectMembers>> ListMembers(CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<ProjectMembers>.Ok(
                new ProjectMembers("project-1", Member.UserId, Member.Role, true, [Member])));
        public Task<AppResult<ProjectInvitations>> ListInvitations(CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<ProjectInvitations>.Ok(new ProjectInvitations([Invitation])));
        public Task<AppResult<CreatedProjectInvitation>> CreateInvitation(string role,
            CancellationToken cancellationToken = default)
        {
            CreatedRole = role;
            return Task.FromResult(AppResult<CreatedProjectInvitation>.Ok(
                new CreatedProjectInvitation(Invitation with { Role = role }, "pmi_secret")));
        }
        public Task<AppResult<ProjectMember>> AcceptInvitation(string token,
            CancellationToken cancellationToken = default)
        {
            AcceptedToken = token;
            return Task.FromResult(AppResult<ProjectMember>.Ok(Member));
        }
        public Task<AppResult> RevokeInvitation(string invitationId,
            CancellationToken cancellationToken = default) => Task.FromResult(AppResult.Ok());
        public Task<AppResult<ProjectMember>> UpdateMemberRole(string userId, string role,
            CancellationToken cancellationToken = default)
        {
            RoleUpdates++;
            return Task.FromResult(AppResult<ProjectMember>.Ok(Member with { Role = role }));
        }
        public Task<AppResult> RemoveMember(string userId, CancellationToken cancellationToken = default)
        {
            Removals++;
            return Task.FromResult(AppResult.Ok());
        }
    }
}
