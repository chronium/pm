using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class MilestoneAddCommand(ProjectRoot projectRoot) : Command<MilestoneAddCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        var key = settings.Key.Trim();
        var title = settings.Title.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title))
        {
            AnsiConsole.MarkupLine("[red]Milestone key and title are required.[/]");
            return 1;
        }

        var config = projectRoot.Config!;
        if (config.Milestones.ContainsKey(key))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Milestone {key.EscapeMarkup()} already exists.[/]");
            return 1;
        }

        config.Milestones[key] = title;
        config.WriteConfig(projectRoot);
        AnsiConsole.MarkupLineInterpolated($"Added milestone [green]{key.EscapeMarkup()}[/].");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Milestone key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<title>")]
        [Description("Milestone title")]
        public string Title { get; init; } = string.Empty;
    }
}
