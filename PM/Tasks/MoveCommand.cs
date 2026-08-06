using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class MoveCommand(LinkedProjectMutationService mutations) : AsyncCommand<MoveCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var target = await mutations.ResolveTargetAsync(settings.Project, cancellationToken: cancellationToken);
        if (!target.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{target.Message!.EscapeMarkup()}[/]");
            return 1;
        }
        var projectRoot = target.Payload!.Root;

        if (!projectRoot.TryGetById(settings.TaskId, out var task))
        {
            AnsiConsole.MarkupLine($"[red]Task with ID {settings.TaskId} not found.[/]");
            return 1;
        }

        if (!projectRoot.TryGetState(task, out var currentState))
        {
            AnsiConsole.MarkupLine($"[red]Task with ID {settings.TaskId} has no associated state.[/]");
            return 1;
        }

        var newStatePrompt = new SelectionPrompt<string>()
            .Title($"Select new [green]state[/]. Current state: [green]{currentState}[/].")
            .UseConverter(key => $"{projectRoot.Config!.TaskStates[key]} ({key})")
            .AddChoices(projectRoot.Config!.TaskStates.Keys);

        var newState = await AnsiConsole.PromptAsync(newStatePrompt, cancellationToken);
        using var mutation = mutations.Track(target.Payload);
        var result = target.Payload.Tasks.MoveTask(settings.TaskId, newState);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(result.Message ?? "Task move failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Task {settings.TaskId} moved to state {newState}[/]");
        LifecycleMutationCommandOutput.Write(result.Payload!.ActivationImpact);
        LinkedProjectConsole.WriteReceipt(mutation.Receipt);

        return 0;
    }

    public class Settings : LinkedProjectMutationSettings
    {
        [CommandArgument(0, "<task-id>")] public string TaskId { get; init; } = string.Empty;
    }
}
