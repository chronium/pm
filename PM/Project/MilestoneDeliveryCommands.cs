using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public interface IMilestoneDeliveryCommandPrompts
{
    bool Confirm(string prompt);
}

public sealed class MilestoneDeliveryCommandPrompts : IMilestoneDeliveryCommandPrompts
{
    public bool Confirm(string prompt) => AnsiConsole.Confirm(prompt, false);
}

public sealed class MilestoneDeliverCommand(
    MilestoneDeliveryService milestones,
    IMilestoneDeliveryCommandPrompts prompts)
    : Command<MilestoneDeliverCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var preview = milestones.PreviewDelivery(settings.Key, settings.Reason);
        if (!preview.Success) return MilestoneDeliveryCommandOutput.Fail(preview.Message);

        var approved = settings.Yes;
        if (preview.Payload!.RequiresConfirmation)
        {
            MilestoneDeliveryCommandOutput.WriteExceptionalPreview(preview.Payload);
            if (!approved)
                approved = prompts.Confirm(
                    $"Accept {preview.Payload.UnfinishedTaskIds.Count} unfinished task(s) and deliver the milestone?");
            if (!approved)
            {
                AnsiConsole.MarkupLine("[yellow]Milestone delivery cancelled.[/]");
                return 1;
            }
        }

        var result = milestones.DeliverMilestone(
            settings.Key,
            settings.Reason,
            preview.Payload.Revision,
            approved);
        return result.Success
            ? MilestoneDeliveryCommandOutput.Delivered(result.Payload!)
            : MilestoneDeliveryCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Milestone key")]
        public string Key { get; init; } = string.Empty;

        [CommandOption("--reason <REASON>")]
        [Description("Reason for exceptionally accepting unfinished tasks")]
        public string? Reason { get; init; }

        [CommandOption("--yes")]
        [Description("Confirm exceptional delivery without prompting")]
        public bool Yes { get; init; }
    }
}

public sealed class MilestoneReopenCommand(MilestoneDeliveryService milestones)
    : Command<MilestoneReopenCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = milestones.ReopenMilestone(settings.Key);
        return result.Success
            ? MilestoneDeliveryCommandOutput.Reopened(result.Payload!)
            : MilestoneDeliveryCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Delivered milestone key")]
        public string Key { get; init; } = string.Empty;
    }
}

internal static class MilestoneDeliveryCommandOutput
{
    public static void WriteExceptionalPreview(MilestoneDeliveryPreview preview)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Exceptional delivery preview for [green]{preview.MilestoneKey.EscapeMarkup()}[/]:");
        AnsiConsole.MarkupLineInterpolated(
            $"Completed tasks: [blue]{preview.DoneTaskCount} / {preview.AssignedTaskCount}[/].");
        AnsiConsole.MarkupLineInterpolated(
            $"Accepted unfinished tasks: [blue]{FormatTaskIds(preview.UnfinishedTaskIds)}[/].");
    }

    public static int Delivered(ResolvedMilestone milestone)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Delivered milestone [green]{milestone.Key.EscapeMarkup()}[/].");
        var delivery = milestone.Delivery!;
        AnsiConsole.MarkupLineInterpolated(
            $"Delivery: [blue]{delivery.Mode.ToString().ToLowerInvariant()} at {delivery.At:u}[/].");
        AnsiConsole.MarkupLineInterpolated(
            $"Tasks: [blue]{milestone.DoneTaskCount} / {milestone.AssignedTaskCount} done[/].");
        if (delivery.Mode == MilestoneDeliveryMode.Exceptional)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"Reason: [blue]{delivery.Reason!.EscapeMarkup()}[/].");
            AnsiConsole.MarkupLineInterpolated(
                $"Accepted unfinished tasks: [blue]{FormatTaskIds(delivery.AcceptedTaskIds)}[/].");
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Lifecycle: [blue]{milestone.Lifecycle.ToString().EscapeMarkup()}[/].");
        return 0;
    }

    public static int Reopened(ResolvedMilestone milestone)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Reopened milestone [green]{milestone.Key.EscapeMarkup()}[/].");
        AnsiConsole.MarkupLineInterpolated(
            $"Lifecycle: [blue]{milestone.Lifecycle.ToString().EscapeMarkup()}[/].");
        var unmet = milestone.UnmetActivationTriggers.Count == 0
            ? "none"
            : string.Join(", ", milestone.UnmetActivationTriggers);
        AnsiConsole.MarkupLineInterpolated(
            $"Unmet activation triggers: [blue]{unmet.EscapeMarkup()}[/].");
        return 0;
    }

    public static int Fail(string? message)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"[red]{(message ?? "Milestone delivery operation failed.").EscapeMarkup()}[/]");
        return 1;
    }

    private static string FormatTaskIds(IReadOnlyList<string> taskIds) =>
        (taskIds.Count == 0 ? "none" : string.Join(", ", taskIds)).EscapeMarkup();
}
