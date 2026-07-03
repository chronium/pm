using System.ComponentModel;
using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Tasks;

public class TaskEditCommand(ProjectRoot projectRoot, IEditorService editorService, ISyntaxHighlighter highlighter)
    : AsyncCommand<TaskEditCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        if (!projectRoot.TryReadTaskFile(settings.TaskId, out var originalContent))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Task {settings.TaskId.EscapeMarkup()} not found.[/]");
            return 1;
        }

        if (settings.DryRun)
        {
            RenderTaskPanel(settings.TaskId, originalContent);
            return 0;
        }

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"pm-task-edit-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFilePath, originalContent, cancellationToken);

        try
        {
            var exitCode = await editorService.EditFile(tempFilePath, cancellationToken);
            if (exitCode != 0)
            {
                AnsiConsole.MarkupLine($"[red]Editor exited with code {exitCode}. Task edit aborted.[/]");
                return 1;
            }

            var editedContent = await File.ReadAllTextAsync(tempFilePath, cancellationToken);
            var editedTask = TaskItem.Parse(editedContent);
            if (editedTask == null)
            {
                AnsiConsole.MarkupLine("[red]Edited task markdown is invalid. Task edit aborted.[/]");
                return 1;
            }

            if (!string.Equals(editedTask.Id, settings.TaskId, StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine("[red]Task ID cannot be changed. Task edit aborted.[/]");
                return 1;
            }

            projectRoot.WriteTaskFile(settings.TaskId, editedContent);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Unable to launch editor: {ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    private void RenderTaskPanel(string taskId, string markdown)
    {
        var sb = new StringBuilder();
        highlighter.Highlight(markdown, "md", new MarkupTokenRenderer(sb));

        var taskPanel = new Panel(sb.ToString())
        {
            Header = new($"{taskId}.{GlobalConfig.DefaultTaskExtension}"),
            Border = new RoundedBoxBorder(),
        };

        AnsiConsole.Write(taskPanel);
    }

    public class Settings : CommonSettings
    {
        [CommandArgument(0, "<task-id>")]
        [Description("Task ID to edit")]
        public string TaskId { get; init; } = string.Empty;
    }
}
