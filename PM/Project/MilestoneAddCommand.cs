using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class MilestoneAddCommand(ProjectConfigService configService) : Command<MilestoneAddCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.AddMilestone(settings.Key, settings.Title, settings.Priority);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Milestone add failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Added milestone [green]{settings.Key.Trim().EscapeMarkup()}[/].");
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

        [CommandOption("--priority <PRIORITY>")]
        [Description("Milestone priority: none, low, medium, high, urgent")]
        public string Priority { get; init; } = PriorityLevel.None;
    }
}
