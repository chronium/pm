using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class MilestoneListCommand(
    ProjectConfigService configService,
    MilestoneActivationResolver activationResolver) : Command<MilestoneListCommand.Settings>
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

        var activation = activationResolver.ResolveCurrentProject();
        if (!activation.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(activation.Message ?? "Milestone list failed.").EscapeMarkup()}[/]");
            return 1;
        }

        var deliveredMilestoneKeys = DeliveredWorkVisibility.ResolveDeliveredMilestoneKeys(activation.Payload!);

        var table = new Table()
            .AddColumn("Key")
            .AddColumn("Title")
            .AddColumn("Priority");

        foreach (var milestone in result.Payload!.Milestones.Where(milestone =>
                     DeliveredWorkVisibility.Includes(
                         milestone.Key,
                         settings.IncludeDelivered,
                         deliveredMilestoneKeys)))
            table.AddRow(
                milestone.Key.EscapeMarkup(),
                milestone.Name.EscapeMarkup(),
                milestone.Priority.EscapeMarkup());

        AnsiConsole.Write(table);
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--include-delivered")]
        [Description("Include delivered milestones")]
        public bool IncludeDelivered { get; init; }
    }
}
