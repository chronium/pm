using PM.Application;
using Spectre.Console;

namespace PM.Project;

internal static class LifecycleMutationCommandOutput
{
    public static void Write(ReleaseVersionTransition? transition)
    {
        if (transition == null) return;
        AnsiConsole.MarkupLineInterpolated(
            $"Release: [blue]{transition.FromVersion} -> {transition.ToVersion}[/] ({transition.Kind.EscapeMarkup()}).");
    }

    public static void Write(AutomaticActivationImpact impact)
    {
        foreach (var trigger in impact.ActivatedTriggers)
            AnsiConsole.MarkupLineInterpolated(
                $"Automatically activated trigger [green]{trigger.Key.EscapeMarkup()}[/].");

        foreach (var change in impact.MilestoneChanges)
            AnsiConsole.MarkupLineInterpolated(
                $"Milestone [green]{change.MilestoneKey.EscapeMarkup()}[/]: [blue]{change.Before.ToString().EscapeMarkup()} -> {change.After.ToString().EscapeMarkup()}[/].");
    }
}
