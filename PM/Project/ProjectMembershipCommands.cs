using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public interface IProjectCommandPrompts
{
    string ReadInvitationToken();
    bool Confirm(string prompt);
}

public sealed class ProjectCommandPrompts : IProjectCommandPrompts
{
    public string ReadInvitationToken()
    {
        if (Console.IsInputRedirected) return Console.In.ReadLine()?.Trim() ?? string.Empty;
        return AnsiConsole.Prompt(new TextPrompt<string>("Invitation token:").Secret());
    }

    public bool Confirm(string prompt) => AnsiConsole.Confirm(prompt, false);
}

public sealed class ProjectIdentityCommand(IProjectMembershipService membership) : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        var result = membership.GetLocalIdentity();
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        var identity = result.Payload!;
        AnsiConsole.MarkupLineInterpolated($"User ID: [green]{identity.UserId.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLineInterpolated($"Display name: {identity.DisplayName.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"Public key: {identity.PublicKey.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"SHA-256 fingerprint: {identity.Fingerprint.EscapeMarkup()}");
        return 0;
    }
}

public sealed class ProjectMembersCommand(IProjectMembershipService membership) : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await membership.ListMembers(cancellationToken);
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        var table = new Table().AddColumn("Name").AddColumn("User ID").AddColumn("Role").AddColumn("Fingerprint");
        foreach (var member in result.Payload!.Members)
            table.AddRow(member.IsLocal ? $"{member.DisplayName.EscapeMarkup()} [grey](local)[/]" : member.DisplayName.EscapeMarkup(),
                member.UserId.EscapeMarkup(), member.Role.EscapeMarkup(), member.Fingerprint.EscapeMarkup());
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class ProjectInvitationsCommand(IProjectMembershipService membership) : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await membership.ListInvitations(cancellationToken);
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        if (result.Payload!.Invitations.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No active project invitations.[/]");
            return 0;
        }
        var table = new Table().AddColumn("Invitation ID").AddColumn("Role").AddColumn("Expires");
        foreach (var invitation in result.Payload.Invitations)
            table.AddRow(invitation.InvitationId.EscapeMarkup(), invitation.Role.EscapeMarkup(),
                invitation.ExpiresAt.ToString("u").EscapeMarkup());
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class ProjectInviteCommand(IProjectMembershipService membership, IProjectCommandPrompts prompts)
    : AsyncCommand<ProjectInviteCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var role = settings.Role.Trim().ToLowerInvariant();
        if (role == "admin" && !settings.Yes &&
            !prompts.Confirm("This invitation grants project administration. Continue?")) return 1;
        var result = await membership.CreateInvitation(role, cancellationToken);
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        AnsiConsole.MarkupLineInterpolated($"Invitation ID: {result.Payload!.Invitation.InvitationId.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"Role: {result.Payload.Invitation.Role.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"Expires: {result.Payload.Invitation.ExpiresAt:u}");
        AnsiConsole.MarkupLine("[yellow]This secret is shown once. Share it through a secure channel.[/]");
        AnsiConsole.WriteLine(result.Payload.Token);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--role <ROLE>")]
        [Description("Invitation role: user or admin")]
        public string Role { get; init; } = "user";

        [CommandOption("--yes")]
        [Description("Confirm granting project administration")]
        public bool Yes { get; init; }
    }
}

public sealed class ProjectJoinCommand(IProjectMembershipService membership, IProjectCommandPrompts prompts)
    : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings,
        CancellationToken cancellationToken)
    {
        var token = prompts.ReadInvitationToken();
        var result = await membership.AcceptInvitation(token, cancellationToken);
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        AnsiConsole.MarkupLineInterpolated($"Joined project as [green]{result.Payload!.Role.EscapeMarkup()}[/].");
        return 0;
    }
}

public sealed class ProjectRevokeInvitationCommand(IProjectMembershipService membership)
    : AsyncCommand<ProjectRevokeInvitationCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var result = await membership.RevokeInvitation(settings.InvitationId, cancellationToken);
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        AnsiConsole.MarkupLineInterpolated($"Revoked invitation [green]{settings.InvitationId.EscapeMarkup()}[/].");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<invitation-id>")]
        public string InvitationId { get; init; } = string.Empty;
    }
}

public sealed class ProjectSetRoleCommand(IProjectMembershipService membership, IProjectCommandPrompts prompts)
    : AsyncCommand<ProjectSetRoleCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var role = settings.Role.Trim().ToLowerInvariant();
        if (role == "user" && !settings.Yes &&
            !prompts.Confirm($"Demote {settings.UserId} to user?")) return 1;
        var result = await membership.UpdateMemberRole(settings.UserId, role, cancellationToken);
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        AnsiConsole.MarkupLineInterpolated($"Updated [green]{settings.UserId.EscapeMarkup()}[/] to {role.EscapeMarkup()}.");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<user-id>")] public string UserId { get; init; } = string.Empty;
        [CommandArgument(1, "<user|admin>")] public string Role { get; init; } = string.Empty;
        [CommandOption("--yes")] public bool Yes { get; init; }
    }
}

public sealed class ProjectRemoveMemberCommand(IProjectMembershipService membership, IProjectCommandPrompts prompts)
    : AsyncCommand<ProjectRemoveMemberCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Yes && !prompts.Confirm($"Remove {settings.UserId} from this project?")) return 1;
        var result = await membership.RemoveMember(settings.UserId, cancellationToken);
        if (!result.Success) return MembershipCommandOutput.Fail(result.Message);
        AnsiConsole.MarkupLineInterpolated($"Removed [green]{settings.UserId.EscapeMarkup()}[/].");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<user-id>")] public string UserId { get; init; } = string.Empty;
        [CommandOption("--yes")] public bool Yes { get; init; }
    }
}

internal static class MembershipCommandOutput
{
    public static int Fail(string? message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? "Project membership request failed.").EscapeMarkup()}[/]");
        return 1;
    }
}
