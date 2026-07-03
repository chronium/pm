using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class TaskRemoveCommand(TaskService taskService) : Command<TaskRemoveCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = taskService.RemoveTask(settings.TaskId);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(result.Message ?? "Task remove failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Removed task [green]{settings.TaskId.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<task-id>")]
        [Description("Task ID")]
        public string TaskId { get; init; } = string.Empty;
    }
}
