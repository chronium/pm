using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed record BulkTaskCreateInput(string Title, string? Description = null);

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

public sealed class TaskService(ProjectRoot projectRoot, INextIdService nextIdService)
{
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
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, config.TaskStates.Keys.First());
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

        projectRoot.UpdateTaskState(task, targetState);
        return AppResult.Ok();
    }

    public AppResult RemoveTask(string taskId)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryGetById(taskId, out var task))
            return AppResult.Fail("missing_task", $"Task with ID {taskId} not found.");

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

        var tasks = new List<TaskItem>();
        foreach (var taskId in normalizedIds)
        {
            if (!projectRoot.TryGetById(taskId, out var task))
                return AppResult<BulkMilestoneAssignmentResult>.Fail("missing_task", $"Task with ID {taskId} not found.");

            tasks.Add(task);
        }

        var changed = 0;
        foreach (var task in tasks)
        {
            if (string.Equals(task.Milestone, milestone, StringComparison.Ordinal))
                continue;

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
        var editedTask = TaskItem.Parse(editedContent);
        if (editedTask == null)
            return AppResult.Fail("invalid_edited_markdown", "Edited task markdown is invalid.");

        if (!string.Equals(editedTask.Id, taskId, StringComparison.Ordinal))
            return AppResult.Fail("changed_task_id", "Task ID cannot be changed.");

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

        projectRoot.WriteTaskFile(taskId, editedContent);
        return AppResult.Ok();
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
