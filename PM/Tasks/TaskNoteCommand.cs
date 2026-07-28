using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public sealed class TaskNoteCommand(TaskService taskService, IEditorService editorService)
    : AsyncCommand<TaskNoteCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var note = settings.Note;
        if (settings.Edit || note == null)
        {
            note = await EditNote(note ?? string.Empty, cancellationToken);
            if (note == null) return 1;
        }

        var result = taskService.AppendTaskNote(settings.TaskId, note);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Task note could not be added.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Added note to task [green]{settings.TaskId.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    private async Task<string?> EditNote(string initialNote, CancellationToken cancellationToken)
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"pm-task-note-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFilePath, initialNote, cancellationToken);

        try
        {
            var exitCode = await editorService.EditFile(tempFilePath, cancellationToken);
            if (exitCode != 0)
            {
                AnsiConsole.MarkupLine($"[red]Editor exited with code {exitCode}. Task note was not added.[/]");
                return null;
            }

            return await File.ReadAllTextAsync(tempFilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Unable to launch editor: {ex.Message.EscapeMarkup()}[/]");
            return null;
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<task-id>")]
        [Description("Task ID")]
        public string TaskId { get; init; } = string.Empty;

        [CommandArgument(1, "[note]")]
        [Description("Note text; omit to open the configured editor")]
        public string? Note { get; init; }

        [CommandOption("--edit")]
        [Description("Open an editor, seeded with the supplied note text")]
        public bool Edit { get; init; }
    }
}
