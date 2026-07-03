using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class MilestoneRemoveCommand(ProjectConfigService configService) : Command<MilestoneRemoveCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.RemoveMilestone(settings.Key);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Milestone remove failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Removed milestone [green]{settings.Key.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Milestone key")]
        public string Key { get; init; } = string.Empty;
    }
}
