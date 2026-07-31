using System.ComponentModel;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class TaskRemoveCommand(LinkedProjectMutationService mutations) : AsyncCommand<TaskRemoveCommand.Settings>
{
    public int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        ExecuteAsync(context, settings, cancellationToken).GetAwaiter().GetResult();

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var target = await mutations.ResolveTargetAsync(settings.Project, cancellationToken: cancellationToken);
        if (!target.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{target.Message!.EscapeMarkup()}[/]");
            return 1;
        }

        using var mutation = mutations.Track(target.Payload!);
        var result = target.Payload!.Tasks.RemoveTask(settings.TaskId);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(result.Message ?? "Task remove failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Removed task [green]{settings.TaskId.Trim().EscapeMarkup()}[/].");
        LinkedProjectConsole.WriteReceipt(mutation.Receipt);
        return 0;
    }

    public class Settings : LinkedProjectMutationSettings
    {
        [CommandArgument(0, "<task-id>")]
        [Description("Task ID")]
        public string TaskId { get; init; } = string.Empty;
    }
}
