using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class TrackRemoveCommand(ProjectConfigService configService) : Command<TrackRemoveCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.RemoveTrack(settings.Key);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(result.Message ?? "Track remove failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Removed track [green]{settings.Key.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Track key")]
        public string Key { get; init; } = string.Empty;
    }
}
