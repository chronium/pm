using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed class TaskService(ProjectRoot projectRoot, INextIdService nextIdService)
{
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
