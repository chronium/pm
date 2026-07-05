using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class MilestonePriorityCommand(ProjectConfigService configService)
    : Command<MilestonePriorityCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.SetMilestonePriority(settings.Key, settings.Priority);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Milestone priority update failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Updated milestone [green]{settings.Key.Trim().EscapeMarkup()}[/] priority.");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Milestone key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<priority>")]
        [Description("Milestone priority: none, low, medium, high, urgent")]
        public string Priority { get; init; } = string.Empty;
    }
}
