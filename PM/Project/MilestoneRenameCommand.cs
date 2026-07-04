using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class MilestoneRenameCommand(ProjectConfigService configService) : Command<MilestoneRenameCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.RenameMilestone(settings.Key, settings.Title);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Milestone rename failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Renamed milestone [green]{settings.Key.Trim().EscapeMarkup()}[/].");
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
