using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class TrackRenameCommand(ProjectConfigService configService) : Command<TrackRenameCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.RenameTrack(settings.Key, settings.Name);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Track rename failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Renamed track [green]{settings.Key.Trim().EscapeMarkup()}[/].");
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
