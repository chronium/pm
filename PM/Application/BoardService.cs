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

public sealed record BoardOption(string Key, string Name, string Priority = PriorityLevel.None);

public sealed record BoardNavigationData(
    int RemainingCount,
    IReadOnlyList<BoardNavigationOption> Tracks,
    IReadOnlyList<BoardNavigationOption> Milestones,
    BoardData Board);

public sealed record BoardNavigationOption(string Key, string Name, int RemainingCount);

public sealed record BoardMilestoneGroup(string? Key, string Name, IReadOnlyList<BoardStateGroup> States);

public sealed record BoardStateGroup(string Key, string Name, IReadOnlyList<BoardTask> Tasks);

public sealed record BoardTask(
    TaskItem Task,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string State,
    DependencyStatus Dependencies,
    string DescriptionPreview,
    string FilePath);

public sealed record DependencyStatus(
    bool Ready,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> WaitingOn,
    IReadOnlyList<string> Missing,
    string Summary);

public sealed record NextTaskQuery(string? Track = null, bool ReadyOnly = false);

public sealed record NextTaskResult(bool Found, BoardTask? Task, string Reason);

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

        var entries = GetBoardTasks(query, descriptionPreviewLength, orderLookup);

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
            config.Milestones
                .Select(milestone => new BoardOption(
                    milestone.Key,
                    milestone.Value,
                    PriorityLevel.Resolve(config, milestone.Key)))
                .ToList(),
            stateOptions,
            entries,
            groups,
            query));
    }

    public AppResult<BoardNavigationData> GetNavigation()
    {
        var boardResult = GetBoard(new BoardQuery());
        if (!boardResult.Success)
            return AppResult<BoardNavigationData>.Fail(boardResult.ErrorCode!, boardResult.Message!);

        var board = boardResult.Payload!;
        var remaining = board.Tasks
            .Where(task => !string.Equals(task.State, "done", StringComparison.Ordinal))
            .ToList();
        return AppResult<BoardNavigationData>.Ok(new BoardNavigationData(
            remaining.Count,
            board.Tracks.Select(track => new BoardNavigationOption(
                track.Key,
                track.Name,
                remaining.Count(task => string.Equals(task.Track, track.Key, StringComparison.Ordinal))))
                .ToList(),
            board.Milestones.Select(milestone => new BoardNavigationOption(
                milestone.Key,
                milestone.Name,
                remaining.Count(task => string.Equals(task.Milestone, milestone.Key, StringComparison.Ordinal))))
                .ToList(),
            board));
    }

    public AppResult<BoardTask> GetTask(string taskId, int descriptionPreviewLength = WebDescriptionPreviewLength)
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<BoardTask>.Fail("missing_project", "Project not found. Run pm init first.");

        var tasks = projectRoot.GetAllTasks();
        var task = tasks
            .FirstOrDefault(item => string.Equals(item.Id, taskId, StringComparison.Ordinal));
        if (task == null)
            return AppResult<BoardTask>.Fail("missing_task", $"Task with ID {taskId} not found.");
        if (!projectRoot.TryGetState(task, out var state))
            return AppResult<BoardTask>.Fail("missing_current_state", $"Task with ID {taskId} has no associated state.");

        var tasksById = BuildTaskLookup(tasks);
        var stateById = tasksById.Values.ToDictionary(
            item => item.Id,
            item => projectRoot.TryGetState(item, out var currentState) ? currentState : string.Empty,
            StringComparer.Ordinal);
        var priority = PriorityLevel.Resolve(projectRoot.Config, task);
        return AppResult<BoardTask>.Ok(new BoardTask(
            task,
            projectRoot.ResolveTaskTrack(task),
            task.Milestone,
            priority.Priority,
            priority.Source,
            state,
            BuildDependencyStatus(task, tasksById, stateById),
            GetDescriptionPreview(task.Description, descriptionPreviewLength),
            projectRoot.GetTaskFilePath(task.Id)));
    }

    public AppResult<NextTaskResult> GetNextTask(
        NextTaskQuery query,
        int descriptionPreviewLength = CliDescriptionPreviewLength)
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<NextTaskResult>.Fail("missing_project", "Project not found. Run pm init first.");

        var config = projectRoot.Config;
        if (!string.IsNullOrWhiteSpace(query.Track) && !config.Tracks.ContainsKey(query.Track))
            return AppResult<NextTaskResult>.Fail("invalid_track", $"Track {query.Track} not found.");

        var orderLookup = BuildOrderLookup(projectRoot.ReadTaskOrder());
        var stateIndex = BuildStateIndex(config);
        var milestoneIndex = BuildMilestoneIndex(config);
        var selected = GetBoardTasks(new BoardQuery(query.Track), descriptionPreviewLength, orderLookup)
            .Where(task => !string.Equals(task.State, "done", StringComparison.Ordinal))
            .Where(task => !query.ReadyOnly || task.Dependencies.Ready)
            .OrderBy(task => task.Dependencies.Ready ? 0 : 1)
            .ThenByDescending(task => PriorityLevel.Rank(task.Priority))
            .ThenBy(task => GetStateIndex(task, stateIndex))
            .ThenBy(task => GetMilestoneIndex(task, milestoneIndex))
            .ThenBy(task => GetOrderIndex(task, orderLookup))
            .ThenByDescending(task => task.Task.ModifiedAt)
            .ThenBy(task => task.Task.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (selected != null)
            return AppResult<NextTaskResult>.Ok(new NextTaskResult(
                true,
                selected,
                BuildNextTaskReason(selected)));

        return AppResult<NextTaskResult>.Ok(new NextTaskResult(
            false,
            null,
            BuildNoNextTaskReason(query)));
    }

    public DependencyStatus GetDependencyStatus(TaskItem task)
    {
        var tasksById = BuildTaskLookup(projectRoot.GetAllTasks());
        var stateById = tasksById.Values
            .ToDictionary(
                item => item.Id,
                item => projectRoot.TryGetState(item, out var state) ? state : string.Empty,
                StringComparer.Ordinal);

        return BuildDependencyStatus(task, tasksById, stateById);
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

    private List<BoardTask> GetBoardTasks(
        BoardQuery query,
        int descriptionPreviewLength,
        Dictionary<TaskOrderScope, Dictionary<string, int>> orderLookup)
    {
        var tasksById = BuildTaskLookup(projectRoot.GetAllTasks());
        var stateById = tasksById.Values
            .ToDictionary(
                task => task.Id,
                task => projectRoot.TryGetState(task, out var state) ? state : string.Empty,
                StringComparer.Ordinal);

        return tasksById.Values
            .Select(task =>
            {
                var priority = PriorityLevel.Resolve(projectRoot.Config!, task);
                var state = stateById.TryGetValue(task.Id, out var currentState) ? currentState : string.Empty;
                return new BoardTask(
                    task,
                    projectRoot.ResolveTaskTrack(task),
                    task.Milestone,
                    priority.Priority,
                    priority.Source,
                    state,
                    BuildDependencyStatus(task, tasksById, stateById),
                    GetDescriptionPreview(task.Description, descriptionPreviewLength),
                    projectRoot.GetTaskFilePath(task.Id));
            })
            .Where(entry => string.IsNullOrWhiteSpace(query.Track) || entry.Track == query.Track)
            .Where(entry => string.IsNullOrWhiteSpace(query.Milestone) || entry.Milestone == query.Milestone)
            .Where(entry => string.IsNullOrWhiteSpace(query.State) || entry.State == query.State)
            .OrderBy(entry => GetOrderIndex(entry, orderLookup))
            .ThenByDescending(entry => entry.Task.ModifiedAt)
            .ThenBy(entry => entry.Task.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static DependencyStatus BuildDependencyStatus(
        TaskItem task,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateById)
    {
        var dependencies = task.DependencyIds;
        if (dependencies.Count == 0)
            return new DependencyStatus(true, [], [], [], "no dependencies");

        var waitingOn = new List<string>();
        var missing = new List<string>();
        foreach (var dependencyId in dependencies)
        {
            if (!tasksById.ContainsKey(dependencyId))
            {
                missing.Add(dependencyId);
                continue;
            }

            if (!stateById.TryGetValue(dependencyId, out var state) ||
                !string.Equals(state, "done", StringComparison.Ordinal))
                waitingOn.Add(dependencyId);
        }

        var ready = waitingOn.Count == 0 && missing.Count == 0;
        var summary = ready
            ? "all dependencies complete"
            : BuildWaitingSummary(waitingOn, missing);

        return new DependencyStatus(ready, dependencies.ToList(), waitingOn, missing, summary);
    }

    private static Dictionary<string, TaskItem> BuildTaskLookup(IEnumerable<TaskItem> tasks)
    {
        return tasks
            .GroupBy(task => task.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private static string BuildNextTaskReason(BoardTask task)
    {
        var milestone = string.IsNullOrWhiteSpace(task.Milestone)
            ? "unassigned milestone"
            : $"milestone {task.Milestone}";
        var source = task.PrioritySource switch
        {
            PriorityLevel.SourceTask => "task override",
            PriorityLevel.SourceMilestone => "milestone default",
            _ => "no priority source",
        };
        return $"Selected {task.Priority} priority task from {source} in state {task.State}, {milestone}; {task.Dependencies.Summary}.";
    }

    private static string BuildNoNextTaskReason(NextTaskQuery query)
    {
        var scope = string.IsNullOrWhiteSpace(query.Track)
            ? string.Empty
            : $" for track {query.Track}";

        return query.ReadyOnly
            ? $"No dependency-ready actionable task found{scope}."
            : $"No actionable task found{scope}.";
    }

    private static string BuildWaitingSummary(IReadOnlyList<string> waitingOn, IReadOnlyList<string> missing)
    {
        var parts = new List<string>();
        if (waitingOn.Count > 0)
            parts.Add($"waiting on {string.Join(", ", waitingOn)}");
        if (missing.Count > 0)
            parts.Add($"missing {string.Join(", ", missing)}");

        return string.Join("; ", parts);
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

    private static Dictionary<string, int> BuildStateIndex(ProjectConfig config)
    {
        return config.TaskStates.Keys
            .Select((state, index) => new { State = state, Index = index })
            .ToDictionary(item => item.State, item => item.Index, StringComparer.Ordinal);
    }

    private static Dictionary<string, int> BuildMilestoneIndex(ProjectConfig config)
    {
        return config.Milestones.Keys
            .Select((milestone, index) => new { Milestone = milestone, Index = index })
            .ToDictionary(item => item.Milestone, item => item.Index, StringComparer.Ordinal);
    }

    private static int GetStateIndex(BoardTask task, Dictionary<string, int> stateIndex)
    {
        return stateIndex.TryGetValue(task.State, out var index) ? index : int.MaxValue;
    }

    private static int GetMilestoneIndex(BoardTask task, Dictionary<string, int> milestoneIndex)
    {
        if (string.IsNullOrWhiteSpace(task.Milestone))
            return milestoneIndex.Count;

        return milestoneIndex.TryGetValue(task.Milestone, out var index)
            ? index
            : milestoneIndex.Count + 1;
    }

    private static string StripMarkdownPrefix(string line)
    {
        return MarkdownPrefixRegex().Replace(line, string.Empty).Trim();
    }

    [GeneratedRegex(@"^(#{1,6}\s+|(?:[-*+]\s+)?\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+|>\s+)")]
    private static partial Regex MarkdownPrefixRegex();
}
