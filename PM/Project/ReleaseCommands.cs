using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public interface IReleaseCommandPrompts
{
    bool Confirm(string prompt);
}

public sealed class ReleaseCommandPrompts : IReleaseCommandPrompts
{
    public bool Confirm(string prompt) => AnsiConsole.Confirm(prompt, false);
}

public sealed class ReleaseStatusCommand(ReleaseVersionService releases) : Command
{
    public override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        var result = releases.ReadStatus();
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{result.Message!.EscapeMarkup()}[/]");
            return 1;
        }
        var status = result.Payload!;
        if (!status.Enabled)
        {
            AnsiConsole.MarkupLine("Release versioning is [yellow]disabled[/] for this project.");
            return 0;
        }
        AnsiConsole.MarkupLineInterpolated($"Current release: [green]{status.Version}[/]");
        AnsiConsole.MarkupLine(status.PendingTransition == null
            ? "Pending transition: none"
            : $"Pending transition: {Format(status.PendingTransition)}");
        AnsiConsole.MarkupLine(status.LatestTransition == null
            ? "Latest evidence: none"
            : $"Latest evidence: {Format(status.LatestTransition)}");
        return 0;
    }

    private static string Format(ReleaseVersionTransition transition) =>
        $"{transition.FromVersion} -> {transition.ToVersion} ({transition.Kind})".EscapeMarkup();
}

public sealed class ReleaseReconcileCommand(ReleaseVersionService releases)
    : Command<ReleaseReconcileCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = releases.Reconcile(settings.DryRun);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{result.Message!.EscapeMarkup()}[/]");
            return 1;
        }
        AnsiConsole.MarkupLine(result.Payload!.Changed
            ? $"Release reconciliation: [green]{result.Payload.Action.EscapeMarkup()}[/]."
            : "Release reconciliation: [grey]nothing pending[/].");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--dry-run")]
        [Description("Preview reconciliation without changing files")]
        public bool DryRun { get; init; }
    }
}

public sealed class ReleaseMajorCommand(ReleaseVersionService releases, IReleaseCommandPrompts prompts)
    : Command<ReleaseMajorCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var preview = releases.PreviewMajor(settings.Reason);
        if (!preview.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{preview.Message!.EscapeMarkup()}[/]");
            return 1;
        }
        var transition = preview.Payload!.Transition;
        AnsiConsole.MarkupLineInterpolated(
            $"Major release preview: [yellow]{transition.FromVersion}[/] -> [green]{transition.ToVersion}[/]");
        AnsiConsole.MarkupLineInterpolated($"Reason: {transition.Reason!.EscapeMarkup()}");
        if (!settings.Yes && !prompts.Confirm("Advance to the next major release?"))
        {
            AnsiConsole.MarkupLine("[yellow]Major release cancelled.[/]");
            return 1;
        }
        var begin = releases.Begin(preview.Payload);
        if (!begin.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{begin.Message!.EscapeMarkup()}[/]");
            return 1;
        }
        var result = releases.Complete(preview.Payload);
        if (!result.Success)
        {
            _ = releases.Rollback(preview.Payload);
            AnsiConsole.MarkupLineInterpolated($"[red]{result.Message!.EscapeMarkup()}[/]");
            return 1;
        }
        AnsiConsole.MarkupLineInterpolated($"Advanced release to [green]{transition.ToVersion}[/].");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--reason <REASON>")]
        [Description("Reason for the major release boundary")]
        public string Reason { get; init; } = string.Empty;

        [CommandOption("--yes")]
        [Description("Advance without prompting")]
        public bool Yes { get; init; }
    }
}
