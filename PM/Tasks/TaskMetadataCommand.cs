using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class TaskMetadataCommand(TaskService taskService) : Command<TaskMetadataCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = taskService.PatchTaskMetadata(
            settings.TaskId,
            settings.Title,
            settings.Track,
            settings.Milestone,
            settings.Description,
            settings.Priority,
            settings.DependsOn == null
                ? null
                : settings.DependsOn.Split(',', StringSplitOptions.TrimEntries));

        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Task metadata update failed.").EscapeMarkup()}[/]");
            return 1;
        }

        var changed = result.Payload!.Changed ? "Updated" : "No changes for";
        AnsiConsole.MarkupLineInterpolated($"{changed} task [green]{settings.TaskId.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<task-id>")]
        [Description("Task ID")]
        public string TaskId { get; init; } = string.Empty;

        [CommandOption("--title <TITLE>")]
        [Description("Task title")]
        public string? Title { get; init; }

        [CommandOption("--track <TRACK>")]
        [Description("Task track")]
        public string? Track { get; init; }

        [CommandOption("--milestone <MILESTONE>")]
        [Description("Task milestone. Use an empty value to clear.")]
        public string? Milestone { get; init; }

        [CommandOption("--priority <PRIORITY>")]
        [Description("Task priority: inherit, none, low, medium, high, urgent")]
        public string? Priority { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        [Description("Markdown description body for the task")]
        public string? Description { get; init; }

        [CommandOption("--depends-on <TASK-IDS>")]
        [Description("Comma-separated local task IDs or canonical pm:// task references. Use an empty value to clear.")]
        public string? DependsOn { get; init; }
    }
}
