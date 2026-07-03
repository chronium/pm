using System.ComponentModel;
using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Tasks;

public class TaskEditCommand(TaskService taskService, IEditorService editorService, ISyntaxHighlighter highlighter)
    : AsyncCommand<TaskEditCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var readResult = taskService.ReadTaskMarkdown(settings.TaskId);
        if (!readResult.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(readResult.Message ?? "Task not found.").EscapeMarkup()}[/]");
            return 1;
        }

        var originalContent = readResult.Payload!;
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
            var saveResult = taskService.SaveEditedTaskContent(settings.TaskId, editedContent);
            if (!saveResult.Success)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]{(saveResult.Message ?? "Task edit aborted.").EscapeMarkup()} Task edit aborted.[/]");
                return 1;
            }

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
