using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class ListCommand(ProjectRoot projectRoot) : AsyncCommand<CommonSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, CommonSettings settings,
        CancellationToken cancellationToken)
    {
        if (ValidateProjectAndServiceHealth() != 0) return Task.FromResult(1);

        foreach (var (state, name) in projectRoot.Config!.TaskStates)
        {
            var items = projectRoot.GetTasksInState(state);

            var tree = new Tree(Markup.FromInterpolated($"{name} ([darkOrange]{items.Count}[/])"));

            foreach (var item in items) tree.AddNode($"[[[darkBlue]{item.Id}[/]]] {item.Title}");

            AnsiConsole.Write(tree);
        }

        return Task.FromResult(0);
    }

    private int ValidateProjectAndServiceHealth()
    {
        if (projectRoot.Exists) return 0;

        AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
        return 1;
    }
}
