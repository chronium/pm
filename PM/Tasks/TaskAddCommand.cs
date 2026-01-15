using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Tasks;

public class TaskAddCommand(ProjectRoot projectRoot, INextIdService nextIdService, ISyntaxHighlighter highlighter)
    : AsyncCommand<TaskAddCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (await ValidateProjectAndServiceHealth(cancellationToken) != 0) return 1;

        var taskItem = await GenerateTaskItem(settings, cancellationToken);

        RenderTaskPanel(taskItem);

        WriteTaskAndSetState(taskItem);

        return 0;
    }

    private void WriteTaskAndSetState(TaskItem taskItem)
    {
        projectRoot.WriteTask(taskItem);
        projectRoot.UpdateTaskState(taskItem, projectRoot.Config!.TaskStates.Keys.First());
    }

    private void RenderTaskPanel(TaskItem taskItem)
    {
        var yaml = YamlSerde.Serialize(taskItem);

        var sb = new StringBuilder();
        sb.AppendLine("---");
        highlighter.Highlight(yaml, "md", new MarkupTokenRenderer(sb));
        sb.AppendLine("---");

        var taskPanel = new Panel(sb.ToString())
        {
            Header = new($"{taskItem.Id}.{GlobalConfig.DefaultTaskExtension}"),
            Border = new RoundedBoxBorder(),
        };

        AnsiConsole.Write(taskPanel);
    }

    private async Task<TaskItem> GenerateTaskItem(Settings settings,
        CancellationToken cancellationToken)
    {
        var nextId = settings.DryRun
            ? await nextIdService.PeekNextId(projectRoot, cancellationToken)
            : await nextIdService.GetNextId(projectRoot, cancellationToken);

        var idPadded = nextId.ToString().PadLeft(projectRoot.Config!.IdWidth, '0');
        var prefixedId = $"{projectRoot.Config.IdPrefix}-{idPadded}";

        var title = settings.Title.Trim();

        AnsiConsole.MarkupLine($"[green]Task created with ID {prefixedId}: [/]{title}");

        var taskItem = new TaskItem
        {
            Id = prefixedId,
            Title = title,
        };
        return taskItem;
    }

    private async Task<int> ValidateProjectAndServiceHealth(CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        if (!await nextIdService.Healthy(cancellationToken))
        {
            AnsiConsole.MarkupLine("[red]Unable to reach the next ID service.[/]");
            return 1;
        }

        return 0;
    }

    public class Settings : CommonSettings
    {
        [CommandArgument(0, "<title>")] public string Title { get; init; } = string.Empty;
    }
}