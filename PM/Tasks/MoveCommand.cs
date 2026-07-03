using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class MoveCommand(ProjectRoot projectRoot) : AsyncCommand<MoveCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (ValidateProjectAndServiceHealth(cancellationToken) != 0) return 1;

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
        projectRoot.UpdateTaskState(task, newState);

        AnsiConsole.MarkupLine($"[green]Task {settings.TaskId} moved to state {newState}[/]");

        return 0;
    }

    private int ValidateProjectAndServiceHealth(CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        return 0;
    }

    public class Settings : CommonSettings
    {
        [CommandArgument(0, "<task-id>")] public string TaskId { get; init; } = string.Empty;
    }
}
