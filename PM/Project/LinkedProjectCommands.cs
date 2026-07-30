using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public sealed class ProjectLinksCommand(
    ProjectRoot projectRoot,
    LinkedProjectService linkedProjects,
    LinkedProjectResolver resolver) : AsyncCommand<ProjectLinksCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var manifest = linkedProjects.GetManifest();
        if (!manifest.Success)
            return WriteError(manifest.Message ?? "Linked projects could not be read.");

        var declarations = LinkedProjectSelector.Enumerate(manifest.Payload!.Manifest).ToList();
        if (declarations.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No linked projects are declared.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumn("Relationship")
            .AddColumn("Alias")
            .AddColumn("Project ID")
            .AddColumn("Status")
            .AddColumn("Source")
            .AddColumn("Repository");
        var repairs = new List<(string Alias, LinkedProjectRepairAction Repair)>();
        var diagnostics = new List<(string Alias, LinkedProjectResolutionDiagnostic Diagnostic)>();

        foreach (var (relationship, declaration) in declarations)
        {
            var resolution = await resolver.ResolveAsync(projectRoot, declaration, cancellationToken);
            table.AddRow(
                relationship.EscapeMarkup(),
                declaration.Alias.EscapeMarkup(),
                declaration.ProjectId.EscapeMarkup(),
                Format(resolution.Status).EscapeMarkup(),
                Format(resolution.Source).EscapeMarkup(),
                (resolution.RepositoryPath ?? "-").EscapeMarkup());
            if (resolution.RepairAction != null)
                repairs.Add((declaration.Alias, resolution.RepairAction));
            diagnostics.AddRange(resolution.Diagnostics.Select(diagnostic => (declaration.Alias, diagnostic)));
        }

        AnsiConsole.Write(table);
        foreach (var (alias, diagnostic) in diagnostics)
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]{alias.EscapeMarkup()} ({diagnostic.Code.EscapeMarkup()}):[/] {diagnostic.Message.EscapeMarkup()}");
        foreach (var (alias, repair) in repairs)
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]{alias.EscapeMarkup()} can be initialized with:[/] {repair.DisplayCommand.EscapeMarkup()}");
        return 0;
    }

    private static string Format<T>(T value) where T : Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"-{char.ToLowerInvariant(character)}" :
            char.ToLowerInvariant(character).ToString()));

    private static int WriteError(string message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{message.EscapeMarkup()}[/]");
        return 1;
    }

    public sealed class Settings : CommandSettings
    {
    }
}

public sealed class ProjectBindCommand(
    LinkedProjectService linkedProjects,
    LinkedProjectRegistryStore registry) : Command<ProjectBindCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var manifest = linkedProjects.GetManifest();
        if (!manifest.Success)
            return WriteError(manifest.Message ?? "Linked projects could not be read.");

        var projectId = LinkedProjectSelector.ResolveProjectId(manifest.Payload!.Manifest, settings.Selector);
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
        [Description("Declared parent, alias, or stable project ID")]
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
    LinkedProjectService linkedProjects,
    LinkedProjectRegistryStore registry) : Command<ProjectUnbindCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var manifest = linkedProjects.GetManifest();
        if (!manifest.Success)
            return WriteError(manifest.Message ?? "Linked projects could not be read.");

        var projectId = LinkedProjectSelector.ResolveProjectId(manifest.Payload!.Manifest, settings.Selector);
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
        [Description("Declared parent, alias, or stable project ID")]
        public string Selector { get; init; } = string.Empty;
    }
}
