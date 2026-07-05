using System.Text.RegularExpressions;
using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed record BoardQuery(string? Track = null, string? Milestone = null, string? State = null);

public sealed record BoardData(
    string ProjectName,
    IReadOnlyList<BoardOption> Tracks,
    IReadOnlyList<BoardOption> Milestones,
    IReadOnlyList<BoardOption> States,
    IReadOnlyList<BoardTask> Tasks,
    IReadOnlyList<BoardMilestoneGroup> MilestoneGroups,
    BoardQuery Query);

public sealed record BoardOption(string Key, string Name);

public sealed record BoardMilestoneGroup(string? Key, string Name, IReadOnlyList<BoardStateGroup> States);

public sealed record BoardStateGroup(string Key, string Name, IReadOnlyList<BoardTask> Tasks);

public sealed record BoardTask(
    TaskItem Task,
    string Track,
    string? Milestone,
    string State,
    string DescriptionPreview,
    string FilePath);

public partial class BoardService(ProjectRoot projectRoot)
{
    public const int CliDescriptionPreviewLength = 48;
    public const int WebDescriptionPreviewLength = 96;

    public AppResult<BoardData> GetBoard(BoardQuery query, int descriptionPreviewLength = WebDescriptionPreviewLength)
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<BoardData>.Fail("missing_project", "Project not found. Run pm init first.");

        var config = projectRoot.Config;
        if (!string.IsNullOrWhiteSpace(query.State) && !config.TaskStates.ContainsKey(query.State))
            return AppResult<BoardData>.Fail("invalid_state", $"State {query.State} not found.");

        if (!string.IsNullOrWhiteSpace(query.Track) && !config.Tracks.ContainsKey(query.Track))
            return AppResult<BoardData>.Fail("invalid_track", $"Track {query.Track} not found.");

        if (!string.IsNullOrWhiteSpace(query.Milestone) && !config.Milestones.ContainsKey(query.Milestone))
            return AppResult<BoardData>.Fail("invalid_milestone", $"Milestone {query.Milestone} not found.");

        var orderLookup = BuildOrderLookup(projectRoot.ReadTaskOrder());

        var entries = projectRoot.GetAllTasks()
            .Select(task => new BoardTask(
                task,
                projectRoot.ResolveTaskTrack(task),
                task.Milestone,
                projectRoot.TryGetState(task, out var state) ? state : string.Empty,
                GetDescriptionPreview(task.Description, descriptionPreviewLength),
                projectRoot.GetTaskFilePath(task.Id)))
            .Where(entry => string.IsNullOrWhiteSpace(query.Track) || entry.Track == query.Track)
            .Where(entry => string.IsNullOrWhiteSpace(query.Milestone) || entry.Milestone == query.Milestone)
            .Where(entry => string.IsNullOrWhiteSpace(query.State) || entry.State == query.State)
            .OrderBy(entry => GetOrderIndex(entry, orderLookup))
            .ThenByDescending(entry => entry.Task.ModifiedAt)
            .ThenBy(entry => entry.Task.Id, StringComparer.Ordinal)
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
                            .OrderBy(entry => GetOrderIndex(entry, orderLookup))
                            .ThenByDescending(entry => entry.Task.ModifiedAt)
                            .ThenBy(entry => entry.Task.Id, StringComparer.Ordinal)
                            .ToList()))
                    .ToList()))
            .ToList();

        return AppResult<BoardData>.Ok(new BoardData(
            config.Name,
            config.Tracks.Select(track => new BoardOption(track.Key, track.Value)).ToList(),
            config.Milestones.Select(milestone => new BoardOption(milestone.Key, milestone.Value)).ToList(),
            stateOptions,
            entries,
            groups,
            query));
    }

    public static string GetDescriptionPreview(string description, int previewLength)
    {
        var firstLine = description
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => StripMarkdownPrefix(line.Trim()))
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        if (string.IsNullOrWhiteSpace(firstLine)) return string.Empty;

        return firstLine.Length <= previewLength
            ? firstLine
            : $"{firstLine[..(previewLength - 3)]}...";
    }

    private string ResolveMilestoneTitle(string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return "Unassigned";
        return projectRoot.Config!.Milestones.TryGetValue(milestone, out var title) ? title : milestone;
    }

    private static Dictionary<TaskOrderScope, Dictionary<string, int>> BuildOrderLookup(TaskOrderFile order)
    {
        var lookup = new Dictionary<TaskOrderScope, Dictionary<string, int>>();
        foreach (var entry in order.Orders)
        {
            var scope = new TaskOrderScope(entry.Track, entry.State,
                string.IsNullOrWhiteSpace(entry.Milestone) ? null : entry.Milestone.Trim());
            lookup[scope] = entry.TaskIds
                .Select((id, index) => new { Id = id, Index = index })
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);
        }

        return lookup;
    }

    private static int GetOrderIndex(
        BoardTask task,
        Dictionary<TaskOrderScope, Dictionary<string, int>> orderLookup)
    {
        var scope = new TaskOrderScope(task.Track, task.State,
            string.IsNullOrWhiteSpace(task.Milestone) ? null : task.Milestone);
        return orderLookup.TryGetValue(scope, out var orderedIds) &&
               orderedIds.TryGetValue(task.Task.Id, out var index)
            ? index
            : int.MaxValue;
    }

    private static string StripMarkdownPrefix(string line)
    {
        return MarkdownPrefixRegex().Replace(line, string.Empty).Trim();
    }

    [GeneratedRegex(@"^(#{1,6}\s+|(?:[-*+]\s+)?\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+|>\s+)")]
    private static partial Regex MarkdownPrefixRegex();
}
