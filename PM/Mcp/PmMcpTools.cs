using System.ComponentModel;
using ModelContextProtocol.Server;
using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Mcp;

[McpServerToolType]
public sealed class PmMcpTools(
    ProjectRoot projectRoot,
    TaskService taskService,
    ProjectCreationService projectCreationService,
    ProjectConfigService configService,
    BoardService boardService,
    WikiService wikiService,
    ProjectValidationService validationService,
    LinkedProjectFamilyService linkedProjectFamilyService,
    LinkedProjectReadService linkedProjectReadService,
    IProjectMembershipService? membershipService,
    McpCapabilityContext capabilityContext)
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
            ToMilestones(project.Milestones, new Dictionary<string, string>()),
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
            ToMilestones(config.Milestones, config.MilestonePriorities));

        return McpToolResponse<ProjectPayload>.Ok($"Project {config.Name} loaded.", payload);
    }

    [McpServerTool(Name = "list_linked_projects", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the bounded linked-project family and structured resolution warnings.")]
    public async Task<McpToolResponse<LinkedProjectFamilyPayload>> ListLinkedProjects(
        CancellationToken cancellationToken = default)
    {
        var result = await linkedProjectFamilyService.ResolveAsync(cancellationToken);
        if (!result.Success)
            return McpToolResponse<LinkedProjectFamilyPayload>.FromFailure(result);

        var family = result.Payload!;
        return McpToolResponse<LinkedProjectFamilyPayload>.Ok(
            $"Returned {family.Members.Count} linked-project family member(s) with {family.Warnings.Count} warning(s).",
            new LinkedProjectFamilyPayload(
                family.ActiveProjectId,
                family.Members.Select(member => new LinkedProjectMemberPayload(
                    member.ProjectId,
                    member.Name,
                    member.Alias,
                    LinkedProjectFamilyService.Format(member.Relationship),
                    LinkedProjectFamilyService.Format(member.Status),
                    LinkedProjectFamilyService.Format(member.Source),
                    member.Readable,
                    member.WriteTrusted)).ToList(),
                family.Warnings.Select(warning => new LinkedProjectWarningPayload(
                    warning.Code,
                    warning.Message,
                    warning.DeclaringProjectId,
                    warning.TargetProjectId,
                    warning.Alias,
                    LinkedProjectFamilyService.Format(warning.Status),
                    warning.RepairAction?.DisplayCommand)).ToList()));
    }

    [McpServerTool(Name = "get_local_identity", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns the shareable local PM identity and public-key fingerprint. Never returns a private key.")]
    public McpToolResponse<LocalIdentityPayload> GetLocalIdentity()
    {
        if (membershipService == null) return MembershipUnavailable<LocalIdentityPayload>();
        var result = membershipService.GetLocalIdentity();
        return result.Success
            ? McpToolResponse<LocalIdentityPayload>.Ok("Local PM identity loaded.",
                new LocalIdentityPayload(result.Payload!.UserId, result.Payload.DisplayName,
                    result.Payload.PublicKey, result.Payload.Fingerprint))
            : McpToolResponse<LocalIdentityPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "list_project_members", ReadOnly = true, Destructive = false, OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists remote project members and identifies the current local identity.")]
    public async Task<McpToolResponse<ProjectMembersPayload>> ListProjectMembers(
        CancellationToken cancellationToken = default)
    {
        if (membershipService == null) return MembershipUnavailable<ProjectMembersPayload>();
        var result = await membershipService.ListMembers(cancellationToken);
        if (!result.Success) return McpToolResponse<ProjectMembersPayload>.FromFailure(result);
        var value = result.Payload!;
        return McpToolResponse<ProjectMembersPayload>.Ok($"Listed {value.Members.Count} project member(s).",
            new ProjectMembersPayload(value.ProjectId, value.CurrentUserId, value.CurrentRole, value.Authenticated,
                value.Members.Select(ToMembershipPayload).ToList()));
    }

    [McpServerTool(Name = "list_project_invitations", ReadOnly = true, Destructive = false, OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists active pending project invitations without invitation secrets.")]
    public async Task<McpToolResponse<ProjectInvitationsPayload>> ListProjectInvitations(
        CancellationToken cancellationToken = default)
    {
        if (membershipService == null) return MembershipUnavailable<ProjectInvitationsPayload>();
        var result = await membershipService.ListInvitations(cancellationToken);
        return result.Success
            ? McpToolResponse<ProjectInvitationsPayload>.Ok(
                $"Listed {result.Payload!.Invitations.Count} active invitation(s).",
                new ProjectInvitationsPayload(result.Payload.Invitations.Select(ToInvitationPayload).ToList()))
            : McpToolResponse<ProjectInvitationsPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "create_project_invitation", Destructive = false, OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Creates a 24-hour, single-use invitation. This is the only membership tool that returns a secret.")]
    public async Task<McpToolResponse<CreatedProjectInvitationPayload>> CreateProjectInvitation(
        string role = "user", CancellationToken cancellationToken = default)
    {
        if (membershipService == null) return MembershipUnavailable<CreatedProjectInvitationPayload>();
        var result = await membershipService.CreateInvitation(role, cancellationToken);
        return result.Success
            ? McpToolResponse<CreatedProjectInvitationPayload>.Ok("Project invitation created; store its secret now.",
                new CreatedProjectInvitationPayload(ToInvitationPayload(result.Payload!.Invitation), result.Payload.Token))
            : McpToolResponse<CreatedProjectInvitationPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "accept_project_invitation", Destructive = false, OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Accepts an invitation using the independent local PM identity.")]
    public async Task<McpToolResponse<ProjectMemberPayload>> AcceptProjectInvitation(
        string token, CancellationToken cancellationToken = default)
    {
        if (membershipService == null) return MembershipUnavailable<ProjectMemberPayload>();
        var result = await membershipService.AcceptInvitation(token, cancellationToken);
        return result.Success
            ? McpToolResponse<ProjectMemberPayload>.Ok("Project invitation accepted.", ToMembershipPayload(result.Payload!))
            : McpToolResponse<ProjectMemberPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "revoke_project_invitation", Destructive = true, Idempotent = true, OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Revokes an active pending project invitation.")]
    public async Task<McpToolResponse<MutatedPayload>> RevokeProjectInvitation(
        string invitationId, CancellationToken cancellationToken = default)
    {
        if (membershipService == null) return MembershipUnavailable<MutatedPayload>();
        var result = await membershipService.RevokeInvitation(invitationId, cancellationToken);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok("Project invitation revoked.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "update_project_member_role", Destructive = true, Idempotent = true, OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Changes a project member role. The Worker prevents demotion of the final admin.")]
    public async Task<McpToolResponse<ProjectMemberPayload>> UpdateProjectMemberRole(
        string userId, string role, CancellationToken cancellationToken = default)
    {
        if (membershipService == null) return MembershipUnavailable<ProjectMemberPayload>();
        var result = await membershipService.UpdateMemberRole(userId, role, cancellationToken);
        return result.Success
            ? McpToolResponse<ProjectMemberPayload>.Ok("Project member role updated.", ToMembershipPayload(result.Payload!))
            : McpToolResponse<ProjectMemberPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "remove_project_member", Destructive = true, OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Removes a project member. The Worker prevents removal of the final admin.")]
    public async Task<McpToolResponse<MutatedPayload>> RemoveProjectMember(
        string userId, CancellationToken cancellationToken = default)
    {
        if (membershipService == null) return MembershipUnavailable<MutatedPayload>();
        var result = await membershipService.RemoveMember(userId, cancellationToken);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok("Project member removed.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "list_tasks", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists tasks with optional track, milestone, state, linked-project selector, or family scope.")]
    public async Task<McpToolResponse<TaskListPayload>> ListTasks(
        string? track = null,
        string? milestone = null,
        string? state = null,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Read across every available project in the linked family; cannot be combined with project.")]
        bool family = false,
        CancellationToken cancellationToken = default)
    {
        if (IsImplicitCurrent(project, family))
        {
            var local = boardService.GetBoard(new BoardQuery(
                NormalizeFilter(track), NormalizeFilter(milestone), NormalizeFilter(state)));
            if (!local.Success) return McpToolResponse<TaskListPayload>.FromFailure(local);
            var localTasks = local.Payload!.MilestoneGroups
                .SelectMany(group => group.States)
                .SelectMany(group => group.Tasks)
                .Select(task => ToTaskSummary(task))
                .ToList();
            return McpToolResponse<TaskListPayload>.Ok(
                $"Returned {localTasks.Count} task(s).", new TaskListPayload(localTasks));
        }

        var denied = LinkedReadDenied<TaskListPayload>(project, family);
        if (denied != null) return denied;
        var request = LinkedProjectReadRequest.FromOptions(project, family);
        if (!request.Success) return McpToolResponse<TaskListPayload>.FromFailure(request);
        var result = await linkedProjectReadService.ListTasksAsync(
            request.Payload!,
            new BoardQuery(NormalizeFilter(track), NormalizeFilter(milestone), NormalizeFilter(state)),
            cancellationToken);
        if (!result.Success)
            return McpToolResponse<TaskListPayload>.FromFailure(result);

        var tasks = result.Payload!.Items
            .Select(item => ToTaskSummary(item.Resource, item.Owner))
            .ToList();

        return McpToolResponse<TaskListPayload>.Ok($"Returned {tasks.Count} task(s).",
            new TaskListPayload(tasks, ToWarnings(result.Payload.Warnings), result.Payload.Truncated));
    }

    [McpServerTool(Name = "search_tasks", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Searches task IDs, metadata, dependencies, descriptions, and full markdown. Supports optional linked-project selection or family scope. Structured predicates include state:, id:, track:, milestone:, and in:selection or in:all.")]
    public async Task<McpToolResponse<TaskSearchPayload>> SearchTasks(
        string query,
        int limit = 20,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Search every available project in the linked family; cannot be combined with project.")]
        bool family = false,
        CancellationToken cancellationToken = default)
    {
        if (IsImplicitCurrent(project, family))
        {
            var local = taskService.SearchTasks(query, limit);
            if (!local.Success) return McpToolResponse<TaskSearchPayload>.FromFailure(local);
            var localTasks = local.Payload!.Select(task => ToTaskSearchResult(task)).ToList();
            return McpToolResponse<TaskSearchPayload>.Ok(
                $"Returned {localTasks.Count} task search result(s).", new TaskSearchPayload(localTasks));
        }

        var denied = LinkedReadDenied<TaskSearchPayload>(project, family);
        if (denied != null) return denied;
        var request = LinkedProjectReadRequest.FromOptions(project, family);
        if (!request.Success) return McpToolResponse<TaskSearchPayload>.FromFailure(request);
        var result = await linkedProjectReadService.SearchTasksAsync(
            query, limit, request.Payload, cancellationToken: cancellationToken);
        if (!result.Success)
            return McpToolResponse<TaskSearchPayload>.FromFailure(result);

        var tasks = result.Payload!.Items
            .Select(item => ToTaskSearchResult(item.Resource, item.Owner))
            .ToList();

        return McpToolResponse<TaskSearchPayload>.Ok($"Returned {tasks.Count} task search result(s).",
            new TaskSearchPayload(tasks, ToWarnings(result.Payload.Warnings)));
    }

    [McpServerTool(Name = "get_next_task", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns one deterministic recommended actionable task, optionally filtered by track and milestone. By default blocked tasks can be returned when no dependency-ready task is available; set readyOnly to true to return only dependency-ready tasks.")]
    public McpToolResponse<NextTaskPayload> GetNextTask(string? track = null, bool readyOnly = false,
        string? milestone = null)
    {
        var result = boardService.GetNextTask(new NextTaskQuery(
            NormalizeFilter(track),
            NormalizeFilter(milestone),
            readyOnly));
        if (!result.Success)
            return McpToolResponse<NextTaskPayload>.FromFailure(result);

        var next = result.Payload!;
        var payload = new NextTaskPayload(
            next.Found,
            next.Task == null ? null : ToTaskSummary(next.Task),
            next.Reason);

        return McpToolResponse<NextTaskPayload>.Ok(next.Reason, payload);
    }

    [McpServerTool(Name = "get_task", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns a task's metadata, current state, file path, markdown, description, and owning project. The optional project selector accepts current, parent, a stable project ID, or a unique alias.")]
    public async Task<McpToolResponse<TaskDetailPayload>> GetTask(
        string taskId,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project)) return GetLocalTask(taskId);

        var denied = LinkedReadDenied<TaskDetailPayload>(project, false);
        if (denied != null) return denied;
        var result = await linkedProjectReadService.GetTaskAsync(taskId, project, cancellationToken);
        if (!result.Success) return McpToolResponse<TaskDetailPayload>.FromFailure(result);
        var item = result.Payload!.Items.Single();
        return McpToolResponse<TaskDetailPayload>.Ok($"Task {item.Resource.Task.Id} loaded.",
            ToTaskDetailPayload(item.Resource, item.Owner, result.Payload.Warnings));
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
    public McpToolResponse<IReadOnlyList<MilestonePayload>> ListMilestones()
    {
        var project = GetProject();
        return project.Success
            ? McpToolResponse<IReadOnlyList<MilestonePayload>>.Ok(
                $"Returned {project.Data!.Milestones.Count} milestone(s).", project.Data.Milestones)
            : McpToolResponse<IReadOnlyList<MilestonePayload>>.Fail(project.ErrorCode!, project.Message!);
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

    [McpServerTool(Name = "update_task_metadata", Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Updates selected task metadata fields without replacing the full markdown file. Dependencies accept local task IDs or canonical pm://project/<project-id>/task/<task-id> references. Use priority inherit to clear a task override, none to explicitly suppress inherited priority, or low/medium/high/urgent to override.")]
    public McpToolResponse<TaskMutationPayload> UpdateTaskMetadata(
        string taskId,
        string? title = null,
        string? track = null,
        string? milestone = null,
        string? description = null,
        string? priority = null,
        IReadOnlyList<string>? dependsOn = null)
    {
        var result = taskService.PatchTaskMetadata(taskId, title, track, milestone, description, priority, dependsOn);
        if (!result.Success)
            return McpToolResponse<TaskMutationPayload>.FromFailure(result);

        var mutation = result.Payload!;
        return McpToolResponse<TaskMutationPayload>.Ok(
            mutation.Changed ? $"Updated task {taskId}." : $"Task {taskId} already matched.",
            new TaskMutationPayload(mutation.Changed, ToTaskDetailPayload(mutation.Task)));
    }

    [McpServerTool(Name = "append_task_note", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Appends a dated note under a task's Notes section.")]
    public McpToolResponse<TaskMutationPayload> AppendTaskNote(string taskId, string note)
    {
        if (!capabilityContext.CanAppendNoteTo(taskId))
            return McpToolResponse<TaskMutationPayload>.Fail(
                "mcp_task_scope_denied",
                $"The run-worker MCP profile may only append notes to task {capabilityContext.AssignedTaskId}.");

        var result = taskService.AppendTaskNote(taskId, note);
        if (!result.Success)
            return McpToolResponse<TaskMutationPayload>.FromFailure(result);

        var mutation = result.Payload!;
        return McpToolResponse<TaskMutationPayload>.Ok($"Appended note to task {taskId}.",
            new TaskMutationPayload(mutation.Changed, ToTaskDetailPayload(mutation.Task)));
    }

    [McpServerTool(Name = "reorder_tasks", Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Persists explicit task order for one track, state, and milestone scope.")]
    public McpToolResponse<TaskReorderPayload> ReorderTasks(
        string track,
        string state,
        IReadOnlyList<string> taskIds,
        string? milestone = null)
    {
        var result = taskService.ReorderTasks(track, state, taskIds, milestone);
        if (!result.Success)
            return McpToolResponse<TaskReorderPayload>.FromFailure(result);

        var reorder = result.Payload!;
        return McpToolResponse<TaskReorderPayload>.Ok(
            reorder.Changed ? "Task order updated." : "Task order already matched.",
            new TaskReorderPayload(
                reorder.Track,
                reorder.State,
                reorder.Milestone,
                reorder.TaskIds,
                reorder.Changed));
    }

    [McpServerTool(Name = "list_wiki_pages", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists wiki pages with ownership and optional linked-project selection or family scope.")]
    public async Task<McpToolResponse<WikiPageListPayload>> ListWikiPages(
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Read every available project in the linked family; cannot be combined with project.")]
        bool family = false,
        CancellationToken cancellationToken = default)
    {
        if (IsImplicitCurrent(project, family))
        {
            var local = wikiService.ListPages();
            if (!local.Success) return McpToolResponse<WikiPageListPayload>.FromFailure(local);
            var localPages = local.Payload!
                .Select(page => new WikiPageSummaryPayload(page.Path, page.Title, page.ModifiedAt, page.FilePath))
                .ToList();
            return McpToolResponse<WikiPageListPayload>.Ok(
                $"Returned {localPages.Count} wiki page(s).", new WikiPageListPayload(localPages));
        }

        var denied = LinkedReadDenied<WikiPageListPayload>(project, family);
        if (denied != null) return denied;
        var request = LinkedProjectReadRequest.FromOptions(project, family);
        if (!request.Success) return McpToolResponse<WikiPageListPayload>.FromFailure(request);
        var result = await linkedProjectReadService.ListWikiPagesAsync(request.Payload!, cancellationToken);
        if (!result.Success)
            return McpToolResponse<WikiPageListPayload>.FromFailure(result);

        var pages = result.Payload!.Items
            .Select(item => new WikiPageSummaryPayload(
                item.Resource.Path,
                item.Resource.Title,
                item.Resource.ModifiedAt,
                item.Resource.FilePath,
                ToOwner(item.Owner)))
            .ToList();

        return McpToolResponse<WikiPageListPayload>.Ok($"Returned {pages.Count} wiki page(s).",
            new WikiPageListPayload(pages, ToWarnings(result.Payload.Warnings), result.Payload.Truncated));
    }

    [McpServerTool(Name = "get_wiki_page", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns a wiki page's metadata, full markdown, body, and owning project. The optional project selector accepts current, parent, a stable project ID, or a unique alias.")]
    public async Task<McpToolResponse<WikiPagePayload>> GetWikiPage(
        string path,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            var local = wikiService.ReadPage(path);
            return local.Success
                ? McpToolResponse<WikiPagePayload>.Ok($"Wiki page {local.Payload!.Path} loaded.",
                    ToWikiPagePayload(local.Payload))
                : McpToolResponse<WikiPagePayload>.FromFailure(local);
        }

        var denied = LinkedReadDenied<WikiPagePayload>(project, false);
        if (denied != null) return denied;
        var result = await linkedProjectReadService.GetWikiPageAsync(path, project, cancellationToken);
        if (!result.Success) return McpToolResponse<WikiPagePayload>.FromFailure(result);
        var item = result.Payload!.Items.Single();
        return McpToolResponse<WikiPagePayload>.Ok($"Wiki page {item.Resource.Path} loaded.",
            ToWikiPagePayload(item.Resource, item.Owner, result.Payload.Warnings));
    }

    [McpServerTool(Name = "outline_wiki_page", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns a wiki page body version and ATX markdown heading outline for targeted patching.")]
    public McpToolResponse<WikiPageOutlinePayload> OutlineWikiPage(string path)
    {
        var result = wikiService.OutlinePage(path);
        return result.Success
            ? McpToolResponse<WikiPageOutlinePayload>.Ok($"Outlined wiki page {result.Payload!.Path}.",
                ToWikiPageOutlinePayload(result.Payload))
            : McpToolResponse<WikiPageOutlinePayload>.FromFailure(result);
    }

    [McpServerTool(Name = "search_wiki_pages", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Searches wiki page title, path, and body with optional linked-project selection or family scope.")]
    public async Task<McpToolResponse<WikiSearchPayload>> SearchWikiPages(
        string query,
        int limit = 20,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Search every available project in the linked family; cannot be combined with project.")]
        bool family = false,
        CancellationToken cancellationToken = default)
    {
        if (IsImplicitCurrent(project, family))
        {
            var local = wikiService.SearchPages(query, limit);
            if (!local.Success) return McpToolResponse<WikiSearchPayload>.FromFailure(local);
            var localPages = local.Payload!.Select(page => new WikiSearchResultPayload(
                page.Path, page.Title, page.ModifiedAt, page.FilePath, page.MatchCount, page.Snippet)).ToList();
            return McpToolResponse<WikiSearchPayload>.Ok(
                $"Returned {localPages.Count} wiki search result(s).", new WikiSearchPayload(localPages));
        }

        var denied = LinkedReadDenied<WikiSearchPayload>(project, family);
        if (denied != null) return denied;
        var request = LinkedProjectReadRequest.FromOptions(project, family);
        if (!request.Success) return McpToolResponse<WikiSearchPayload>.FromFailure(request);
        var result = await linkedProjectReadService.SearchWikiPagesAsync(
            query, limit, request.Payload, cancellationToken);
        if (!result.Success)
            return McpToolResponse<WikiSearchPayload>.FromFailure(result);

        var pages = result.Payload!.Items
            .Select(item => new WikiSearchResultPayload(
                item.Resource.Path,
                item.Resource.Title,
                item.Resource.ModifiedAt,
                item.Resource.FilePath,
                item.Resource.MatchCount,
                item.Resource.Snippet,
                ToOwner(item.Owner)))
            .ToList();
        return McpToolResponse<WikiSearchPayload>.Ok($"Returned {pages.Count} wiki search result(s).",
            new WikiSearchPayload(pages, ToWarnings(result.Payload.Warnings)));
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

    [McpServerTool(Name = "patch_wiki_page", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Applies a guarded body-only wiki patch under or around a heading from outline_wiki_page. Operation values are exposed by the tool schema enum.")]
    public McpToolResponse<WikiPagePatchPayload> PatchWikiPage(
        string path,
        string version,
        string headingId,
        [Description("Patch operation. Accepted values are represented by the schema enum.")]
        WikiPatchOperation operation,
        string markdown)
    {
        var result = wikiService.PatchPageSection(path, version, headingId, ToOperationValue(operation), markdown);
        return result.Success
            ? McpToolResponse<WikiPagePatchPayload>.Ok($"Patched wiki page {result.Payload!.Page.Path}.",
                new WikiPagePatchPayload(ToWikiPagePayload(result.Payload.Page), result.Payload.Version))
            : McpToolResponse<WikiPagePatchPayload>.FromFailure(result);
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
    public McpToolResponse<MutatedPayload> AddMilestone(string key, string title, string? priority = null)
    {
        var result = configService.AddMilestone(key, title, priority);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Added milestone {key}.", new MutatedPayload(true))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "set_milestone_priority", Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Sets a milestone priority to none, low, medium, high, or urgent.")]
    public McpToolResponse<MutatedPayload> SetMilestonePriority(string key, string priority)
    {
        var result = configService.SetMilestonePriority(key, priority);
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Updated milestone {key} priority.", new MutatedPayload(true))
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

    [McpServerTool(Name = "validate_project", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Validates task files, state refs, wiki pages, and stored task order data.")]
    public McpToolResponse<ProjectValidationPayload> ValidateProject()
    {
        var result = validationService.ValidateProject();
        if (!result.Success)
            return McpToolResponse<ProjectValidationPayload>.FromFailure(result);

        var validation = result.Payload!;
        var payload = new ProjectValidationPayload(
            validation.Valid,
            validation.Issues.Select(issue => new ProjectValidationIssuePayload(
                issue.Severity,
                issue.Code,
                issue.Message,
                issue.Path,
                issue.TaskId,
                issue.WikiPath,
                issue.State,
                issue.ProjectId,
                issue.ProjectAlias)).ToList());
        return McpToolResponse<ProjectValidationPayload>.Ok(
            validation.Valid && validation.Issues.Count == 0
                ? "Project validation passed."
                : validation.Valid
                    ? $"Project validation passed with {validation.Issues.Count} warning(s)."
                : $"Project validation found {validation.Issues.Count} issue(s).",
            payload);
    }

    private static IReadOnlyList<OptionPayload> ToOptions(IReadOnlyDictionary<string, string> options)
    {
        return options.Select(option => new OptionPayload(option.Key, option.Value)).ToList();
    }

    private static IReadOnlyList<MilestonePayload> ToMilestones(
        IReadOnlyDictionary<string, string> milestones,
        IReadOnlyDictionary<string, string> priorities)
    {
        return milestones
            .Select(milestone => new MilestonePayload(
                milestone.Key,
                milestone.Value,
                priorities.TryGetValue(milestone.Key, out var configured) &&
                PriorityLevel.TryNormalize(configured, out var priority)
                    ? priority
                    : PriorityLevel.None))
            .ToList();
    }

    private static TaskSummaryPayload ToTaskSummary(
        BoardTask task,
        LinkedProjectResourceOwner? owner = null)
    {
        return new TaskSummaryPayload(
            task.Task.Id,
            task.Task.Title,
            task.Track,
            task.Milestone,
            task.Priority,
            task.PrioritySource,
            task.State,
            task.Dependencies.DependsOn,
            task.Dependencies.Ready,
            task.Dependencies.Summary,
            task.Dependencies.WaitingOn,
            task.Dependencies.Missing,
            task.DescriptionPreview,
            task.FilePath,
            owner == null ? null : ToOwner(owner));
    }

    private static TaskSearchResultPayload ToTaskSearchResult(
        TaskSearchResult task,
        LinkedProjectResourceOwner? owner = null)
    {
        return new TaskSearchResultPayload(
            task.Task.Id,
            task.Task.Title,
            task.Track,
            task.Milestone,
            task.Priority,
            task.PrioritySource,
            task.State,
            task.Dependencies.DependsOn,
            task.Dependencies.Ready,
            task.Dependencies.Summary,
            task.Dependencies.WaitingOn,
            task.Dependencies.Missing,
            task.DescriptionPreview,
            task.FilePath,
            task.MatchCount,
            task.Snippet,
            owner == null ? null : ToOwner(owner));
    }

    private TaskDetailPayload ToTaskDetailPayload(TaskItem task)
    {
        var state = projectRoot.TryGetState(task, out var currentState) ? currentState : string.Empty;
        var markdown = projectRoot.TryReadTaskFile(task.Id, out var content) ? content : task.ToMarkdown();
        var priority = PriorityLevel.Resolve(projectRoot.Config!, task);
        var dependencies = boardService.GetDependencyStatus(task);
        return new TaskDetailPayload(
            task.Id,
            task.Title,
            projectRoot.ResolveTaskTrack(task),
            task.Milestone,
            priority.Priority,
            priority.Source,
            task.CreatedAt,
            task.ModifiedAt,
            state,
            dependencies.DependsOn,
            dependencies.Ready,
            dependencies.Summary,
            dependencies.WaitingOn,
            dependencies.Missing,
            projectRoot.GetTaskFilePath(task.Id),
            markdown,
            task.Description);
    }

    private McpToolResponse<TaskDetailPayload> GetLocalTask(string taskId)
    {
        var markdownResult = taskService.ReadTaskMarkdown(taskId);
        if (!markdownResult.Success)
            return McpToolResponse<TaskDetailPayload>.FromFailure(markdownResult);
        if (!projectRoot.TryGetById(taskId, out var task))
            return McpToolResponse<TaskDetailPayload>.Fail("missing_task", $"Task {taskId} not found.");

        return McpToolResponse<TaskDetailPayload>.Ok(
            $"Task {task.Id} loaded.", ToTaskDetailPayload(task));
    }

    private static TaskDetailPayload ToTaskDetailPayload(
        BoardTask task,
        LinkedProjectResourceOwner owner,
        IReadOnlyList<LinkedProjectFamilyWarning> warnings)
    {
        return new TaskDetailPayload(
            task.Task.Id,
            task.Task.Title,
            task.Track,
            task.Milestone,
            task.Priority,
            task.PrioritySource,
            task.Task.CreatedAt,
            task.Task.ModifiedAt,
            task.State,
            task.Dependencies.DependsOn,
            task.Dependencies.Ready,
            task.Dependencies.Summary,
            task.Dependencies.WaitingOn,
            task.Dependencies.Missing,
            task.FilePath,
            task.Markdown ?? task.Task.ToMarkdown(),
            task.Task.Description,
            ToOwner(owner),
            ToWarnings(warnings));
    }

    private static WikiPagePayload ToWikiPagePayload(
        WikiPageData page,
        LinkedProjectResourceOwner? owner = null,
        IReadOnlyList<LinkedProjectFamilyWarning>? warnings = null)
    {
        return new WikiPagePayload(
            page.Path,
            page.Title,
            page.CreatedAt,
            page.ModifiedAt,
            page.FilePath,
            page.Markdown,
            page.Body,
            owner == null ? null : ToOwner(owner),
            warnings == null ? null : ToWarnings(warnings));
    }

    private static WikiPageOutlinePayload ToWikiPageOutlinePayload(WikiPageOutlineData page)
    {
        return new WikiPageOutlinePayload(
            page.Path,
            page.Title,
            page.CreatedAt,
            page.ModifiedAt,
            page.FilePath,
            page.Version,
            page.Headings.Select(heading => new WikiHeadingOutlinePayload(
                    heading.Id,
                    heading.Level,
                    heading.Title,
                    heading.Breadcrumb,
                    heading.Preview))
                .ToList());
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsImplicitCurrent(string? project, bool family) =>
        !family && string.IsNullOrWhiteSpace(project);

    private McpToolResponse<T>? LinkedReadDenied<T>(string? project, bool family)
    {
        return capabilityContext.CanReadLinkedProjects(project, family)
            ? null
            : McpToolResponse<T>.Fail(
                "mcp_project_scope_denied",
                "The run-worker MCP profile may only read the current project.");
    }

    private static LinkedProjectOwnerPayload ToOwner(LinkedProjectResourceOwner owner) =>
        new(owner.ProjectId, owner.ProjectName, owner.Alias,
            LinkedProjectFamilyService.Format(owner.Relationship), owner.Revision, owner.Dirty);

    private static IReadOnlyList<LinkedProjectWarningPayload> ToWarnings(
        IReadOnlyList<LinkedProjectFamilyWarning> warnings) =>
        warnings.Select(warning => new LinkedProjectWarningPayload(
            warning.Code,
            warning.Message,
            warning.DeclaringProjectId,
            warning.TargetProjectId,
            warning.Alias,
            LinkedProjectFamilyService.Format(warning.Status),
            warning.RepairAction?.DisplayCommand)).ToList();

    private static ProjectMemberPayload ToMembershipPayload(ProjectMember member) =>
        new(member.UserId, member.DisplayName, member.PublicKey, member.Fingerprint, member.Role, member.IsLocal);

    private static ProjectInvitationPayload ToInvitationPayload(ProjectInvitation invitation) =>
        new(invitation.InvitationId, invitation.Role, invitation.CreatedByUserId,
            invitation.CreatedAt, invitation.ExpiresAt);

    private static McpToolResponse<T> MembershipUnavailable<T>() =>
        McpToolResponse<T>.Fail("membership_unavailable", "Project membership service is unavailable.");

    private static string ToOperationValue(WikiPatchOperation operation)
    {
        return operation switch
        {
            WikiPatchOperation.AppendToSection => "append_to_section",
            WikiPatchOperation.PrependToSection => "prepend_to_section",
            WikiPatchOperation.ReplaceSectionBody => "replace_section_body",
            WikiPatchOperation.InsertBeforeHeading => "insert_before_heading",
            WikiPatchOperation.InsertAfterSection => "insert_after_section",
            _ => string.Empty,
        };
    }
}
