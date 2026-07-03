using System.Text.RegularExpressions;
using PM.Project;

namespace PM.Web;

public partial class BoardQueryService(ProjectRoot projectRoot)
{
    private const int DescriptionPreviewLength = 96;

    public BoardData GetBoard(BoardQuery query)
    {
        var config = projectRoot.Config ?? throw new InvalidOperationException("Project root is not initialized.");
        var entries = projectRoot.GetAllTasks()
            .Select(task => new BoardTask(
                task,
                projectRoot.ResolveTaskTrack(task),
                task.Milestone,
                projectRoot.TryGetState(task, out var state) ? state : string.Empty,
                GetDescriptionPreview(task.Description),
                projectRoot.GetTaskFilePath(task.Id)))
            .Where(entry => string.IsNullOrWhiteSpace(query.Track) || entry.Track == query.Track)
            .Where(entry => string.IsNullOrWhiteSpace(query.Milestone) || entry.Milestone == query.Milestone)
            .Where(entry => string.IsNullOrWhiteSpace(query.State) || entry.State == query.State)
            .ToList();

        var milestoneKeys = entries
            .Select(entry => entry.Milestone)
            .Where(milestone => !string.IsNullOrWhiteSpace(milestone))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(milestone => milestone, StringComparer.Ordinal)
            .ToList();

        if (string.IsNullOrWhiteSpace(query.Milestone))
            milestoneKeys.Add(null);
        else if (!milestoneKeys.Contains(query.Milestone, StringComparer.Ordinal))
            milestoneKeys.Add(query.Milestone);

        var stateOptions = config.TaskStates
            .Select(state => new BoardOption(state.Key, state.Value))
            .ToList();

        var groups = milestoneKeys
            .Select(milestone => new BoardMilestoneGroup(
                milestone,
                ResolveMilestoneTitle(milestone),
                stateOptions
                    .Where(state => string.IsNullOrWhiteSpace(query.State) || state.Key == query.State)
                    .Select(state => new BoardStateGroup(
                        state.Key,
                        state.Name,
                        entries
                            .Where(entry => string.Equals(entry.Milestone, milestone, StringComparison.Ordinal))
                            .Where(entry => entry.State == state.Key)
                            .OrderByDescending(entry => entry.Task.ModifiedAt)
                            .ThenBy(entry => entry.Task.Id, StringComparer.Ordinal)
                            .ToList()))
                    .ToList()))
            .ToList();

        return new BoardData(
            config.Name,
            config.Tracks.Select(track => new BoardOption(track.Key, track.Value)).ToList(),
            config.Milestones.Select(milestone => new BoardOption(milestone.Key, milestone.Value)).ToList(),
            stateOptions,
            groups,
            query);
    }

    private string ResolveMilestoneTitle(string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return "Unassigned";
        return projectRoot.Config!.Milestones.TryGetValue(milestone, out var title) ? title : milestone;
    }

    public static string GetDescriptionPreview(string description)
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

    [GeneratedRegex(@"^(#{1,6}\s+|(?:[-*+]\s+)?\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+|>\s+)")]
    private static partial Regex MarkdownPrefixRegex();
}

