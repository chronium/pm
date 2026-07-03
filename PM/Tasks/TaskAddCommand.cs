using System.ComponentModel;
using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Tasks;

public class TaskAddCommand(
    ProjectRoot projectRoot,
    INextIdService nextIdService,
    ISyntaxHighlighter highlighter,
    IEditorService editorService)
    : AsyncCommand<TaskAddCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (await ValidateProjectAndServiceHealth(settings, cancellationToken) != 0) return 1;

        var track = await ResolveTrack(settings, cancellationToken);
        if (track == null) return 1;

        var milestone = ResolveMilestone(settings);
        if (milestone == InvalidMilestone) return 1;

        var description = await ResolveDescription(settings, cancellationToken);
        if (description == null) return 1;

        var taskItem = await GenerateTaskItem(settings, track, milestone, description, cancellationToken);

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

    private const string InvalidMilestone = "\0";

    private async Task<TaskItem> GenerateTaskItem(Settings settings, string track, string? milestone, string description,
        CancellationToken cancellationToken)
    {
        var config = projectRoot.Config!;
        var idPadded = settings.DryRun
            ? await GetDryRunId(track, cancellationToken)
            : (await nextIdService.GetNextId(projectRoot, track, cancellationToken)).ToString()
            .PadLeft(config.IdWidth, '0');
        var prefixedId = $"{track}-{idPadded}";

        var title = settings.Title.Trim();

        AnsiConsole.MarkupLine($"[green]Task created with ID {prefixedId}: [/]{title}");

        var taskItem = new TaskItem
        {
            Id = prefixedId,
            Title = title,
            Track = track,
            Milestone = milestone,
            Description = description,
        };
        return taskItem;
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

    private string? ResolveMilestone(Settings settings)
    {
        var milestone = settings.Milestone?.Trim();
        if (string.IsNullOrWhiteSpace(milestone)) return null;

        if (projectRoot.Config!.Milestones.ContainsKey(milestone)) return milestone;

        AnsiConsole.MarkupLineInterpolated($"[red]Milestone {milestone.EscapeMarkup()} not found.[/]");
        return InvalidMilestone;
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

    private async Task<string> GetDryRunId(string track, CancellationToken cancellationToken)
    {
        var nextId = await nextIdService.PeekExistingNextId(projectRoot, track, cancellationToken);
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
