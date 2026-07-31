using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public sealed class ProjectLinksCommand(
    LinkedProjectFamilyService familyService) : AsyncCommand<ProjectLinksCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var result = await familyService.ResolveAsync(cancellationToken);
        if (!result.Success)
            return WriteError(result.Message ?? "Linked projects could not be resolved.");
        var family = result.Payload!;

        var table = new Table()
            .AddColumn("Relationship")
            .AddColumn("Alias")
            .AddColumn("Project ID")
            .AddColumn("Status")
            .AddColumn("Write")
            .AddColumn("Source")
            .AddColumn("Repository");
        foreach (var member in family.Members)
        {
            table.AddRow(
                LinkedProjectFamilyService.Format(member.Relationship).EscapeMarkup(),
                (member.Alias ?? "-").EscapeMarkup(),
                member.ProjectId.EscapeMarkup(),
                LinkedProjectFamilyService.Format(member.Status).EscapeMarkup(),
                (member.Relationship == LinkedProjectRelationship.Current || member.WriteTrusted
                    ? "trusted"
                    : "read-only").EscapeMarkup(),
                LinkedProjectFamilyService.Format(member.Source).EscapeMarkup(),
                (member.RepositoryPath ?? "-").EscapeMarkup());
        }

        AnsiConsole.Write(table);
        foreach (var warning in family.Warnings)
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]{(warning.Alias ?? warning.TargetProjectId).EscapeMarkup()} ({warning.Code.EscapeMarkup()}):[/] {warning.Message.EscapeMarkup()}");
        foreach (var warning in family.Warnings.Where(warning => warning.RepairAction != null))
            AnsiConsole.MarkupLineInterpolated($"[yellow]Repair:[/] {warning.RepairAction!.DisplayCommand.EscapeMarkup()}");
        return 0;
    }

    private static int WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{message.EscapeMarkup()}[/]");
        return 1;
    }

    public sealed class Settings : CommandSettings
    {
    }
}

public sealed class ProjectTrustCommand(
    LinkedProjectFamilyService familyService,
    LinkedProjectRegistryStore registry) : AsyncCommand<ProjectTrustCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var family = await familyService.ResolveAsync(cancellationToken);
        if (!family.Success)
            return WriteError(family.Message ?? "Linked projects could not be resolved.");
        var selected = LinkedProjectFamilyService.SelectMember(family.Payload!, settings.Selector);
        if (!selected.Success)
            return WriteError(selected.Message ?? "Linked-project selector is invalid.");
        if (selected.Payload!.Relationship == LinkedProjectRelationship.Current)
            return WriteError("The active project is already writable and does not require local trust.");

        var result = registry.GrantWriteTrust(selected.Payload.ProjectId);
        if (!result.Success)
            return WriteError(result.Message ?? "Write trust could not be granted.");

        AnsiConsole.MarkupLineInterpolated(
            $"Trusted [green]{result.Payload!.ProjectId.EscapeMarkup()}[/] for local writes at {result.Payload.RepositoryPath.EscapeMarkup()}.");
        return 0;
    }

    private static int WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{message.EscapeMarkup()}[/]");
        return 1;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<selector-or-id>")]
        [Description("Declared parent, child, sibling, alias, or stable project ID")]
        public string Selector { get; init; } = string.Empty;
    }
}

public sealed class ProjectUntrustCommand(
    LinkedProjectFamilyService familyService,
    LinkedProjectRegistryStore registry) : AsyncCommand<ProjectUntrustCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var family = await familyService.ResolveAsync(cancellationToken);
        if (!family.Success)
            return WriteError(family.Message ?? "Linked projects could not be resolved.");
        var selected = LinkedProjectFamilyService.SelectMember(family.Payload!, settings.Selector);
        if (!selected.Success)
            return WriteError(selected.Message ?? "Linked-project selector is invalid.");
        if (selected.Payload!.Relationship == LinkedProjectRelationship.Current)
            return WriteError("Write access to the active project cannot be revoked.");

        var result = registry.RevokeWriteTrust(selected.Payload.ProjectId);
        if (!result.Success)
            return WriteError(result.Message ?? "Write trust could not be revoked.");

        AnsiConsole.MarkupLineInterpolated(
            $"Revoked local write trust for [green]{result.Payload!.ProjectId.EscapeMarkup()}[/].");
        return 0;
    }

    private static int WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{message.EscapeMarkup()}[/]");
        return 1;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<selector-or-id>")]
        [Description("Declared parent, child, sibling, alias, or stable project ID")]
        public string Selector { get; init; } = string.Empty;
    }
}

public sealed class ProjectBindCommand(
    ProjectRoot projectRoot,
    LinkedProjectService linkedProjects,
    LinkedProjectRegistryStore registry) : Command<ProjectBindCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var manifest = linkedProjects.GetManifest();
        if (!manifest.Success)
            return WriteError(manifest.Message ?? "Linked projects could not be read.");

        var activeProjectId = projectRoot.TryReadProjectId(out var currentProjectId) ? currentProjectId : null;
        var projectId = LinkedProjectSelector.ResolveProjectId(
            activeProjectId, manifest.Payload!.Manifest, settings.Selector);
        if (!projectId.Success)
            return WriteError(projectId.Message ?? "Linked-project selector is invalid.");

        var result = registry.Bind(projectId.Payload!, settings.RepositoryPath, settings.Replace);
        if (!result.Success)
            return WriteError(result.Message ?? "Project binding failed.");

        AnsiConsole.MarkupLineInterpolated(
            $"Bound [green]{result.Payload!.ProjectId.EscapeMarkup()}[/] to {result.Payload.RepositoryPath.EscapeMarkup()}.");
        return 0;
    }

    private static int WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{message.EscapeMarkup()}[/]");
        return 1;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<selector-or-id>")]
        [Description("Current project, declared parent, alias, or stable project ID")]
        public string Selector { get; init; } = string.Empty;

        [CommandArgument(1, "<repository-root>")]
        [Description("Exact repository root to bind")]
        public string RepositoryPath { get; init; } = string.Empty;

        [CommandOption("--replace")]
        [Description("Replace an existing binding to another repository")]
        public bool Replace { get; init; }
    }
}

public sealed class ProjectUnbindCommand(
    ProjectRoot projectRoot,
    LinkedProjectService linkedProjects,
    LinkedProjectRegistryStore registry) : Command<ProjectUnbindCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var manifest = linkedProjects.GetManifest();
        if (!manifest.Success)
            return WriteError(manifest.Message ?? "Linked projects could not be read.");

        var activeProjectId = projectRoot.TryReadProjectId(out var currentProjectId) ? currentProjectId : null;
        var projectId = LinkedProjectSelector.ResolveProjectId(
            activeProjectId, manifest.Payload!.Manifest, settings.Selector);
        if (!projectId.Success)
            return WriteError(projectId.Message ?? "Linked-project selector is invalid.");

        var result = registry.Remove(projectId.Payload!);
        if (!result.Success)
            return WriteError(result.Message ?? "Project unbind failed.");

        AnsiConsole.MarkupLineInterpolated($"Unbound [green]{projectId.Payload.EscapeMarkup()}[/].");
        return 0;
    }

    private static int WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{message.EscapeMarkup()}[/]");
        return 1;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<selector-or-id>")]
        [Description("Current project, declared parent, alias, or stable project ID")]
        public string Selector { get; init; } = string.Empty;
    }
}
