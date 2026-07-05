using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class MilestoneListCommand(ProjectConfigService configService) : Command<MilestoneListCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.GetSettings();
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Milestone list failed.").EscapeMarkup()}[/]");
            return 1;
        }

        var table = new Table()
            .AddColumn("Key")
            .AddColumn("Title")
            .AddColumn("Priority");

        foreach (var milestone in result.Payload!.Milestones)
            table.AddRow(
                milestone.Key.EscapeMarkup(),
                milestone.Name.EscapeMarkup(),
                milestone.Priority.EscapeMarkup());

        AnsiConsole.Write(table);
        return 0;
    }

    public class Settings : CommandSettings
    {
    }
}
