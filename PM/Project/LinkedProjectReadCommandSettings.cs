using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public abstract class LinkedProjectSelectorSettings : CommonSettings
{
    [CommandOption("--project <SELECTOR>")]
    [Description("Read from current, parent, a stable project ID, or a unique linked-project alias")]
    public string? Project { get; init; }

    public AppResult<LinkedProjectReadRequest> ToProjectReadRequest() =>
        LinkedProjectReadRequest.FromOptions(Project, false);
}

public abstract class LinkedProjectMutationSettings : CommonSettings
{
    [CommandOption("--project <SELECTOR>")]
    [Description("Write to current, parent, a stable project ID, or a trusted linked-project alias")]
    public string? Project { get; init; }
}

public abstract class LinkedProjectAggregateReadSettings : LinkedProjectSelectorSettings
{
    [CommandOption("--family")]
    [Description("Read across every available project in the linked family")]
    public bool Family { get; init; }

    public override ValidationResult Validate() =>
        Family && !string.IsNullOrWhiteSpace(Project)
            ? ValidationResult.Error("--project and --family cannot be used together.")
            : ValidationResult.Success();

    public AppResult<LinkedProjectReadRequest> ToLinkedReadRequest() =>
        LinkedProjectReadRequest.FromOptions(Project, Family);
}

public static class LinkedProjectConsole
{
    public static void WriteReceipt(ProjectMutationReceipt receipt)
    {
        var paths = receipt.ChangedPaths.Count == 0
            ? "no files changed"
            : string.Join(", ", receipt.ChangedPaths);
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Project {receipt.ProjectId.EscapeMarkup()} · {paths.EscapeMarkup()}[/]");
    }

    public static string ProjectLabel(LinkedProjectResourceOwner owner)
    {
        var alias = owner.Alias is null ? string.Empty : $" / {owner.Alias}";
        return $"{owner.ProjectName}{alias} ({owner.ProjectId})";
    }

    public static void WriteSource(LinkedProjectResourceOwner owner)
    {
        var revision = owner.Revision == null ? "revision unavailable" : owner.Revision;
        var dirty = owner.Dirty switch
        {
            true => "dirty",
            false => "clean",
            null => "working-tree state unavailable",
        };
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]{ProjectLabel(owner).EscapeMarkup()} · {LinkedProjectFamilyService.Format(owner.Relationship).EscapeMarkup()} · {revision.EscapeMarkup()} · {dirty.EscapeMarkup()}[/]");
    }

    public static void WriteWarnings(IReadOnlyList<LinkedProjectFamilyWarning> warnings)
    {
        foreach (var warning in warnings)
            AnsiConsole.MarkupLineInterpolated($"[yellow]Warning: {warning.Message.EscapeMarkup()}[/]");
    }
}
