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
        if (await ValidateProjectAndServiceHealth(settings, cancellationToken) != 0) return 1;

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
        var config = projectRoot.Config!;
        var idPadded = settings.DryRun
            ? await GetDryRunId(cancellationToken)
            : (await nextIdService.GetNextId(projectRoot, cancellationToken)).ToString()
            .PadLeft(config.IdWidth, '0');
        var prefixedId = $"{config.IdPrefix}-{idPadded}";

        var title = settings.Title.Trim();

        AnsiConsole.MarkupLine($"[green]Task created with ID {prefixedId}: [/]{title}");

        var taskItem = new TaskItem
        {
            Id = prefixedId,
            Title = title,
        };
        return taskItem;
    }

    private async Task<string> GetDryRunId(CancellationToken cancellationToken)
    {
        var nextId = await nextIdService.PeekExistingNextId(projectRoot, cancellationToken);
        return nextId?.ToString().PadLeft(projectRoot.Config!.IdWidth, '0')
               ?? new string('?', projectRoot.Config!.IdWidth);
    }

    private async Task<int> ValidateProjectAndServiceHealth(Settings settings, CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        if (settings.DryRun && !File.Exists(Path.Combine(projectRoot.RootPath!, GlobalConfig.NextIdFile)))
            return 0;

        if (!await nextIdService.Healthy(projectRoot.Config!, cancellationToken))
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
