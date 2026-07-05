using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class DoctorCommand(ProjectValidationService validationService) : Command<DoctorCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
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
            AnsiConsole.MarkupLine("Project validation passed.");
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
        if (!string.IsNullOrWhiteSpace(issue.Path)) parts.Add($"path {issue.Path.EscapeMarkup()}");
        return string.Join("; ", parts);
    }

    private static string SeverityColor(string severity)
    {
        return string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase) ? "red" : "yellow";
    }

    public class Settings : CommandSettings
    {
    }
}
