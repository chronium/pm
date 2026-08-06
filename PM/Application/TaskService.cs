using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed record BulkTaskCreateInput(string Title, string? Description = null);
public sealed record TaskPlacementUpdate(string Track, string? Milestone);

public sealed record BulkCreatedTask(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string FilePath);

public sealed record BulkTaskOperationFailure(string ErrorCode, string Message);

public sealed record BulkCreateTasksResult(
    string Track,
    IReadOnlyList<BulkCreatedTask> Tasks,
    int RequestedCount,
    int CreatedCount,
    BulkTaskOperationFailure? Failure);

public sealed record BulkMilestoneAssignmentResult(
    string Milestone,
    IReadOnlyList<string> TaskIds,
    IReadOnlyList<string> FilePaths,
    int RequestedCount,
    int UpdatedCount);

public sealed record TaskMutationResult(bool Changed, TaskItem Task);

public sealed record TaskReorderResult(
    string Track,
    string State,
    string? Milestone,
    IReadOnlyList<string> TaskIds,
    bool Changed);

public sealed record TaskSearchResult(
    TaskItem Task,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string State,
    DependencyStatus Dependencies,
    string DescriptionPreview,
    string FilePath,
    int MatchCount,
    string Snippet);

public sealed class TaskService
{
    private readonly ProjectRoot projectRoot;
    private readonly INextIdService nextIdService;
    private readonly MilestoneActivationGraphService activationGraph;

    public TaskService(ProjectRoot projectRoot, INextIdService nextIdService)
        : this(projectRoot, nextIdService, new MilestoneActivationGraphService())
    {
    }

    public TaskService(
        ProjectRoot projectRoot,
        INextIdService nextIdService,
        MilestoneActivationGraphService activationGraph)
    {
        this.projectRoot = projectRoot;
        this.nextIdService = nextIdService;
        this.activationGraph = activationGraph;
    }

    public ProjectRoot ProjectRoot => projectRoot;
    public INextIdService NextIdService => nextIdService;

    private const int MaxBulkTaskCount = 100;

    public async Task<AppResult<TaskItem>> CreateTask(
        string title,
        string track,
        string? milestone,
        string description,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (!projectRoot.Exists)
            return AppResult<TaskItem>.Fail("missing_project", "Project not found. Run pm init first.");

        var config = projectRoot.Config!;
        track = track.Trim();
        if (!config.Tracks.ContainsKey(track))
            return AppResult<TaskItem>.Fail("invalid_track", $"Track {track} not found.");

        milestone = string.IsNullOrWhiteSpace(milestone) ? null : milestone.Trim();
        if (milestone != null && !config.Milestones.ContainsKey(milestone))
            return AppResult<TaskItem>.Fail("invalid_milestone", $"Milestone {milestone} not found.");

        string idPadded;
        try
        {
            int? nextId;
            if (dryRun)
            {
                nextId = await nextIdService.PeekExistingNextId(projectRoot, track, cancellationToken);
            }
            else
            {
                if (!await nextIdService.Healthy(config, cancellationToken))
                    return AppResult<TaskItem>.Fail("next_id_unavailable", "Unable to reach the next ID service.");

                nextId = await nextIdService.GetNextId(projectRoot, track, cancellationToken);
            }

            idPadded = nextId?.ToString().PadLeft(config.IdWidth, '0') ?? new string('?', config.IdWidth);
        }
        catch
        {
            return AppResult<TaskItem>.Fail("next_id_unavailable", "Unable to allocate the next task ID.");
        }

        var task = BuildTask(title, track, milestone, description, idPadded);
        if (!dryRun)
        {
            try
            {
                projectRoot.WriteTask(task);
                projectRoot.UpdateTaskState(task, config.TaskStates.Keys.First());
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                TryDeletePartialTask(task);
                return AppResult<TaskItem>.Fail("task_storage_write_failed",
                    $"Task {task.Id} could not be stored. Its allocated ID will not be reused.");
            }
        }

        return AppResult<TaskItem>.Ok(task);
    }

    public async Task<AppResult<BulkCreateTasksResult>> BulkCreateTasksForTrack(
        string track,
        IReadOnlyList<BulkTaskCreateInput> tasks,
        CancellationToken cancellationToken = default)
    {
        if (!projectRoot.Exists)
            return AppResult<BulkCreateTasksResult>.Fail("missing_project", "Project not found. Run pm init first.");

        track = track.Trim();
        var config = projectRoot.Config!;
        if (!config.Tracks.ContainsKey(track))
            return AppResult<BulkCreateTasksResult>.Fail("invalid_track", $"Track {track} not found.");

        if (tasks.Count is < 1 or > MaxBulkTaskCount)
            return AppResult<BulkCreateTasksResult>.Fail("invalid_batch_size", "Bulk task creation requires 1 to 100 tasks.");

        if (tasks.Any(task => string.IsNullOrWhiteSpace(task.Title)))
            return AppResult<BulkCreateTasksResult>.Fail("invalid_title", "All task titles are required.");

        var createdTasks = new List<BulkCreatedTask>();
        foreach (var task in tasks)
        {
            var result = await CreateTask(task.Title, track, null, task.Description ?? string.Empty, false,
                cancellationToken);
            if (!result.Success)
            {
                return AppResult<BulkCreateTasksResult>.Ok(new BulkCreateTasksResult(
                    track,
                    createdTasks,
                    tasks.Count,
                    createdTasks.Count,
                    new BulkTaskOperationFailure(
                        result.ErrorCode ?? "unknown_error",
                        result.Message ?? "Task creation stopped.")));
            }

            var created = result.Payload!;
            createdTasks.Add(new BulkCreatedTask(
                created.Id,
                created.Title,
                projectRoot.ResolveTaskTrack(created),
                created.Milestone,
                projectRoot.GetTaskFilePath(created.Id)));
        }

        return AppResult<BulkCreateTasksResult>.Ok(new BulkCreateTasksResult(
            track,
            createdTasks,
            tasks.Count,
            createdTasks.Count,
            null));
    }

    public AppResult<IReadOnlyList<TaskSearchResult>> SearchTasks(
        string query,
        int limit = 20,
        TaskSearchContext? context = null)
    {
        if (!projectRoot.Exists)
            return AppResult<IReadOnlyList<TaskSearchResult>>.Fail("missing_project", "Project not found. Run pm init first.");

        var parsedQuery = TaskSearchQueryParser.Parse(query);
        if (!parsedQuery.Success)
            return AppResult<IReadOnlyList<TaskSearchResult>>.Fail(parsedQuery.ErrorCode!, parsedQuery.Message!);

        context ??= new TaskSearchContext();
        var normalizedContext = new TaskSearchContext(
            NormalizeFilter(context.Track), NormalizeFilter(context.Milestone), NormalizeFilter(context.State));
        var contextError = ValidateSearchContext(normalizedContext, parsedQuery.Payload!.Scope);
        if (contextError != null) return contextError;

        var search = parsedQuery.Payload!;
        limit = Math.Clamp(limit, 1, 100);
        var parsedTasks = new List<(TaskItem Task, string FilePath, string Markdown)>();
        foreach (var (filePath, markdown) in projectRoot.GetTaskMarkdownFiles())
        {
            if (!TaskItem.TryParse(markdown, out var task, out _, out _) || task == null)
                return AppResult<IReadOnlyList<TaskSearchResult>>.Fail("invalid_task_markdown",
                    $"Task file {filePath} markdown is invalid.");

            parsedTasks.Add((task, filePath, markdown));
        }

        var tasksById = parsedTasks
            .Select(entry => entry.Task)
            .GroupBy(task => task.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var stateById = tasksById.Values
            .ToDictionary(
                task => task.Id,
                task => projectRoot.TryGetState(task, out var state) ? state : string.Empty,
                StringComparer.Ordinal);

        var results = new List<TaskSearchResult>();
        foreach (var (task, filePath, markdown) in parsedTasks)
        {
            var track = projectRoot.ResolveTaskTrack(task);
            var state = stateById.TryGetValue(task.Id, out var currentState) ? currentState : string.Empty;
            if (!MatchesFilters(task, track, state, search, normalizedContext)) continue;
            var priority = PriorityLevel.Resolve(projectRoot.Config!, task);
            var fields = BuildSearchFields(task, markdown, track, state, priority.Priority);
            var matchCount = search.HasFreeText
                ? CountSearchMatches(fields, search.FreeText)
                : 0;
            if (search.HasFreeText && matchCount == 0) continue;

            var descriptionPreview = BoardService.GetDescriptionPreview(
                task.Description, BoardService.WebDescriptionPreviewLength);

            results.Add(new TaskSearchResult(
                task,
                track,
                task.Milestone,
                priority.Priority,
                priority.Source,
                state,
                BoardService.BuildDependencyStatus(task, tasksById, stateById, GetActiveProjectId()),
                descriptionPreview,
                filePath,
                matchCount,
                search.HasFreeText ? BuildSnippet(fields, search.FreeText) : descriptionPreview));
        }

        var ordered = search.HasFreeText
            ? results.OrderByDescending(result => result.MatchCount)
                .ThenBy(result => result.Task.Id, StringComparer.Ordinal)
            : results.OrderBy(result => result.Task.Id, StringComparer.Ordinal);
        return AppResult<IReadOnlyList<TaskSearchResult>>.Ok(ordered
            .Take(limit)
            .ToList());
    }

    private AppResult<IReadOnlyList<TaskSearchResult>>? ValidateSearchContext(TaskSearchContext context,
        TaskSearchScope scope)
    {
        var config = projectRoot.Config!;
        if (scope == TaskSearchScope.Selection && context.Track != null && !config.Tracks.ContainsKey(context.Track))
            return AppResult<IReadOnlyList<TaskSearchResult>>.Fail("invalid_track", $"Track {context.Track} not found.");
        if (scope == TaskSearchScope.Selection && context.Milestone != null && !config.Milestones.ContainsKey(context.Milestone))
            return AppResult<IReadOnlyList<TaskSearchResult>>.Fail("invalid_milestone", $"Milestone {context.Milestone} not found.");
        if (context.State != null && !config.TaskStates.ContainsKey(context.State))
            return AppResult<IReadOnlyList<TaskSearchResult>>.Fail("invalid_state", $"State {context.State} not found.");
        return null;
    }

    private static bool MatchesFilters(TaskItem task, string track, string state, TaskSearchQuery query,
        TaskSearchContext context)
    {
        return MatchesAny(query.States, state) &&
               MatchesAny(query.Tracks, track) &&
               MatchesAny(query.Milestones, task.Milestone ?? string.Empty) &&
               MatchesAnyTaskId(query.Ids, task.Id) &&
               MatchesContext(context.State, state) &&
               (query.Scope == TaskSearchScope.All ||
                MatchesContext(context.Track, track) &&
                MatchesContext(context.Milestone, task.Milestone ?? string.Empty));
    }

    private static bool MatchesAny(IReadOnlyList<string> values, string actual) =>
        values.Count == 0 || values.Any(value => actual.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesAnyTaskId(IReadOnlyList<string> values, string actual) =>
        values.Count == 0 || values.Any(value => MatchesTaskId(value, actual));

    private static bool MatchesTaskId(string value, string actual)
    {
        if (!value.All(char.IsDigit))
            return actual.StartsWith(value, StringComparison.OrdinalIgnoreCase);

        var suffixStart = actual.Length;
        while (suffixStart > 0 && char.IsDigit(actual[suffixStart - 1])) suffixStart--;
        if (suffixStart == actual.Length) return false;

        return NormalizeTaskNumber(actual[suffixStart..]) == NormalizeTaskNumber(value);
    }

    private static string NormalizeTaskNumber(string value)
    {
        var normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static bool MatchesContext(string? expected, string actual) =>
        expected == null || actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public AppResult MoveTask(string taskId, string targetState)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.Config!.TaskStates.ContainsKey(targetState))
            return AppResult.Fail("invalid_state", $"State {targetState} not found.");

        if (!projectRoot.TryGetById(taskId, out var task))
            return AppResult.Fail("missing_task", $"Task with ID {taskId} not found.");

        if (!projectRoot.TryGetState(task, out _))
            return AppResult.Fail("missing_current_state", $"Task with ID {taskId} has no associated state.");

        try
        {
            projectRoot.UpdateTaskState(task, targetState);
            return AppResult.Ok();
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult.Fail("task_state_write_failed", $"Task {taskId} could not be moved to {targetState}.");
        }
    }

    public AppResult RemoveTask(string taskId)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryGetById(taskId, out var task))
            return AppResult.Fail("missing_task", $"Task with ID {taskId} not found.");

        var requiringTriggers = projectRoot.Config!.ActivationTriggers
            .Where(trigger => (trigger.Value.Requirements ?? []).Any(requirement =>
                requirement.Kind == ActivationRequirementKind.Task &&
                string.Equals(requirement.Source, task.Id, StringComparison.Ordinal)))
            .Select(trigger => trigger.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (requiringTriggers.Count > 0)
            return AppResult.Fail(
                "activation_requirement_in_use",
                $"Task {task.Id} is required by activation trigger(s): {string.Join(", ", requiringTriggers)}.");

        projectRoot.DeleteTask(task);
        return AppResult.Ok();
    }

    public AppResult<BulkMilestoneAssignmentResult> BulkAssignTasksToMilestone(
        string milestone,
        IReadOnlyList<string> taskIds)
    {
        if (!projectRoot.Exists)
            return AppResult<BulkMilestoneAssignmentResult>.Fail("missing_project", "Project not found. Run pm init first.");

        milestone = milestone.Trim();
        var config = projectRoot.Config!;
        if (!config.Milestones.ContainsKey(milestone))
            return AppResult<BulkMilestoneAssignmentResult>.Fail("missing_milestone", $"Milestone {milestone} not found.");

        if (taskIds.Count is < 1 or > MaxBulkTaskCount)
            return AppResult<BulkMilestoneAssignmentResult>.Fail("invalid_batch_size",
                "Bulk milestone assignment requires 1 to 100 task IDs.");

        var normalizedIds = taskIds.Select(id => id.Trim()).ToList();
        if (normalizedIds.Any(string.IsNullOrWhiteSpace))
            return AppResult<BulkMilestoneAssignmentResult>.Fail("invalid_task_id", "All task IDs are required.");

        if (normalizedIds.Distinct(StringComparer.Ordinal).Count() != normalizedIds.Count)
            return AppResult<BulkMilestoneAssignmentResult>.Fail("duplicate_task_id",
                "Bulk milestone assignment cannot include duplicate task IDs.");

        var tasks = new List<TaskItem>();
        foreach (var taskId in normalizedIds)
        {
            if (!projectRoot.TryGetById(taskId, out var task))
                return AppResult<BulkMilestoneAssignmentResult>.Fail("missing_task", $"Task with ID {taskId} not found.");

            tasks.Add(task);
        }

        var prospectiveTasks = tasks
            .Where(task => !string.Equals(task.Milestone, milestone, StringComparison.Ordinal))
            .Select(task => task with { Milestone = milestone })
            .ToList();
        if (prospectiveTasks.Count > 0)
        {
            var preflight = PreflightMilestonePlacements(prospectiveTasks);
            if (!preflight.Success)
                return AppResult<BulkMilestoneAssignmentResult>.Fail(preflight.ErrorCode!, preflight.Message!);
        }

        var changed = 0;
        foreach (var task in tasks)
        {
            if (string.Equals(task.Milestone, milestone, StringComparison.Ordinal))
                continue;

            if (projectRoot.TryGetState(task, out var state))
                projectRoot.MoveTaskOrderScope(task.Id,
                    new TaskOrderScope(projectRoot.ResolveTaskTrack(task), state, task.Milestone),
                    new TaskOrderScope(projectRoot.ResolveTaskTrack(task), state, milestone));

            projectRoot.WriteTask(task with { Milestone = milestone, ModifiedAt = DateTime.UtcNow });
            changed++;
        }

        return AppResult<BulkMilestoneAssignmentResult>.Ok(new BulkMilestoneAssignmentResult(
            milestone,
            normalizedIds,
            normalizedIds.Select(projectRoot.GetTaskFilePath).ToList(),
            normalizedIds.Count,
            changed));
    }

    public AppResult<string> ReadTaskMarkdown(string taskId)
    {
        if (!projectRoot.Exists)
            return AppResult<string>.Fail("missing_project", "Project not found. Run pm init first.");

        return projectRoot.TryReadTaskFile(taskId, out var content)
            ? AppResult<string>.Ok(content)
            : AppResult<string>.Fail("missing_task", $"Task {taskId} not found.");
    }

    public AppResult ValidateEditedTaskMarkdown(string taskId, string editedContent)
    {
        if (!TaskItem.TryParse(editedContent, out var editedTask, out var errorCode, out var message) ||
            editedTask == null)
            return AppResult.Fail(errorCode == "invalid_task_priority" ? "invalid_priority" : "invalid_edited_markdown",
                errorCode == "invalid_task_priority" ? message : "Edited task markdown is invalid.");

        if (!string.Equals(editedTask.Id, taskId, StringComparison.Ordinal))
            return AppResult.Fail("changed_task_id", "Task ID cannot be changed.");

        var dependencyValidation = NormalizeDependenciesForWrite(editedTask.DependencyIds);
        if (!dependencyValidation.Success)
            return AppResult.Fail(dependencyValidation.ErrorCode!, dependencyValidation.Message!);

        if (TaskItem.HasSelfDependency(editedTask.Id, editedTask.DependencyIds, GetActiveProjectId()))
            return AppResult.Fail("invalid_dependency", $"Task {editedTask.Id} cannot depend on itself.");

        return AppResult.Ok();
    }

    public AppResult SaveEditedTaskContent(string taskId, string editedContent)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryReadTaskFile(taskId, out _))
            return AppResult.Fail("missing_task", $"Task {taskId} not found.");

        var validation = ValidateEditedTaskMarkdown(taskId, editedContent);
        if (!validation.Success) return validation;

        var oldTask = TaskItem.Parse(projectRoot.TryReadTaskFile(taskId, out var oldContent) ? oldContent : string.Empty);
        var editedTask = TaskItem.Parse(editedContent);
        if (oldTask != null && editedTask != null &&
            !string.Equals(oldTask.Milestone, editedTask.Milestone, StringComparison.Ordinal))
        {
            var preflight = PreflightMilestonePlacements([editedTask]);
            if (!preflight.Success) return preflight;
        }

        if (oldTask != null && editedTask != null && projectRoot.TryGetState(oldTask, out var state))
            projectRoot.MoveTaskOrderScope(taskId,
                new TaskOrderScope(projectRoot.ResolveTaskTrack(oldTask), state, oldTask.Milestone),
                new TaskOrderScope(projectRoot.ResolveTaskTrack(editedTask), state, editedTask.Milestone));

        projectRoot.WriteTaskFile(taskId, editedContent);
        return AppResult.Ok();
    }

    public AppResult<TaskMutationResult> PatchTaskMetadata(
        string taskId,
        string? title = null,
        string? track = null,
        string? milestone = null,
        string? description = null,
        string? priority = null,
        IReadOnlyList<string>? dependsOn = null)
    {
        if (!projectRoot.Exists)
            return AppResult<TaskMutationResult>.Fail("missing_project", "Project not found. Run pm init first.");

        var config = projectRoot.Config!;
        if (title != null && string.IsNullOrWhiteSpace(title))
            return AppResult<TaskMutationResult>.Fail("invalid_title", "Task title is required.");

        var normalizedTrack = track == null ? null : track.Trim();
        if (normalizedTrack != null && !config.Tracks.ContainsKey(normalizedTrack))
            return AppResult<TaskMutationResult>.Fail("invalid_track", $"Track {normalizedTrack} not found.");

        var normalizedMilestone = milestone == null
            ? null
            : string.IsNullOrWhiteSpace(milestone) ? null : milestone.Trim();
        if (milestone != null && normalizedMilestone != null && !config.Milestones.ContainsKey(normalizedMilestone))
            return AppResult<TaskMutationResult>.Fail("invalid_milestone", $"Milestone {normalizedMilestone} not found.");

        string? normalizedPriority = null;
        if (priority != null && !PriorityLevel.TryNormalizePatchValue(priority, out normalizedPriority))
            return AppResult<TaskMutationResult>.Fail("invalid_priority",
                $"Task priority must be inherit or one of {string.Join(", ", PriorityLevel.Values)}.");

        IReadOnlyList<string>? normalizedDependencies = null;
        if (dependsOn != null)
        {
            var dependencyResult = NormalizeDependenciesForWrite(dependsOn);
            if (!dependencyResult.Success)
                return AppResult<TaskMutationResult>.Fail(dependencyResult.ErrorCode!, dependencyResult.Message!);
            normalizedDependencies = dependencyResult.Payload!;
        }

        if (!projectRoot.TryGetById(taskId, out var task))
            return AppResult<TaskMutationResult>.Fail("missing_task", $"Task with ID {taskId} not found.");

        if (normalizedDependencies != null &&
            TaskItem.HasSelfDependency(task.Id, normalizedDependencies, GetActiveProjectId()))
            return AppResult<TaskMutationResult>.Fail("invalid_dependency", $"Task {task.Id} cannot depend on itself.");

        if (!projectRoot.TryGetState(task, out var state))
            return AppResult<TaskMutationResult>.Fail("missing_current_state", $"Task with ID {taskId} has no associated state.");

        var updated = task with
        {
            Title = title == null ? task.Title : title.Trim(),
            Track = normalizedTrack ?? task.Track,
            Milestone = milestone == null ? task.Milestone : normalizedMilestone,
            Priority = priority == null ? task.Priority : normalizedPriority,
            DependsOn = dependsOn == null ? task.DependsOn : normalizedDependencies!.ToListOrNull(),
            Description = description ?? task.Description,
        };

        var changed =
            !string.Equals(updated.Title, task.Title, StringComparison.Ordinal) ||
            !string.Equals(projectRoot.ResolveTaskTrack(updated), projectRoot.ResolveTaskTrack(task), StringComparison.Ordinal) ||
            !string.Equals(updated.Milestone, task.Milestone, StringComparison.Ordinal) ||
            !string.Equals(updated.Priority, task.Priority, StringComparison.Ordinal) ||
            !updated.DependencyIds.SequenceEqual(task.DependencyIds, StringComparer.Ordinal) ||
            !string.Equals(updated.Description, task.Description, StringComparison.Ordinal);

        if (!changed)
            return AppResult<TaskMutationResult>.Ok(new TaskMutationResult(false, task));

        if (!string.Equals(updated.Milestone, task.Milestone, StringComparison.Ordinal))
        {
            var preflight = PreflightMilestonePlacements([updated]);
            if (!preflight.Success)
                return AppResult<TaskMutationResult>.Fail(preflight.ErrorCode!, preflight.Message!);
        }

        updated = updated with { ModifiedAt = DateTime.UtcNow };
        projectRoot.MoveTaskOrderScope(task.Id,
            new TaskOrderScope(projectRoot.ResolveTaskTrack(task), state, task.Milestone),
            new TaskOrderScope(projectRoot.ResolveTaskTrack(updated), state, updated.Milestone));
        projectRoot.WriteTask(updated);
        return AppResult<TaskMutationResult>.Ok(new TaskMutationResult(true, updated));
    }

    public AppResult<TaskMutationResult> AppendTaskNote(string taskId, string note)
    {
        if (!projectRoot.Exists)
            return AppResult<TaskMutationResult>.Fail("missing_project", "Project not found. Run pm init first.");

        if (string.IsNullOrWhiteSpace(note))
            return AppResult<TaskMutationResult>.Fail("invalid_note", "Task note is required.");

        if (!projectRoot.TryGetById(taskId, out var task))
            return AppResult<TaskMutationResult>.Fail("missing_task", $"Task with ID {taskId} not found.");

        if (!projectRoot.TryGetState(task, out _))
            return AppResult<TaskMutationResult>.Fail("missing_current_state", $"Task with ID {taskId} has no associated state.");

        var updated = task with
        {
            Description = AppendNote(task.Description, note),
            ModifiedAt = DateTime.UtcNow,
        };
        projectRoot.WriteTask(updated);
        return AppResult<TaskMutationResult>.Ok(new TaskMutationResult(true, updated));
    }

    public AppResult<TaskReorderResult> ReorderTasks(
        string track,
        string state,
        IReadOnlyList<string> taskIds,
        string? milestone = null)
    {
        if (!projectRoot.Exists)
            return AppResult<TaskReorderResult>.Fail("missing_project", "Project not found. Run pm init first.");

        track = track.Trim();
        state = state.Trim();
        milestone = string.IsNullOrWhiteSpace(milestone) ? null : milestone.Trim();
        var config = projectRoot.Config!;

        if (!config.Tracks.ContainsKey(track))
            return AppResult<TaskReorderResult>.Fail("invalid_track", $"Track {track} not found.");

        if (!config.TaskStates.ContainsKey(state))
            return AppResult<TaskReorderResult>.Fail("invalid_state", $"State {state} not found.");

        if (milestone != null && !config.Milestones.ContainsKey(milestone))
            return AppResult<TaskReorderResult>.Fail("invalid_milestone", $"Milestone {milestone} not found.");

        var normalizedIds = taskIds.Select(id => id.Trim()).ToList();
        if (normalizedIds.Any(string.IsNullOrWhiteSpace) ||
            normalizedIds.Distinct(StringComparer.Ordinal).Count() != normalizedIds.Count)
            return AppResult<TaskReorderResult>.Fail("invalid_task_order",
                "Task order must contain each task in the scope exactly once.");

        var scopedIds = projectRoot.GetAllTasks()
            .Where(task => projectRoot.ResolveTaskTrack(task) == track)
            .Where(task => string.Equals(task.Milestone, milestone, StringComparison.Ordinal))
            .Where(task => projectRoot.TryGetState(task, out var taskState) && taskState == state)
            .Select(task => task.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (!scopedIds.SequenceEqual(normalizedIds.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
            return AppResult<TaskReorderResult>.Fail("invalid_task_order",
                "Task order must contain each task in the scope exactly once.");

        var scope = new TaskOrderScope(track, state, milestone);
        var changed = projectRoot.SetTaskOrder(scope, normalizedIds);
        return AppResult<TaskReorderResult>.Ok(new TaskReorderResult(track, state, milestone, normalizedIds, changed));
    }

    public AppResult<TaskItem> UpdateTaskDetails(
        string taskId,
        string title,
        string targetState,
        string description,
        string? priority = null,
        TaskPlacementUpdate? placement = null)
    {
        if (!projectRoot.Exists)
            return AppResult<TaskItem>.Fail("missing_project", "Project not found. Run pm init first.");

        if (string.IsNullOrWhiteSpace(title))
            return AppResult<TaskItem>.Fail("invalid_title", "Task title is required.");

        if (!projectRoot.Config!.TaskStates.ContainsKey(targetState))
            return AppResult<TaskItem>.Fail("invalid_state", $"State {targetState} not found.");

        string? normalizedPriority = null;
        if (priority != null && !PriorityLevel.TryNormalizePatchValue(priority, out normalizedPriority))
            return AppResult<TaskItem>.Fail("invalid_priority",
                $"Task priority must be inherit or one of {string.Join(", ", PriorityLevel.Values)}.");

        string? normalizedTrack = null;
        string? normalizedMilestone = null;
        if (placement != null)
        {
            normalizedTrack = placement.Track?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTrack) ||
                !projectRoot.Config.Tracks.ContainsKey(normalizedTrack))
                return AppResult<TaskItem>.Fail("invalid_track", $"Track {normalizedTrack} not found.");

            normalizedMilestone = string.IsNullOrWhiteSpace(placement.Milestone)
                ? null
                : placement.Milestone.Trim();
            if (placement.Milestone != null && string.IsNullOrWhiteSpace(placement.Milestone))
                return AppResult<TaskItem>.Fail("invalid_milestone", "Task milestone must be configured or null.");
            if (normalizedMilestone != null && !projectRoot.Config.Milestones.ContainsKey(normalizedMilestone))
                return AppResult<TaskItem>.Fail("invalid_milestone", $"Milestone {normalizedMilestone} not found.");
        }

        if (!projectRoot.TryGetById(taskId, out var task))
            return AppResult<TaskItem>.Fail("missing_task", $"Task with ID {taskId} not found.");

        if (!projectRoot.TryGetState(task, out var currentState))
            return AppResult<TaskItem>.Fail("missing_current_state", $"Task with ID {taskId} has no associated state.");

        var updated = task with
        {
            Title = title.Trim(),
            Track = placement == null ? task.Track : normalizedTrack!,
            Milestone = placement == null ? task.Milestone : normalizedMilestone,
            Priority = priority == null ? task.Priority : normalizedPriority,
            ModifiedAt = DateTime.UtcNow,
            Description = description ?? string.Empty,
        };

        if (!string.Equals(updated.Milestone, task.Milestone, StringComparison.Ordinal))
        {
            var preflight = PreflightMilestonePlacements([updated]);
            if (!preflight.Success)
                return AppResult<TaskItem>.Fail(preflight.ErrorCode!, preflight.Message!);
        }

        try
        {
            projectRoot.MoveTaskOrderScope(task.Id,
                new TaskOrderScope(projectRoot.ResolveTaskTrack(task), currentState, task.Milestone),
                new TaskOrderScope(projectRoot.ResolveTaskTrack(updated), currentState, updated.Milestone));
            projectRoot.WriteTask(updated);
            projectRoot.UpdateTaskState(updated, targetState);
            return AppResult<TaskItem>.Ok(updated);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            TryRestoreTask(task, updated, currentState);
            return AppResult<TaskItem>.Fail("task_storage_write_failed", $"Task {taskId} could not be updated.");
        }
    }

    private void TryDeletePartialTask(TaskItem task)
    {
        try
        {
            projectRoot.DeleteTask(task);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            // Preserve the bounded create failure.
        }
    }

    private AppResult PreflightMilestonePlacements(IReadOnlyList<TaskItem> updatedTasks)
    {
        var tasksById = projectRoot.GetAllTasks()
            .GroupBy(task => task.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var task in updatedTasks)
            tasksById[task.Id] = task;

        var cycle = activationGraph.Build(projectRoot.Config!, tasksById).Cycles.FirstOrDefault();
        return cycle == null
            ? AppResult.Ok()
            : AppResult.Fail(
                "activation_cycle",
                $"Task milestone placement would create an activation cycle: {string.Join(" -> ", cycle.Path)}.");
    }

    private void TryRestoreTask(TaskItem task, TaskItem updated, string state)
    {
        try
        {
            projectRoot.MoveTaskOrderScope(task.Id,
                new TaskOrderScope(projectRoot.ResolveTaskTrack(updated), state, updated.Milestone),
                new TaskOrderScope(projectRoot.ResolveTaskTrack(task), state, task.Milestone));
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, state);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            // Preserve the bounded update failure.
        }
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private static string AppendNote(string description, string note)
    {
        var normalizedDescription = (description ?? string.Empty).ReplaceLineEndings("\n").TrimEnd();
        var normalizedNote = note.Trim().ReplaceLineEndings("\n");
        var noteLines = normalizedNote.Split('\n');
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var formattedNote = $"- {timestamp} - {noteLines[0]}";
        if (noteLines.Length > 1)
            formattedNote += "\n" + string.Join('\n', noteLines.Skip(1).Select(line => $"  {line}"));

        if (string.IsNullOrWhiteSpace(normalizedDescription))
            return $"## Notes\n\n{formattedNote}";

        if (normalizedDescription.Contains("## Notes", StringComparison.Ordinal))
            return $"{normalizedDescription}\n{formattedNote}";

        return $"{normalizedDescription}\n\n## Notes\n\n{formattedNote}";
    }

    private AppResult<IReadOnlyList<string>> NormalizeDependenciesForWrite(IEnumerable<string?> values)
    {
        var activeProjectId = GetActiveProjectId();
        var normalized = new List<string>();
        foreach (var value in TaskItem.NormalizeDependencyIds(values))
        {
            if (!TaskDependencyReference.TryParse(value, out var dependency, out var message))
                return AppResult<IReadOnlyList<string>>.Fail(
                    "invalid_dependency_reference", $"Dependency {value} is invalid: {message}");
            normalized.Add(dependency!.ToPersistedValue(activeProjectId));
        }

        return AppResult<IReadOnlyList<string>>.Ok(normalized.Distinct(StringComparer.Ordinal).ToList());
    }

    private string? GetActiveProjectId() =>
        projectRoot.TryReadProjectId(out var projectId) ? projectId : null;

    private static IReadOnlyList<(string Label, string Value, bool IsFallback)> BuildSearchFields(
        TaskItem task,
        string markdown,
        string track,
        string state,
        string priority)
    {
        return
        [
            ("Description", task.Description, false),
            ("Title", task.Title, false),
            ("ID", task.Id, false),
            ("Track", track, false),
            ("Milestone", task.Milestone ?? string.Empty, false),
            ("State", state, false),
            ("Priority", priority, false),
            ("Dependencies", string.Join(' ', task.DependencyIds), false),
            ("Markdown", markdown, true),
        ];
    }

    private static int CountSearchMatches(
        IReadOnlyList<(string Label, string Value, bool IsFallback)> fields,
        string query)
    {
        var semanticMatchCount = fields
            .Where(field => !field.IsFallback)
            .Sum(field => CountMatches(field.Value, query));
        return semanticMatchCount > 0
            ? semanticMatchCount
            : fields.Where(field => field.IsFallback).Sum(field => CountMatches(field.Value, query));
    }

    private static int CountMatches(string value, string query)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = value.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return count;
            count++;
            index += query.Length;
        }
    }

    private static string BuildSnippet(
        IReadOnlyList<(string Label, string Value, bool IsFallback)> fields,
        string query)
    {
        var field = fields.FirstOrDefault(field =>
            !string.IsNullOrWhiteSpace(field.Value) &&
            field.Value.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(field.Value))
            field = fields.FirstOrDefault(field => !string.IsNullOrWhiteSpace(field.Value));

        if (string.IsNullOrWhiteSpace(field.Value)) return string.Empty;

        var haystack = NormalizeSnippetText(field.Value);
        var index = haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = 0;

        var start = Math.Max(0, index - 40);
        var length = Math.Min(120, haystack.Length - start);
        var snippet = haystack.Substring(start, length).Trim();
        if (start > 0) snippet = "..." + snippet;
        if (start + length < haystack.Length) snippet += "...";
        return $"{field.Label}: {snippet}";
    }

    private static string NormalizeSnippetText(string value)
    {
        return string.Join(' ', value
            .ReplaceLineEndings("\n")
            .Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static TaskItem BuildTask(
        string title,
        string track,
        string? milestone,
        string description,
        string idPadded)
    {
        return new TaskItem
        {
            Id = $"{track}-{idPadded}",
            Title = title.Trim(),
            Track = track,
            Milestone = milestone,
            Description = string.IsNullOrWhiteSpace(description) ? string.Empty : description,
        };
    }
}
