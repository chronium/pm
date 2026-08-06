using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public interface IActivationTriggerCommandPrompts
{
    bool Confirm(string prompt);
}

public sealed class ActivationTriggerCommandPrompts : IActivationTriggerCommandPrompts
{
    public bool Confirm(string prompt) => AnsiConsole.Confirm(prompt, false);
}

public sealed class ActivationTriggerAddCommand(ActivationTriggerService triggers)
    : Command<ActivationTriggerAddCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var requirements = ActivationRequirementInput.ParseOptional(settings.Requirements);
        if (!requirements.Success) return ActivationTriggerCommandOutput.Fail(requirements.Message);

        var result = triggers.AddTrigger(settings.Key, settings.Title, requirements.Payload!);
        return result.Success
            ? ActivationTriggerCommandOutput.Changed("Added", result.Payload!)
            : ActivationTriggerCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Activation trigger key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<title>")]
        [Description("Activation trigger title")]
        public string Title { get; init; } = string.Empty;

        [CommandOption("--requirements <REQUIREMENTS>")]
        [Description("Comma-separated task:<id> or milestone:<key> requirements")]
        public string? Requirements { get; init; }
    }
}

public sealed class ActivationTriggerRenameCommand(ActivationTriggerService triggers)
    : Command<ActivationTriggerRenameCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = triggers.RenameTrigger(settings.Key, settings.Title);
        return result.Success
            ? ActivationTriggerCommandOutput.Changed("Renamed", result.Payload!)
            : ActivationTriggerCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Activation trigger key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<title>")]
        [Description("New activation trigger title")]
        public string Title { get; init; } = string.Empty;
    }
}

public sealed class ActivationTriggerRemoveCommand(ActivationTriggerService triggers)
    : Command<ActivationTriggerRemoveCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = triggers.RemoveTrigger(settings.Key);
        return result.Success
            ? ActivationTriggerCommandOutput.Changed("Removed", result.Payload!)
            : ActivationTriggerCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Activation trigger key")]
        public string Key { get; init; } = string.Empty;
    }
}

public sealed class ActivationTriggerSetRequirementsCommand(ActivationTriggerService triggers)
    : Command<ActivationTriggerSetRequirementsCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Clear == (settings.Requirements != null))
            return ActivationTriggerCommandOutput.Fail(
                "Specify exactly one of --requirements or --clear.");

        var requirements = settings.Clear
            ? AppResult<IReadOnlyList<ActivationRequirement>>.Ok([])
            : ActivationRequirementInput.ParseRequired(settings.Requirements!);
        if (!requirements.Success) return ActivationTriggerCommandOutput.Fail(requirements.Message);

        var result = triggers.SetRequirements(settings.Key, requirements.Payload!);
        return result.Success
            ? ActivationTriggerCommandOutput.Changed("Updated", result.Payload!)
            : ActivationTriggerCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Activation trigger key")]
        public string Key { get; init; } = string.Empty;

        [CommandOption("--requirements <REQUIREMENTS>")]
        [Description("Comma-separated task:<id> or milestone:<key> requirements")]
        public string? Requirements { get; init; }

        [CommandOption("--clear")]
        [Description("Remove every requirement and make the trigger manual-only")]
        public bool Clear { get; init; }

        public override ValidationResult Validate() =>
            Clear == (Requirements != null)
                ? ValidationResult.Error("Specify exactly one of --requirements or --clear.")
                : ValidationResult.Success();
    }
}

public sealed class ActivationTriggerAttachCommand(ActivationTriggerService triggers)
    : Command<ActivationTriggerAttachCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = triggers.AttachTrigger(settings.Key, settings.Milestone);
        return result.Success
            ? ActivationTriggerCommandOutput.Changed("Attached", result.Payload!)
            : ActivationTriggerCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<trigger-key>")]
        [Description("Activation trigger key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<milestone-key>")]
        [Description("Milestone that should require the trigger")]
        public string Milestone { get; init; } = string.Empty;
    }
}

public sealed class ActivationTriggerRedefineCommand(
    ActivationTriggerService triggers,
    IActivationTriggerCommandPrompts prompts)
    : Command<ActivationTriggerRedefineCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Clear == (settings.Requirements != null))
            return ActivationTriggerCommandOutput.Fail(
                "Specify exactly one of --requirements or --clear.");

        var requirements = settings.Clear
            ? AppResult<IReadOnlyList<ActivationRequirement>>.Ok([])
            : ActivationRequirementInput.ParseRequired(settings.Requirements!);
        if (!requirements.Success) return ActivationTriggerCommandOutput.Fail(requirements.Message);

        var preview = triggers.PreviewRedefinition(settings.Key, requirements.Payload!);
        if (!preview.Success) return ActivationTriggerCommandOutput.Fail(preview.Message);
        ActivationTriggerCommandOutput.WritePreview(preview.Payload!);

        var approved = settings.Yes;
        if (preview.Payload!.RequiresConfirmation && !approved)
        {
            approved = prompts.Confirm(
                $"Redefinition will make eligible milestone(s) inactive and remove " +
                $"{preview.Payload.TaskIdsLosingEligibility.Count} task(s) from activation eligibility. Continue?");
            if (!approved)
            {
                AnsiConsole.MarkupLine("[yellow]Activation trigger redefinition cancelled.[/]");
                return 1;
            }
        }

        var result = triggers.RedefineTrigger(
            settings.Key,
            requirements.Payload!,
            preview.Payload.Revision,
            approved);
        return result.Success
            ? ActivationTriggerCommandOutput.Redefined(result.Payload!)
            : ActivationTriggerCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Active activation trigger key")]
        public string Key { get; init; } = string.Empty;

        [CommandOption("--requirements <REQUIREMENTS>")]
        [Description("Comma-separated task:<id> or milestone:<key> requirements")]
        public string? Requirements { get; init; }

        [CommandOption("--clear")]
        [Description("Replace the definition with a manual-only trigger")]
        public bool Clear { get; init; }

        [CommandOption("--yes")]
        [Description("Confirm milestone eligibility loss without prompting")]
        public bool Yes { get; init; }

        public override ValidationResult Validate() =>
            Clear == (Requirements != null)
                ? ValidationResult.Error("Specify exactly one of --requirements or --clear.")
                : ValidationResult.Success();
    }
}

public sealed class ActivationTriggerDetachCommand(ActivationTriggerService triggers)
    : Command<ActivationTriggerDetachCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = triggers.DetachTrigger(settings.Key, settings.Milestone);
        return result.Success
            ? ActivationTriggerCommandOutput.Changed("Detached", result.Payload!)
            : ActivationTriggerCommandOutput.Fail(result.Message);
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<trigger-key>")]
        [Description("Activation trigger key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<milestone-key>")]
        [Description("Milestone that should no longer require the trigger")]
        public string Milestone { get; init; } = string.Empty;
    }
}

public sealed class ActivationTriggerListCommand(ActivationTriggerService triggers)
    : Command<ActivationTriggerListCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = triggers.ListTriggers();
        if (!result.Success) return ActivationTriggerCommandOutput.Fail(result.Message);

        var table = new Table()
            .AddColumn("Key")
            .AddColumn("Title")
            .AddColumn("Activation")
            .AddColumn("Requirements")
            .AddColumn("Milestones");

        foreach (var trigger in result.Payload!)
        {
            var activation = trigger.Activation?.Mode.ToString().ToLowerInvariant()
                             ?? (trigger.RequirementCount == 0 ? "manual-only" : "pending");
            var requirements = trigger.Requirements.Count == 0
                ? "-"
                : string.Join(", ", trigger.Requirements.Select(requirement =>
                    $"{requirement.Kind.ToString().ToLowerInvariant()}:{requirement.Source}"));
            var milestones = trigger.ConsumingMilestones.Count == 0
                ? "-"
                : string.Join(", ", trigger.ConsumingMilestones);

            table.AddRow(
                trigger.Key.EscapeMarkup(),
                trigger.Title.EscapeMarkup(),
                activation.EscapeMarkup(),
                requirements.EscapeMarkup(),
                milestones.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }

    public sealed class Settings : CommandSettings;
}

internal static class ActivationRequirementInput
{
    public static AppResult<IReadOnlyList<ActivationRequirement>> ParseOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? AppResult<IReadOnlyList<ActivationRequirement>>.Ok([])
            : Parse(value);

    public static AppResult<IReadOnlyList<ActivationRequirement>> ParseRequired(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Invalid("Requirements cannot be empty. Use --clear to remove every requirement.")
            : Parse(value);

    private static AppResult<IReadOnlyList<ActivationRequirement>> Parse(string value)
    {
        var requirements = new List<ActivationRequirement>();
        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0 || separator == entry.Length - 1)
                return Invalid($"Activation requirement {entry} must use task:<id> or milestone:<key>.");

            var kindText = entry[..separator].Trim();
            var source = entry[(separator + 1)..].Trim();
            var kind = kindText.ToLowerInvariant() switch
            {
                "task" => ActivationRequirementKind.Task,
                "milestone" => ActivationRequirementKind.Milestone,
                _ => (ActivationRequirementKind?)null,
            };
            if (kind == null || string.IsNullOrWhiteSpace(source))
                return Invalid($"Activation requirement {entry} must use task:<id> or milestone:<key>.");

            requirements.Add(new ActivationRequirement { Kind = kind.Value, Source = source });
        }

        return AppResult<IReadOnlyList<ActivationRequirement>>.Ok(requirements);
    }

    private static AppResult<IReadOnlyList<ActivationRequirement>> Invalid(string message) =>
        AppResult<IReadOnlyList<ActivationRequirement>>.Fail("invalid_activation_requirement", message);
}

internal static class ActivationTriggerCommandOutput
{
    public static void WritePreview(ActivationTriggerRedefinitionPreview preview)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Redefinition preview for [green]{preview.TriggerKey.EscapeMarkup()}[/]:");
        AnsiConsole.MarkupLineInterpolated(
            $"Proposed activation: [blue]{(preview.WillReactivateAutomatically ? "automatic" : "pending")}[/]");

        var table = new Table()
            .AddColumn("Milestone")
            .AddColumn("Before")
            .AddColumn("After")
            .AddColumn("Eligible tasks")
            .AddColumn("Losing eligibility");
        foreach (var impact in preview.Milestones)
            table.AddRow(
                impact.MilestoneKey.EscapeMarkup(),
                impact.Before.ToString().EscapeMarkup(),
                impact.After.ToString().EscapeMarkup(),
                FormatTaskIds(impact.CurrentlyEligibleTaskIds),
                FormatTaskIds(impact.TaskIdsLosingEligibility));
        AnsiConsole.Write(table);
    }

    public static int Redefined(ActivationTriggerRedefinitionResult result)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Redefined activation trigger [green]{result.TriggerKey.EscapeMarkup()}[/].");
        var activation = result.IsActive
            ? $"active {result.ActivationMode!.Value.ToString().ToLowerInvariant()}"
            : "pending";
        AnsiConsole.MarkupLineInterpolated($"Activation: [blue]{activation.EscapeMarkup()}[/].");
        var affected = result.AffectedMilestones.Count == 0
            ? "none"
            : string.Join(", ", result.AffectedMilestones);
        AnsiConsole.MarkupLineInterpolated($"Affected milestones: [blue]{affected.EscapeMarkup()}[/].");
        return 0;
    }

    public static int Changed(string operation, ActivationTriggerMutationResult result)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"{operation} activation trigger [green]{result.TriggerKey.EscapeMarkup()}[/].");
        var affected = result.AffectedMilestones.Count == 0
            ? "none"
            : string.Join(", ", result.AffectedMilestones);
        AnsiConsole.MarkupLineInterpolated($"Affected milestones: [blue]{affected.EscapeMarkup()}[/].");
        return 0;
    }

    public static int Fail(string? message)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"[red]{(message ?? "Activation trigger operation failed.").EscapeMarkup()}[/]");
        return 1;
    }

    private static string FormatTaskIds(IReadOnlyList<string> taskIds) =>
        (taskIds.Count == 0 ? "-" : string.Join(", ", taskIds)).EscapeMarkup();
}
