using System.ComponentModel;
using System.Text.RegularExpressions;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public partial class ListCommand(ProjectRoot projectRoot) : AsyncCommand<ListCommand.Settings>
{
    private const int DescriptionPreviewLength = 48;

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (ValidateProjectAndServiceHealth() != 0) return Task.FromResult(1);

        if (!ValidateFilters(settings)) return Task.FromResult(1);

        var entries = projectRoot.GetAllTasks()
            .Select(task => new TaskListEntry(
                task,
                projectRoot.ResolveTaskTrack(task),
                task.Milestone,
                projectRoot.TryGetState(task, out var state) ? state : string.Empty))
            .Where(entry => string.IsNullOrWhiteSpace(settings.Track) || entry.Track == settings.Track)
            .Where(entry => string.IsNullOrWhiteSpace(settings.Milestone) || entry.Milestone == settings.Milestone)
            .Where(entry => string.IsNullOrWhiteSpace(settings.State) || entry.State == settings.State)
            .ToList();

        var milestoneKeys = entries
            .Select(entry => entry.Milestone)
            .Where(milestone => !string.IsNullOrWhiteSpace(milestone))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(milestone => milestone, StringComparer.Ordinal)
            .ToList();

        if (string.IsNullOrWhiteSpace(settings.Milestone))
            milestoneKeys.Add(null);
        else if (!milestoneKeys.Contains(settings.Milestone, StringComparer.Ordinal))
            milestoneKeys.Add(settings.Milestone);

        foreach (var milestone in milestoneKeys)
        {
            var milestoneEntries = entries
                .Where(entry => string.Equals(entry.Milestone, milestone, StringComparison.Ordinal))
                .ToList();

            var milestoneTitle = ResolveMilestoneTitle(milestone);
            AnsiConsole.Write(new Rule(Markup.Escape(milestoneTitle)).RuleStyle("grey"));

            var states = projectRoot.Config!.TaskStates
                .Where(state => string.IsNullOrWhiteSpace(settings.State) || state.Key == settings.State);

            foreach (var (state, name) in states)
            {
                var stateEntries = milestoneEntries
                    .Where(entry => entry.State == state)
                    .OrderByDescending(entry => entry.Task.ModifiedAt)
                    .ThenBy(entry => entry.Task.Id, StringComparer.Ordinal)
                    .ToList();

                AnsiConsole.Write(BuildStateTable(state, name, stateEntries));
            }
        }

        return Task.FromResult(0);
    }

    private bool ValidateFilters(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.State) &&
            !projectRoot.Config!.TaskStates.ContainsKey(settings.State))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]State {settings.State.EscapeMarkup()} not found.[/]");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.Track) &&
            !projectRoot.Config!.Tracks.ContainsKey(settings.Track))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Track {settings.Track.EscapeMarkup()} not found.[/]");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.Milestone) &&
            !projectRoot.Config!.Milestones.ContainsKey(settings.Milestone))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Milestone {settings.Milestone.EscapeMarkup()} not found.[/]");
            return false;
        }

        return true;
    }

    private string ResolveMilestoneTitle(string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return "Unassigned";
        return projectRoot.Config!.Milestones.TryGetValue(milestone, out var title) ? title : milestone;
    }

    private static Table BuildStateTable(string state, string name, List<TaskListEntry> items)
    {
        var table = new Table()
            .Title($"{Markup.Escape(name)} ([darkOrange]{items.Count}[/])")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("ID")
            .AddColumn("Title")
            .AddColumn("Track")
            .AddColumn("State")
            .AddColumn("Modified")
            .AddColumn("Description");

        foreach (var entry in items)
            table.AddRow(
                Markup.Escape(entry.Task.Id),
                Markup.Escape(entry.Task.Title),
                Markup.Escape(entry.Track),
                Markup.Escape(state),
                Markup.Escape(FormatModifiedAt(entry.Task.ModifiedAt)),
                Markup.Escape(GetDescriptionPreview(entry.Task.Description)));

        return table;
    }

    private static string FormatModifiedAt(DateTime modifiedAt)
    {
        return modifiedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string GetDescriptionPreview(string description)
    {
        var firstLine = description
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => StripMarkdownPrefix(line.Trim()))
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        if (string.IsNullOrWhiteSpace(firstLine)) return string.Empty;

        return firstLine.Length <= DescriptionPreviewLength
            ? firstLine
            : $"{firstLine[..(DescriptionPreviewLength - 3)]}...";
    }

    private static string StripMarkdownPrefix(string line)
    {
        return MarkdownPrefixRegex().Replace(line, string.Empty).Trim();
    }

    private int ValidateProjectAndServiceHealth()
    {
        if (projectRoot.Exists) return 0;

        AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
        return 1;
    }

    public class Settings : CommonSettings
    {
        [CommandOption("--state <STATE>")]
        [Description("List tasks in one state")]
        public string? State { get; init; }

        [CommandOption("--track <TRACK>")]
        [Description("List tasks in one track")]
        public string? Track { get; init; }

        [CommandOption("--milestone <MILESTONE>")]
        [Description("List tasks in one milestone")]
        public string? Milestone { get; init; }
    }

    private sealed record TaskListEntry(TaskItem Task, string Track, string? Milestone, string State);

    [GeneratedRegex(@"^(#{1,6}\s+|(?:[-*+]\s+)?\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+|>\s+)")]
    private static partial Regex MarkdownPrefixRegex();
}
