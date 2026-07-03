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

public class TaskAddCommand(
    ProjectRoot projectRoot,
    TaskService taskService,
    ISyntaxHighlighter highlighter,
    IEditorService editorService)
    : AsyncCommand<TaskAddCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        var track = await ResolveTrack(settings, cancellationToken);
        if (track == null) return 1;

        var description = await ResolveDescription(settings, cancellationToken);
        if (description == null) return 1;

        var result = await taskService.CreateTask(
            settings.Title,
            track,
            settings.Milestone,
            description,
            settings.DryRun,
            cancellationToken);
        if (!result.Success)
        {
            RenderError(result.Message);
            return 1;
        }

        var taskItem = result.Payload!;
        AnsiConsole.MarkupLine($"[green]Task created with ID {taskItem.Id}: [/]{taskItem.Title}");
        RenderTaskPanel(taskItem);

        return 0;
    }

    private void RenderTaskPanel(TaskItem taskItem)
    {
        var yaml = taskItem.ToMarkdown();

        var sb = new StringBuilder();
        highlighter.Highlight(yaml, "md", new MarkupTokenRenderer(sb));

        var taskPanel = new Panel(sb.ToString())
        {
            Header = new($"{taskItem.Id}.{GlobalConfig.DefaultTaskExtension}"),
            Border = new RoundedBoxBorder(),
        };

        AnsiConsole.Write(taskPanel);
    }

    private async Task<string?> ResolveTrack(Settings settings, CancellationToken cancellationToken)
    {
        var config = projectRoot.Config!;
        var track = settings.Track?.Trim();
        if (!string.IsNullOrWhiteSpace(track))
        {
            if (!config.Tracks.ContainsKey(track))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Track {track.EscapeMarkup()} not found.[/]");
                return null;
            }

            return track;
        }

        if (config.Tracks.Count == 1) return config.DefaultTrackKey;

        var prompt = new SelectionPrompt<string>()
            .Title("Select task track")
            .UseConverter(key => $"{config.Tracks[key]} ({key})")
            .AddChoices(config.Tracks.Keys);

        return await AnsiConsole.PromptAsync(prompt, cancellationToken);
    }

    private async Task<string?> ResolveDescription(Settings settings, CancellationToken cancellationToken)
    {
        var description = settings.Description ?? string.Empty;
        if (!settings.Edit || settings.DryRun) return NormalizeDescription(description);

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"pm-task-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFilePath, description, cancellationToken);

        try
        {
            var exitCode = await editorService.EditFile(tempFilePath, cancellationToken);
            if (exitCode != 0)
            {
                AnsiConsole.MarkupLine($"[red]Editor exited with code {exitCode}. Task creation aborted.[/]");
                return null;
            }

            return NormalizeDescription(await File.ReadAllTextAsync(tempFilePath, cancellationToken));
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

    private static string NormalizeDescription(string description)
    {
        return string.IsNullOrWhiteSpace(description) ? string.Empty : description;
    }

    private static void RenderError(string? message)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? "Task creation failed.").EscapeMarkup()}[/]");
    }

    public class Settings : CommonSettings
    {
        [CommandArgument(0, "<title>")] public string Title { get; init; } = string.Empty;

        [CommandOption("--description <text>")]
        [Description("Markdown description body for the task")]
        public string? Description { get; init; }

        [CommandOption("--edit")]
        [Description("Open an editor for the task description")]
        public bool Edit { get; init; }

        [CommandOption("--track <TRACK>")]
        [Description("Track for the task")]
        public string? Track { get; init; }

        [CommandOption("--milestone <MILESTONE>")]
        [Description("Milestone for the task")]
        public string? Milestone { get; init; }
    }
}
