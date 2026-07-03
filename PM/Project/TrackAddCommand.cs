using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class TrackAddCommand(ProjectRoot projectRoot) : Command<TrackAddCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        var key = settings.Key.Trim();
        var name = settings.Name.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
        {
            AnsiConsole.MarkupLine("[red]Track key and name are required.[/]");
            return 1;
        }

        var config = projectRoot.Config!;
        if (config.Tracks.ContainsKey(key))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Track {key.EscapeMarkup()} already exists.[/]");
            return 1;
        }

        config.Tracks[key] = name;
        config.WriteConfig(projectRoot);
        AnsiConsole.MarkupLineInterpolated($"Added track [green]{key.EscapeMarkup()}[/].");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Track key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<name>")]
        [Description("Track display name")]
        public string Name { get; init; } = string.Empty;
    }
}
