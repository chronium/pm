using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class DoctorCommand(
    ProjectValidationService validationService,
    ProjectConfigService configService) : Command<DoctorCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Fix)
        {
            var migration = configService.MigrateMilestoneSchema();
            if (!migration.Success)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]{(migration.Message ?? "Project repair failed.").EscapeMarkup()}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine(migration.Payload
                ? "Migrated milestone configuration to the structured schema."
                : "Milestone configuration already uses the structured schema.");
        }

        var result = validationService.ValidateProject();
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Project validation failed.").EscapeMarkup()}[/]");
            return 1;
        }

        var validation = result.Payload!;
        if (validation.Valid)
        {
            if (validation.Issues.Count == 0)
                AnsiConsole.MarkupLine("Project validation passed.");
            else
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[yellow]Project validation passed with {validation.Issues.Count} warning(s).[/]");
                foreach (var issue in validation.Issues)
                    AnsiConsole.MarkupLine(FormatIssue(issue));
            }
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[red]Project validation found {validation.Issues.Count} issue(s).[/]");
        foreach (var issue in validation.Issues)
            AnsiConsole.MarkupLine(FormatIssue(issue));

        return 1;
    }

    private static string FormatIssue(ProjectValidationIssue issue)
    {
        var context = FormatContext(issue);
        var suffix = string.IsNullOrWhiteSpace(context) ? string.Empty : $" ({context})";
        return
            $"[{SeverityColor(issue.Severity)}]{issue.Severity.EscapeMarkup()}[/] " +
            $"{issue.Code.EscapeMarkup()}: {issue.Message.EscapeMarkup()}{suffix}";
    }

    private static string FormatContext(ProjectValidationIssue issue)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(issue.TaskId)) parts.Add($"task {issue.TaskId.EscapeMarkup()}");
        if (!string.IsNullOrWhiteSpace(issue.WikiPath)) parts.Add($"wiki {issue.WikiPath.EscapeMarkup()}");
        if (!string.IsNullOrWhiteSpace(issue.State)) parts.Add($"state {issue.State.EscapeMarkup()}");
        if (!string.IsNullOrWhiteSpace(issue.ProjectId)) parts.Add($"project {issue.ProjectId.EscapeMarkup()}");
        if (!string.IsNullOrWhiteSpace(issue.ProjectAlias)) parts.Add($"alias {issue.ProjectAlias.EscapeMarkup()}");
        if (!string.IsNullOrWhiteSpace(issue.Path)) parts.Add($"path {issue.Path.EscapeMarkup()}");
        return string.Join("; ", parts);
    }

    private static string SeverityColor(string severity)
    {
        return string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase) ? "red" : "yellow";
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--fix")]
        [Description("Apply supported project metadata repairs before validation")]
        public bool Fix { get; init; }
    }
}
