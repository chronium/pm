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
    BoardQuery Query,
    MilestoneActivationSnapshot MilestoneActivation);

public sealed record BoardOption(string Key, string Name, string Priority = PriorityLevel.None);

public sealed record BoardNavigationData(
    int RemainingCount,
    int ActivationEligibleCount,
    IReadOnlyList<BoardNavigationOption> Tracks,
    IReadOnlyList<BoardMilestoneNavigationOption> Milestones,
    BoardData Board);

public sealed record BoardNavigationOption(
    string Key,
    string Name,
    int RemainingCount,
    int ActivationEligibleCount);

public sealed record BoardMilestoneNavigationOption(
    string Key,
    string Name,
    int RemainingCount,
    int ActivationEligibleCount,
    MilestoneLifecycle Lifecycle,
    IReadOnlyList<string> UnmetActivationTriggers);

public sealed record BoardMilestoneGroup(
    string? Key,
    string Name,
    string Description,
    MilestoneLifecycle? Lifecycle,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers,
    IReadOnlyList<BoardStateGroup> States);

public sealed record BoardStateGroup(string Key, string Name, IReadOnlyList<BoardTask> Tasks);

public sealed record BoardTask(
    TaskItem Task,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string State,
    DependencyStatus Dependencies,
    TaskActivationEligibility Activation,
    string DescriptionPreview,
    string FilePath,
    string? Markdown = null);

public sealed record TaskActivationEligibility(
    bool IsEligible,
    MilestoneLifecycle? MilestoneLifecycle,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers,
    string Summary);

public sealed record DependencyStatus(
    bool Ready,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Completed,
    IReadOnlyList<string> WaitingOn,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unavailable,
    IReadOnlyList<string> Invalid,
    string Summary);

public sealed record NextTaskQuery(string? Track = null, string? Milestone = null, bool ReadyOnly = false);

public sealed record NextTaskResult(bool Found, BoardTask? Task, string Reason);

public partial class BoardService(
    ProjectRoot projectRoot,
    MilestoneActivationResolver activationResolver)
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

        var readContext = ReadContext();
        var orderLookup = BuildOrderLookup(projectRoot.ReadTaskOrder());
        var entries = GetBoardTasks(query, descriptionPreviewLength, orderLookup, readContext);

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
            .Select(milestone =>
            {
                var resolved = string.IsNullOrWhiteSpace(milestone)
                    ? null
                    : readContext.MilestonesByKey.GetValueOrDefault(milestone);
                return new BoardMilestoneGroup(
                    milestone,
                    ResolveMilestoneTitle(milestone),
                    resolved?.Description ?? string.Empty,
                    resolved?.Lifecycle,
                    resolved?.RequiredActivationTriggers ?? [],
                    resolved?.UnmetActivationTriggers ?? [],
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
                        .ToList());
            })
            .ToList();

        return AppResult<BoardData>.Ok(new BoardData(
            config.Name,
            config.Tracks.Select(track => new BoardOption(track.Key, track.Value)).ToList(),
            config.Milestones
                .Select(milestone => new BoardOption(
                    milestone.Key,
                    milestone.Value.Title,
                    PriorityLevel.Resolve(config, milestone.Key)))
                .ToList(),
            stateOptions,
            entries,
            groups,
            query,
            readContext.Activation));
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
        var activationEligible = remaining
            .Where(task => task.Activation.IsEligible)
            .ToList();
        return AppResult<BoardNavigationData>.Ok(new BoardNavigationData(
            remaining.Count,
            activationEligible.Count,
            board.Tracks.Select(track => new BoardNavigationOption(
                track.Key,
                track.Name,
                remaining.Count(task => string.Equals(task.Track, track.Key, StringComparison.Ordinal)),
                activationEligible.Count(task => string.Equals(task.Track, track.Key, StringComparison.Ordinal))))
                .ToList(),
            board.Milestones.Select(milestone =>
            {
                var resolved = board.MilestoneActivation.Milestones.Single(item =>
                    string.Equals(item.Key, milestone.Key, StringComparison.Ordinal));
                return new BoardMilestoneNavigationOption(
                    milestone.Key,
                    milestone.Name,
                    remaining.Count(task => string.Equals(task.Milestone, milestone.Key, StringComparison.Ordinal)),
                    activationEligible.Count(task =>
                        string.Equals(task.Milestone, milestone.Key, StringComparison.Ordinal)),
                    resolved.Lifecycle,
                    resolved.UnmetActivationTriggers);
            })
                .ToList(),
            board));
    }

    public AppResult<BoardTask> GetTask(string taskId, int descriptionPreviewLength = WebDescriptionPreviewLength)
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<BoardTask>.Fail("missing_project", "Project not found. Run pm init first.");

        var readContext = ReadContext();
        var task = readContext.TasksById.GetValueOrDefault(taskId);
        if (task == null)
            return AppResult<BoardTask>.Fail("missing_task", $"Task with ID {taskId} not found.");
        if (!readContext.StateById.TryGetValue(task.Id, out var state) || string.IsNullOrWhiteSpace(state))
            return AppResult<BoardTask>.Fail("missing_current_state", $"Task with ID {taskId} has no associated state.");

        var priority = PriorityLevel.Resolve(projectRoot.Config, task);
        return AppResult<BoardTask>.Ok(new BoardTask(
            task,
            projectRoot.ResolveTaskTrack(task),
            task.Milestone,
            priority.Priority,
            priority.Source,
            state,
            BuildDependencyStatus(task, readContext.TasksById, readContext.StateById, GetActiveProjectId()),
            ResolveActivationEligibility(task, readContext.MilestonesByKey),
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
        if (!string.IsNullOrWhiteSpace(query.Milestone) && !config.Milestones.ContainsKey(query.Milestone))
            return AppResult<NextTaskResult>.Fail("invalid_milestone", $"Milestone {query.Milestone} not found.");

        var orderLookup = BuildOrderLookup(projectRoot.ReadTaskOrder());
        var stateIndex = BuildStateIndex(config);
        var milestoneIndex = BuildMilestoneIndex(config);
        var readContext = ReadContext();
        var actionable = GetBoardTasks(
                new BoardQuery(query.Track, query.Milestone), descriptionPreviewLength, orderLookup, readContext)
            .Where(task => !string.Equals(task.State, "done", StringComparison.Ordinal))
            .ToList();
        var activationEligible = actionable
            .Where(task => task.Activation.IsEligible)
            .ToList();
        var selected = activationEligible
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
            BuildNoNextTaskReason(query, readContext.MilestonesByKey, actionable, activationEligible)));
    }

    public DependencyStatus GetDependencyStatus(TaskItem task)
    {
        var tasksById = BuildTaskLookup(projectRoot.GetAllTasks());
        var stateById = tasksById.Values
            .ToDictionary(
                item => item.Id,
                item => projectRoot.TryGetState(item, out var state) ? state : string.Empty,
                StringComparer.Ordinal);

        return BuildDependencyStatus(task, tasksById, stateById, GetActiveProjectId());
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
        return projectRoot.Config!.Milestones.TryGetValue(milestone, out var definition)
            ? definition.Title
            : milestone;
    }

    private List<BoardTask> GetBoardTasks(
        BoardQuery query,
        int descriptionPreviewLength,
        Dictionary<TaskOrderScope, Dictionary<string, int>> orderLookup,
        BoardReadContext readContext)
    {
        return readContext.TasksById.Values
            .Select(task =>
            {
                var priority = PriorityLevel.Resolve(projectRoot.Config!, task);
                var state = readContext.StateById.TryGetValue(task.Id, out var currentState)
                    ? currentState
                    : string.Empty;
                return new BoardTask(
                    task,
                    projectRoot.ResolveTaskTrack(task),
                    task.Milestone,
                    priority.Priority,
                    priority.Source,
                    state,
                    BuildDependencyStatus(
                        task, readContext.TasksById, readContext.StateById, GetActiveProjectId()),
                    ResolveActivationEligibility(task, readContext.MilestonesByKey),
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
        IReadOnlyDictionary<string, string> stateById,
        string? activeProjectId = null)
    {
        var dependencies = task.DependencyIds;
        if (dependencies.Count == 0)
            return new DependencyStatus(true, [], [], [], [], [], [], "no dependencies");

        var completed = new List<string>();
        var waitingOn = new List<string>();
        var missing = new List<string>();
        var unavailable = new List<string>();
        var invalid = new List<string>();
        foreach (var dependencyValue in dependencies)
        {
            if (!TaskDependencyReference.TryParse(dependencyValue, out var dependency, out _))
            {
                invalid.Add(dependencyValue);
                continue;
            }

            if (!dependency!.IsLocalTo(activeProjectId))
            {
                unavailable.Add(dependencyValue);
                continue;
            }

            var dependencyId = dependency.TaskId;
            if (!tasksById.ContainsKey(dependencyId))
            {
                missing.Add(dependencyValue);
                continue;
            }

            if (!stateById.TryGetValue(dependencyId, out var state) ||
                !string.Equals(state, "done", StringComparison.Ordinal))
                waitingOn.Add(dependencyValue);
            else
                completed.Add(dependencyValue);
        }

        var ready = waitingOn.Count == 0 && missing.Count == 0 &&
                    unavailable.Count == 0 && invalid.Count == 0;
        var summary = ready
            ? "all dependencies complete"
            : BuildWaitingSummary(waitingOn, missing, unavailable, invalid);

        return new DependencyStatus(
            ready, dependencies.ToList(), completed, waitingOn, missing, unavailable, invalid, summary);
    }

    private string? GetActiveProjectId() =>
        projectRoot.TryReadProjectId(out var projectId) ? projectId : null;

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
        return $"Selected {task.Priority} priority task from {source} in state {task.State}, {milestone}; " +
               $"{task.Dependencies.Summary}.{BuildActivationSelectionContext(task)}";
    }

    internal static string BuildActivationSelectionContext(BoardTask task) =>
        string.IsNullOrWhiteSpace(task.Milestone) ? string.Empty : $" {task.Activation.Summary}";

    private static string BuildNoNextTaskReason(
        NextTaskQuery query,
        IReadOnlyDictionary<string, ResolvedMilestone> milestonesByKey,
        IReadOnlyList<BoardTask> actionable,
        IReadOnlyList<BoardTask> activationEligible)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Track)) filters.Add($"track {query.Track}");
        if (!string.IsNullOrWhiteSpace(query.Milestone)) filters.Add($"milestone {query.Milestone}");
        var scope = filters.Count == 0 ? string.Empty : $" for {string.Join(" and ", filters)}";

        if (!string.IsNullOrWhiteSpace(query.Milestone) &&
            milestonesByKey.TryGetValue(query.Milestone, out var milestone))
        {
            if (milestone.Lifecycle == MilestoneLifecycle.Inactive)
                return $"No activation-eligible task found{scope}; milestone {query.Milestone} is inactive; " +
                       $"unmet activation triggers: {string.Join(", ", milestone.UnmetActivationTriggers)}.";
            if (milestone.Lifecycle == MilestoneLifecycle.Delivered)
                return $"No activation-eligible task found{scope}; milestone {query.Milestone} is delivered.";
        }

        var activationExcludedCount = actionable.Count - activationEligible.Count;
        if (activationEligible.Count == 0 && activationExcludedCount > 0)
        {
            var noun = activationExcludedCount == 1 ? "task is" : "tasks are";
            return $"No activation-eligible task found{scope}; {activationExcludedCount} remaining {noun} " +
                   "excluded by inactive or delivered milestones.";
        }

        return query.ReadyOnly
            ? $"No dependency-ready actionable task found{scope}."
            : $"No actionable task found{scope}.";
    }

    internal static string BuildWaitingSummary(
        IReadOnlyList<string> waitingOn,
        IReadOnlyList<string> missing,
        IReadOnlyList<string> unavailable,
        IReadOnlyList<string> invalid)
    {
        var parts = new List<string>();
        if (waitingOn.Count > 0)
            parts.Add($"waiting on {string.Join(", ", waitingOn)}");
        if (missing.Count > 0)
            parts.Add($"missing {string.Join(", ", missing)}");
        if (unavailable.Count > 0)
            parts.Add($"unavailable {string.Join(", ", unavailable)}");
        if (invalid.Count > 0)
            parts.Add($"invalid {string.Join(", ", invalid)}");

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

    private BoardReadContext ReadContext()
    {
        var tasksById = BuildTaskLookup(projectRoot.GetAllTasks());
        var stateById = tasksById.Values.ToDictionary(
            task => task.Id,
            task => projectRoot.TryGetState(task, out var state) ? state : string.Empty,
            StringComparer.Ordinal);
        var activation = activationResolver.Resolve(projectRoot.Config!, tasksById, stateById);
        return new BoardReadContext(
            tasksById,
            stateById,
            activation,
            activation.Milestones.ToDictionary(milestone => milestone.Key, StringComparer.Ordinal));
    }

    private static TaskActivationEligibility ResolveActivationEligibility(
        TaskItem task,
        IReadOnlyDictionary<string, ResolvedMilestone> milestonesByKey)
    {
        if (string.IsNullOrWhiteSpace(task.Milestone))
            return new TaskActivationEligibility(
                true, null, [], [], "Eligible: task is not gated by a milestone.");

        if (!milestonesByKey.TryGetValue(task.Milestone, out var milestone))
            return new TaskActivationEligibility(
                false, null, [], [], $"Ineligible: milestone {task.Milestone} is not configured.");

        var isEligible = milestone.Lifecycle is MilestoneLifecycle.Active or MilestoneLifecycle.ReadyToDeliver;
        var summary = milestone.Lifecycle switch
        {
            MilestoneLifecycle.Active => $"Eligible: milestone {task.Milestone} is active.",
            MilestoneLifecycle.ReadyToDeliver => $"Eligible: milestone {task.Milestone} is ready to deliver.",
            MilestoneLifecycle.Inactive =>
                $"Ineligible: milestone {task.Milestone} is inactive; unmet activation triggers: " +
                $"{string.Join(", ", milestone.UnmetActivationTriggers)}.",
            MilestoneLifecycle.Delivered => $"Ineligible: milestone {task.Milestone} is delivered.",
            _ => $"Ineligible: milestone {task.Milestone} has an unknown lifecycle.",
        };
        return new TaskActivationEligibility(
            isEligible,
            milestone.Lifecycle,
            milestone.RequiredActivationTriggers,
            milestone.UnmetActivationTriggers,
            summary);
    }

    private sealed record BoardReadContext(
        IReadOnlyDictionary<string, TaskItem> TasksById,
        IReadOnlyDictionary<string, string> StateById,
        MilestoneActivationSnapshot Activation,
        IReadOnlyDictionary<string, ResolvedMilestone> MilestonesByKey);

    private static string StripMarkdownPrefix(string line)
    {
        return MarkdownPrefixRegex().Replace(line, string.Empty).Trim();
    }

    [GeneratedRegex(@"^(#{1,6}\s+|(?:[-*+]\s+)?\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+|>\s+)")]
    private static partial Regex MarkdownPrefixRegex();
}
