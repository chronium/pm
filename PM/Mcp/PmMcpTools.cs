using System.ComponentModel;
using ModelContextProtocol.Server;
using PM.Application;
using PM.Project;

namespace PM.Mcp;

[McpServerToolType]
public sealed class PmMcpTools(
    ProjectRoot projectRoot,
    TaskService taskService,
    ProjectConfigService configService,
    BoardService boardService)
{
    [McpServerTool(Name = "get_project", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns project name, root path, states, tracks, and milestones.")]
    public McpToolResponse<ProjectPayload> GetProject()
    {
        if (!projectRoot.Exists || projectRoot.Config == null || projectRoot.RootPath == null)
            return McpToolResponse<ProjectPayload>.Fail("missing_project", "Project not found. Run pm init first.");

        var config = projectRoot.Config;
        var payload = new ProjectPayload(
            config.Name,
            projectRoot.RootPath,
            ToOptions(config.TaskStates),
            ToOptions(config.Tracks),
            ToOptions(config.Milestones));

        return McpToolResponse<ProjectPayload>.Ok($"Project {config.Name} loaded.", payload);
    }

    [McpServerTool(Name = "list_tasks", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists tasks with optional track, milestone, and state filters.")]
    public McpToolResponse<TaskListPayload> ListTasks(
        string? track = null,
        string? milestone = null,
        string? state = null)
    {
        var result = boardService.GetBoard(new BoardQuery(NormalizeFilter(track), NormalizeFilter(milestone),
            NormalizeFilter(state)));
        if (!result.Success)
            return McpToolResponse<TaskListPayload>.FromFailure(result);

        var tasks = result.Payload!.MilestoneGroups
            .SelectMany(group => group.States)
            .SelectMany(group => group.Tasks)
            .Select(ToTaskSummary)
            .ToList();

        return McpToolResponse<TaskListPayload>.Ok($"Returned {tasks.Count} task(s).", new TaskListPayload(tasks));
    }

    [McpServerTool(Name = "get_task", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns a task's metadata, current state, file path, markdown, and description.")]
    public McpToolResponse<TaskDetailPayload> GetTask(string taskId)
    {
        var markdownResult = taskService.ReadTaskMarkdown(taskId);
        if (!markdownResult.Success)
            return McpToolResponse<TaskDetailPayload>.FromFailure(markdownResult);

        if (!projectRoot.TryGetById(taskId, out var task))
            return McpToolResponse<TaskDetailPayload>.Fail("missing_task", $"Task {taskId} not found.");

        var state = projectRoot.TryGetState(task, out var currentState) ? currentState : string.Empty;
        var payload = new TaskDetailPayload(
            task.Id,
            task.Title,
            projectRoot.ResolveTaskTrack(task),
            task.Milestone,
            task.CreatedAt,
            task.ModifiedAt,
            state,
            projectRoot.GetTaskFilePath(task.Id),
            markdownResult.Payload!,
            task.Description);

        return McpToolResponse<TaskDetailPayload>.Ok($"Task {task.Id} loaded.", payload);
    }

    [McpServerTool(Name = "list_tracks", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists configured tracks.")]
    public McpToolResponse<IReadOnlyList<OptionPayload>> ListTracks()
    {
        var project = GetProject();
        return project.Success
            ? McpToolResponse<IReadOnlyList<OptionPayload>>.Ok(
                $"Returned {project.Data!.Tracks.Count} track(s).", project.Data.Tracks)
            : McpToolResponse<IReadOnlyList<OptionPayload>>.Fail(project.ErrorCode!, project.Message!);
    }

    [McpServerTool(Name = "list_milestones", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists configured milestones.")]
    public McpToolResponse<IReadOnlyList<OptionPayload>> ListMilestones()
    {
        var project = GetProject();
        return project.Success
            ? McpToolResponse<IReadOnlyList<OptionPayload>>.Ok(
                $"Returned {project.Data!.Milestones.Count} milestone(s).", project.Data.Milestones)
            : McpToolResponse<IReadOnlyList<OptionPayload>>.Fail(project.ErrorCode!, project.Message!);
    }

    [McpServerTool(Name = "list_states", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists configured task states.")]
    public McpToolResponse<IReadOnlyList<OptionPayload>> ListStates()
    {
        var project = GetProject();
        return project.Success
            ? McpToolResponse<IReadOnlyList<OptionPayload>>.Ok(
                $"Returned {project.Data!.States.Count} state(s).", project.Data.States)
            : McpToolResponse<IReadOnlyList<OptionPayload>>.Fail(project.ErrorCode!, project.Message!);
    }

    [McpServerTool(Name = "create_task", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates a task using track-scoped ID allocation.")]
    public async Task<McpToolResponse<CreatedTaskPayload>> CreateTask(
        string title,
        string track,
        string? milestone = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var result = await taskService.CreateTask(title, track, milestone, description ?? string.Empty, false,
            cancellationToken);
        if (!result.Success)
            return McpToolResponse<CreatedTaskPayload>.FromFailure(result);

        var task = result.Payload!;
        var payload = new CreatedTaskPayload(
            task.Id,
            task.Title,
            projectRoot.ResolveTaskTrack(task),
            task.Milestone,
            projectRoot.GetTaskFilePath(task.Id));

        return McpToolResponse<CreatedTaskPayload>.Ok($"Created task {task.Id}.", payload);
    }

    [McpServerTool(Name = "move_task", Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Moves a task to the target state.")]
    public McpToolResponse<MutatedPayload> MoveTask(string taskId, string targetState)
    {
        var result = taskService.MoveTask(taskId, targetState);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Moved task {taskId} to {targetState}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "update_task_markdown", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Replaces a task markdown file after validating the task ID is unchanged.")]
    public McpToolResponse<MutatedPayload> UpdateTaskMarkdown(string taskId, string markdown)
    {
        var result = taskService.SaveEditedTaskContent(taskId, markdown);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Updated task {taskId}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "add_track", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Adds a new track.")]
    public McpToolResponse<MutatedPayload> AddTrack(string key, string displayName)
    {
        var result = configService.AddTrack(key, displayName);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Added track {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "add_milestone", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Adds a new milestone.")]
    public McpToolResponse<MutatedPayload> AddMilestone(string key, string title)
    {
        var result = configService.AddMilestone(key, title);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Added milestone {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    private static IReadOnlyList<OptionPayload> ToOptions(Dictionary<string, string> options)
    {
        return options.Select(option => new OptionPayload(option.Key, option.Value)).ToList();
    }

    private static TaskSummaryPayload ToTaskSummary(BoardTask task)
    {
        return new TaskSummaryPayload(
            task.Task.Id,
            task.Task.Title,
            task.Track,
            task.Milestone,
            task.State,
            task.DescriptionPreview,
            task.FilePath);
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
