using System.ComponentModel;
using ModelContextProtocol.Server;
using PM.Application;
using PM.Project;

namespace PM.Mcp;

[McpServerToolType]
public sealed class PmMcpTools(
    ProjectRoot projectRoot,
    TaskService taskService,
    ProjectCreationService projectCreationService,
    ProjectConfigService configService,
    BoardService boardService,
    WikiService wikiService)
{
    [McpServerTool(Name = "create_project", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Initializes a PM project in the current directory.")]
    public async Task<McpToolResponse<ProjectPayload>> CreateProject(
        string name,
        int? idWidth = null,
        string? idPrefix = null,
        string? nextIdServiceUrl = null,
        Dictionary<string, string?>? states = null,
        Dictionary<string, string?>? tracks = null,
        Dictionary<string, string?>? milestones = null,
        CancellationToken cancellationToken = default)
    {
        var result = await projectCreationService.CreateProject(new ProjectCreationRequest(
            name,
            idWidth,
            idPrefix,
            nextIdServiceUrl,
            states,
            tracks,
            milestones), cancellationToken);
        if (!result.Success)
            return McpToolResponse<ProjectPayload>.FromFailure(result);

        var project = result.Payload!;
        var payload = new ProjectPayload(
            project.Name,
            project.RootPath,
            ToOptions(project.States),
            ToOptions(project.Tracks),
            ToOptions(project.Milestones),
            project.ProjectId,
            project.RecoveryKey);

        return McpToolResponse<ProjectPayload>.Ok($"Project {project.Name} initialized.", payload);
    }

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

    [McpServerTool(Name = "bulk_create_tasks_for_track", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates 1 to 100 tasks in one track using sequential track-scoped ID allocation.")]
    public async Task<McpToolResponse<BulkCreatedTasksPayload>> BulkCreateTasksForTrack(
        string track,
        IReadOnlyList<BulkTaskInputPayload> tasks,
        CancellationToken cancellationToken = default)
    {
        var result = await taskService.BulkCreateTasksForTrack(
            track,
            tasks.Select(task => new BulkTaskCreateInput(task.Title, task.Description)).ToList(),
            cancellationToken);
        if (!result.Success)
            return McpToolResponse<BulkCreatedTasksPayload>.FromFailure(result);

        var bulk = result.Payload!;
        var payload = new BulkCreatedTasksPayload(
            bulk.Track,
            bulk.Tasks.Select(task => new BulkCreatedTaskPayload(
                task.Id,
                task.Title,
                task.Track,
                task.Milestone,
                task.FilePath)).ToList(),
            bulk.RequestedCount,
            bulk.CreatedCount,
            bulk.Failure == null ? null : new BulkFailurePayload(bulk.Failure.ErrorCode, bulk.Failure.Message));

        var summary = bulk.Failure == null
            ? $"Created {bulk.CreatedCount} task(s)."
            : $"Created {bulk.CreatedCount} of {bulk.RequestedCount} task(s); stopped after {bulk.Failure.ErrorCode}.";
        return McpToolResponse<BulkCreatedTasksPayload>.Ok(summary, payload);
    }

    [McpServerTool(Name = "bulk_assign_tasks_to_milestone", Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Assigns 1 to 100 existing tasks to a milestone.")]
    public McpToolResponse<BulkMilestoneAssignmentPayload> BulkAssignTasksToMilestone(
        string milestone,
        IReadOnlyList<string> taskIds)
    {
        var result = taskService.BulkAssignTasksToMilestone(milestone, taskIds);
        if (!result.Success)
            return McpToolResponse<BulkMilestoneAssignmentPayload>.FromFailure(result);

        var assignment = result.Payload!;
        var payload = new BulkMilestoneAssignmentPayload(
            assignment.Milestone,
            assignment.TaskIds,
            assignment.FilePaths,
            assignment.RequestedCount,
            assignment.UpdatedCount);

        return McpToolResponse<BulkMilestoneAssignmentPayload>.Ok(
            $"Assigned {assignment.RequestedCount} task(s) to {assignment.Milestone}.",
            payload);
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

    [McpServerTool(Name = "remove_task", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Permanently removes a task and its state reference.")]
    public McpToolResponse<MutatedPayload> RemoveTask(string taskId)
    {
        var result = taskService.RemoveTask(taskId);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Removed task {taskId}.", new MutatedPayload(true))
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

    [McpServerTool(Name = "list_wiki_pages", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists wiki pages with path, title, modified timestamp, and file path.")]
    public McpToolResponse<WikiPageListPayload> ListWikiPages()
    {
        var result = wikiService.ListPages();
        if (!result.Success)
            return McpToolResponse<WikiPageListPayload>.FromFailure(result);

        var pages = result.Payload!
            .Select(page => new WikiPageSummaryPayload(page.Path, page.Title, page.ModifiedAt, page.FilePath))
            .ToList();

        return McpToolResponse<WikiPageListPayload>.Ok($"Returned {pages.Count} wiki page(s).",
            new WikiPageListPayload(pages));
    }

    [McpServerTool(Name = "get_wiki_page", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns a wiki page's metadata, file path, full markdown, and markdown body.")]
    public McpToolResponse<WikiPagePayload> GetWikiPage(string path)
    {
        var result = wikiService.ReadPage(path);
        return result.Success
            ? McpToolResponse<WikiPagePayload>.Ok($"Wiki page {result.Payload!.Path} loaded.",
                ToWikiPagePayload(result.Payload))
            : McpToolResponse<WikiPagePayload>.FromFailure(result);
    }

    [McpServerTool(Name = "create_wiki_page", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates a wiki page from a slash-separated path, title, and markdown body.")]
    public McpToolResponse<WikiPagePayload> CreateWikiPage(string path, string title, string body = "")
    {
        var result = wikiService.CreatePage(path, title, body);
        return result.Success
            ? McpToolResponse<WikiPagePayload>.Ok($"Created wiki page {result.Payload!.Path}.",
                ToWikiPagePayload(result.Payload))
            : McpToolResponse<WikiPagePayload>.FromFailure(result);
    }

    [McpServerTool(Name = "update_wiki_page_markdown", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Replaces a wiki markdown file after validating frontmatter.")]
    public McpToolResponse<WikiPagePayload> UpdateWikiPageMarkdown(string path, string markdown)
    {
        var result = wikiService.UpdatePageMarkdown(path, markdown);
        return result.Success
            ? McpToolResponse<WikiPagePayload>.Ok($"Updated wiki page {result.Payload!.Path}.",
                ToWikiPagePayload(result.Payload))
            : McpToolResponse<WikiPagePayload>.FromFailure(result);
    }

    [McpServerTool(Name = "rename_wiki_page", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Renames a wiki page path, title, or both while preserving body and created timestamp.")]
    public McpToolResponse<WikiPagePayload> RenameWikiPage(string path, string newPath, string title)
    {
        var result = wikiService.RenamePage(path, newPath, title);
        return result.Success
            ? McpToolResponse<WikiPagePayload>.Ok($"Renamed wiki page {result.Payload!.Path}.",
                ToWikiPagePayload(result.Payload))
            : McpToolResponse<WikiPagePayload>.FromFailure(result);
    }

    [McpServerTool(Name = "remove_wiki_page", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Permanently removes one wiki page.")]
    public McpToolResponse<MutatedPayload> RemoveWikiPage(string path)
    {
        var result = wikiService.RemovePage(path);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Removed wiki page {path}.", new MutatedPayload(true))
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

    [McpServerTool(Name = "rename_track", Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Renames a track display name without changing its key.")]
    public McpToolResponse<MutatedPayload> RenameTrack(string key, string displayName)
    {
        var result = configService.RenameTrack(key, displayName);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Renamed track {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "remove_track", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes an unused track.")]
    public McpToolResponse<MutatedPayload> RemoveTrack(string key)
    {
        var result = configService.RemoveTrack(key);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Removed track {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "add_status", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Adds a new task status.")]
    public McpToolResponse<MutatedPayload> AddStatus(string key, string displayName)
    {
        var result = configService.AddStatus(key, displayName);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Added status {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "rename_status", Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Renames a status display name without changing its key.")]
    public McpToolResponse<MutatedPayload> RenameStatus(string key, string displayName)
    {
        var result = configService.RenameStatus(key, displayName);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Renamed status {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "remove_status", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes an unused task status.")]
    public McpToolResponse<MutatedPayload> RemoveStatus(string key)
    {
        var result = configService.RemoveStatus(key);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Removed status {key}.", new MutatedPayload(true))
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

    [McpServerTool(Name = "remove_milestone", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes an unused milestone.")]
    public McpToolResponse<MutatedPayload> RemoveMilestone(string key)
    {
        var result = configService.RemoveMilestone(key);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Removed milestone {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "rename_milestone", Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Renames a milestone title without changing its key.")]
    public McpToolResponse<MutatedPayload> RenameMilestone(string key, string title)
    {
        var result = configService.RenameMilestone(key, title);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Renamed milestone {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    private static IReadOnlyList<OptionPayload> ToOptions(IReadOnlyDictionary<string, string> options)
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

    private static WikiPagePayload ToWikiPagePayload(WikiPageData page)
    {
        return new WikiPagePayload(
            page.Path,
            page.Title,
            page.CreatedAt,
            page.ModifiedAt,
            page.FilePath,
            page.Markdown,
            page.Body);
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
