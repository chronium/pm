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
    LinkedProjectMutationService linkedProjectMutations,
    IProjectMembershipService? membershipService,
    McpCapabilityContext capabilityContext,
    McpLinkedWikiContextStore? linkedWikiContexts = null)
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
    [Description("Returns project name, root path, states, tracks, milestones, and owning-project metadata. The optional project selector accepts current, parent, a stable project ID, or a unique alias.")]
    public async Task<ProjectScopedMcpToolResponse<ProjectPayload>> GetProject(
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ProjectScopedLinkedReadDenied<ProjectPayload>(project);
        if (denied != null) return denied;
        var result = await linkedProjectReadService.GetProjectAsync(project, cancellationToken);
        if (!result.Success) return ProjectScopedMcpToolResponse<ProjectPayload>.FromFailure(result);
        var item = result.Payload!.Items.Single();
        var projectPayload = ToProjectPayload(item.Resource);
        if (!projectPayload.Success)
            return ProjectScopedMcpToolResponse<ProjectPayload>.FromFailure(projectPayload);
        var payload = projectPayload.Payload!;
        return ProjectScopedMcpToolResponse<ProjectPayload>.Ok(
            $"Project {payload.Name} loaded.", payload, ToOwner(item.Owner), ToWarnings(result.Payload.Warnings));
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
    [Description("Lists tasks with optional track, milestone, state, linked-project selector, or family scope. Tasks assigned to delivered milestones are excluded unless includeDelivered is true.")]
    public async Task<McpToolResponse<TaskListPayload>> ListTasks(
        string? track = null,
        string? milestone = null,
        string? state = null,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Read across every available project in the linked family; cannot be combined with project.")]
        bool family = false,
        [Description("Include tasks assigned to delivered milestones.")]
        bool includeDelivered = false,
        CancellationToken cancellationToken = default)
    {
        if (IsLocalRead(project, family) &&
            (capabilityContext.Profile == McpCapabilityProfile.RunWorker ||
             !projectRoot.TryReadProjectId(out _)))
        {
            var local = boardService.GetBoard(new BoardQuery(
                NormalizeFilter(track), NormalizeFilter(milestone), NormalizeFilter(state), includeDelivered));
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
            new BoardQuery(
                NormalizeFilter(track),
                NormalizeFilter(milestone),
                NormalizeFilter(state),
                includeDelivered),
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
    [Description("Searches task IDs, metadata, dependencies, descriptions, and full markdown. Tasks assigned to delivered milestones are excluded unless includeDelivered is true. Supports optional linked-project selection or family scope. Structured predicates include state:, id:, track:, milestone:, and in:selection or in:all.")]
    public async Task<McpToolResponse<TaskSearchPayload>> SearchTasks(
        string query,
        int limit = 20,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Search every available project in the linked family; cannot be combined with project.")]
        bool family = false,
        [Description("Include tasks assigned to delivered milestones.")]
        bool includeDelivered = false,
        CancellationToken cancellationToken = default)
    {
        if (IsLocalRead(project, family) &&
            (capabilityContext.Profile == McpCapabilityProfile.RunWorker ||
             !projectRoot.TryReadProjectId(out _)))
        {
            var local = taskService.SearchTasks(
                query,
                limit,
                new TaskSearchContext(IncludeDelivered: includeDelivered));
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
            query,
            limit,
            request.Payload,
            new TaskSearchContext(IncludeDelivered: includeDelivered),
            cancellationToken);
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
    [Description("Returns one deterministic recommended actionable task with linked dependencies resolved. Optional project and family scopes broaden candidate selection; by default only active-project tasks are candidates.")]
    public async Task<McpToolResponse<NextTaskPayload>> GetNextTask(
        string? track = null,
        bool readyOnly = false,
        string? milestone = null,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Recommend across every available project in the linked family; cannot be combined with project.")]
        bool family = false,
        CancellationToken cancellationToken = default)
    {
        if (capabilityContext.Profile == McpCapabilityProfile.RunWorker && IsLocalRead(project, family))
        {
            var local = boardService.GetNextTask(new NextTaskQuery(
                NormalizeFilter(track), NormalizeFilter(milestone), readyOnly));
            if (!local.Success) return McpToolResponse<NextTaskPayload>.FromFailure(local);
            return McpToolResponse<NextTaskPayload>.Ok(local.Payload!.Reason, new NextTaskPayload(
                local.Payload.Found,
                local.Payload.Task == null ? null : ToTaskSummary(local.Payload.Task),
                local.Payload.Reason));
        }

        var denied = LinkedReadDenied<NextTaskPayload>(project, family);
        if (denied != null) return denied;
        var request = LinkedProjectReadRequest.FromOptions(project, family);
        if (!request.Success) return McpToolResponse<NextTaskPayload>.FromFailure(request);
        var result = await linkedProjectReadService.GetNextTaskAsync(
            request.Payload!,
            new NextTaskQuery(NormalizeFilter(track), NormalizeFilter(milestone), readyOnly),
            cancellationToken: cancellationToken);
        if (!result.Success) return McpToolResponse<NextTaskPayload>.FromFailure(result);

        var next = result.Payload!;
        var payload = new NextTaskPayload(
            next.Found,
            next.Task == null ? null : ToTaskSummary(next.Task, next.Owner),
            next.Reason,
            ToWarnings(next.Warnings));

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
        if (IsLocalRead(project, false) &&
            (capabilityContext.Profile == McpCapabilityProfile.RunWorker ||
             !projectRoot.TryReadProjectId(out _)))
            return GetLocalTask(taskId);

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
        var project = ToProjectPayload(projectRoot);
        return project.Success
            ? McpToolResponse<IReadOnlyList<OptionPayload>>.Ok(
                $"Returned {project.Payload!.Tracks.Count} track(s).", project.Payload.Tracks)
            : McpToolResponse<IReadOnlyList<OptionPayload>>.FromFailure(project);
    }

    [McpServerTool(Name = "list_milestones", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists undelivered milestones and owning-project metadata. Set includeDelivered to true to include delivered milestones. The optional project selector accepts current, parent, a stable project ID, or a unique alias.")]
    public async Task<ProjectScopedMcpToolResponse<IReadOnlyList<MilestonePayload>>> ListMilestones(
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        [Description("Include delivered milestones.")]
        bool includeDelivered = false,
        CancellationToken cancellationToken = default)
    {
        var denied = ProjectScopedLinkedReadDenied<IReadOnlyList<MilestonePayload>>(project);
        if (denied != null) return denied;
        var result = await linkedProjectReadService.GetProjectAsync(project, cancellationToken);
        if (!result.Success)
            return ProjectScopedMcpToolResponse<IReadOnlyList<MilestonePayload>>.FromFailure(result);
        var item = result.Payload!.Items.Single();
        var activation = new MilestoneActivationResolver(item.Resource).ResolveCurrentProject();
        if (!activation.Success)
            return ProjectScopedMcpToolResponse<IReadOnlyList<MilestonePayload>>.FromFailure(activation);
        var deliveredMilestoneKeys = DeliveredWorkVisibility.ResolveDeliveredMilestoneKeys(activation.Payload!);
        var milestones = ToMilestones(item.Resource.Config!.Milestones)
            .Where(milestone => DeliveredWorkVisibility.Includes(
                milestone.Key,
                includeDelivered,
                deliveredMilestoneKeys))
            .ToList();
        return ProjectScopedMcpToolResponse<IReadOnlyList<MilestonePayload>>.Ok(
            $"Returned {milestones.Count} milestone(s).",
            milestones,
            ToOwner(item.Owner),
            ToWarnings(result.Payload.Warnings));
    }

    [McpServerTool(Name = "get_activation_switchboard", ReadOnly = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns structured milestone deliverables, resolved activation triggers, provenance, current requirement status, validation issues, and owning-project metadata. The optional project selector accepts current, parent, a stable project ID, or a unique alias.")]
    public async Task<ProjectScopedMcpToolResponse<ActivationSwitchboardPayload>> GetActivationSwitchboard(
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ProjectScopedLinkedReadDenied<ActivationSwitchboardPayload>(project);
        if (denied != null) return denied;
        var selected = await linkedProjectReadService.GetProjectAsync(project, cancellationToken);
        if (!selected.Success)
            return ProjectScopedMcpToolResponse<ActivationSwitchboardPayload>.FromFailure(selected);
        var item = selected.Payload!.Items.Single();
        var result = ResolveActivationSwitchboard(item.Resource);
        return result.Success
            ? ProjectScopedMcpToolResponse<ActivationSwitchboardPayload>.Ok(
                $"Returned {result.Payload!.Milestones.Count} milestone(s) and " +
                $"{result.Payload.ActivationTriggers.Count} activation trigger(s).",
                result.Payload,
                ToOwner(item.Owner),
                ToWarnings(selected.Payload.Warnings))
            : ProjectScopedMcpToolResponse<ActivationSwitchboardPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "list_states", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists configured task states.")]
    public McpToolResponse<IReadOnlyList<OptionPayload>> ListStates()
    {
        var project = ToProjectPayload(projectRoot);
        return project.Success
            ? McpToolResponse<IReadOnlyList<OptionPayload>>.Ok(
                $"Returned {project.Payload!.States.Count} state(s).", project.Payload.States)
            : McpToolResponse<IReadOnlyList<OptionPayload>>.FromFailure(project);
    }

    [McpServerTool(Name = "create_task", Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Creates a task using track-scoped ID allocation.")]
    public async Task<McpToolResponse<CreatedTaskPayload>> CreateTask(
        string title,
        string track,
        string? milestone = null,
        string? description = null,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = await linkedProjectMutations.ExecuteAsync(
            project,
            (target, token) => target.Tasks.CreateTask(
                title, track, milestone, description ?? string.Empty, false, token),
            MutationAccess,
            cancellationToken);
        if (!result.Success)
            return McpToolResponse<CreatedTaskPayload>.FromFailure(result);

        var task = result.Payload!.Value;
        var payload = new CreatedTaskPayload(
            task.Id,
            task.Title,
            result.Payload.Receipt.ProjectId == ActiveProjectId
                ? projectRoot.ResolveTaskTrack(task)
                : task.Track ?? track,
            task.Milestone,
            result.Payload.Receipt.ChangedPaths.FirstOrDefault(path => path.EndsWith($"/{task.Id}.md", StringComparison.Ordinal))
            ?? $".pm/tasks/{task.Id}.md",
            ToReceipt(result.Payload.Receipt));

        return McpToolResponse<CreatedTaskPayload>.Ok($"Created task {task.Id}.", payload);
    }

    [McpServerTool(Name = "bulk_create_tasks_for_track", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates 1 to 100 tasks in one track using sequential track-scoped ID allocation.")]
    public async Task<McpToolResponse<BulkCreatedTasksPayload>> BulkCreateTasksForTrack(
        string track,
        IReadOnlyList<BulkTaskInputPayload> tasks,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = await linkedProjectMutations.ExecuteAsync(
            project,
            (target, token) => target.Tasks.BulkCreateTasksForTrack(
                track,
                tasks.Select(task => new BulkTaskCreateInput(task.Title, task.Description)).ToList(),
                token),
            MutationAccess,
            cancellationToken);
        if (!result.Success)
            return McpToolResponse<BulkCreatedTasksPayload>.FromFailure(result);

        var bulk = result.Payload!.Value;
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
            bulk.Failure == null ? null : new BulkFailurePayload(bulk.Failure.ErrorCode, bulk.Failure.Message),
            ToReceipt(result.Payload.Receipt));

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
        IReadOnlyList<string> taskIds,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Tasks.BulkAssignTasksToMilestone(milestone, taskIds),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        if (!result.Success)
            return McpToolResponse<BulkMilestoneAssignmentPayload>.FromFailure(result);

        var assignment = result.Payload!.Value;
        var payload = new BulkMilestoneAssignmentPayload(
            assignment.Milestone,
            assignment.TaskIds,
            assignment.FilePaths,
            assignment.RequestedCount,
            assignment.UpdatedCount,
            ToReceipt(result.Payload.Receipt));

        return McpToolResponse<BulkMilestoneAssignmentPayload>.Ok(
            $"Assigned {assignment.RequestedCount} task(s) to {assignment.Milestone}.",
            payload);
    }

    [McpServerTool(Name = "move_task", Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Moves a task to the target state.")]
    public McpToolResponse<MutatedPayload> MoveTask(
        string taskId,
        string targetState,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Tasks.MoveTask(taskId, targetState),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Moved task {taskId} to {targetState}.",
                new MutatedPayload(
                    true,
                    ToReceipt(result.Payload!.Receipt),
                    ToReleaseTransition(result.Payload.Value.ReleaseTransition)))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "remove_task", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Permanently removes a task and its state reference.")]
    public McpToolResponse<MutatedPayload> RemoveTask(
        string taskId,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => ToPayload(target.Tasks.RemoveTask(taskId)),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Removed task {taskId}.",
                new MutatedPayload(true, ToReceipt(result.Payload!.Receipt)))
            : McpToolResponse<MutatedPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "update_task_markdown", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Replaces a task markdown file after validating the task ID is unchanged.")]
    public McpToolResponse<MutatedPayload> UpdateTaskMarkdown(
        string taskId,
        string markdown,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => ToPayload(target.Tasks.SaveEditedTaskContent(taskId, markdown)),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Updated task {taskId}.",
                new MutatedPayload(true, ToReceipt(result.Payload!.Receipt)))
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
        IReadOnlyList<string>? dependsOn = null,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Tasks.PatchTaskMetadata(
                taskId, title, track, milestone, description, priority, dependsOn),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        if (!result.Success)
            return McpToolResponse<TaskMutationPayload>.FromFailure(result);

        var mutation = result.Payload!;
        var task = ReadTaskAfterMutation(taskId, mutation.Receipt, cancellationToken);
        if (!task.Success)
            return McpToolResponse<TaskMutationPayload>.FromFailure(task);

        return McpToolResponse<TaskMutationPayload>.Ok(
            mutation.Value.Changed ? $"Updated task {taskId}." : $"Task {taskId} already matched.",
            new TaskMutationPayload(
                mutation.Value.Changed,
                task.Payload!,
                ToReceipt(mutation.Receipt)));
    }

    [McpServerTool(Name = "append_task_note", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Appends a dated note under a task's Notes section.")]
    public McpToolResponse<TaskMutationPayload> AppendTaskNote(
        string taskId,
        string note,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        if (!capabilityContext.CanAppendNoteTo(taskId))
            return McpToolResponse<TaskMutationPayload>.Fail(
                "mcp_task_scope_denied",
                $"The run-worker MCP profile may only append notes to task {capabilityContext.AssignedTaskId}.");

        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Tasks.AppendTaskNote(taskId, note),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        if (!result.Success)
            return McpToolResponse<TaskMutationPayload>.FromFailure(result);

        var mutation = result.Payload!;
        var task = ReadTaskAfterMutation(taskId, mutation.Receipt, cancellationToken);
        if (!task.Success)
            return McpToolResponse<TaskMutationPayload>.FromFailure(task);

        return McpToolResponse<TaskMutationPayload>.Ok($"Appended note to task {taskId}.",
            new TaskMutationPayload(
                mutation.Value.Changed,
                task.Payload!,
                ToReceipt(mutation.Receipt)));
    }

    [McpServerTool(Name = "reorder_tasks", Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Persists explicit task order for one track, state, and milestone scope.")]
    public McpToolResponse<TaskReorderPayload> ReorderTasks(
        string track,
        string state,
        IReadOnlyList<string> taskIds,
        string? milestone = null,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Tasks.ReorderTasks(track, state, taskIds, milestone),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        if (!result.Success)
            return McpToolResponse<TaskReorderPayload>.FromFailure(result);

        var reorder = result.Payload!.Value;
        return McpToolResponse<TaskReorderPayload>.Ok(
            reorder.Changed ? "Task order updated." : "Task order already matched.",
            new TaskReorderPayload(
                reorder.Track,
                reorder.State,
                reorder.Milestone,
                reorder.TaskIds,
                reorder.Changed,
                ToReceipt(result.Payload.Receipt)));
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
        if (IsLocalRead(project, family))
        {
            var local = wikiService.ListPages();
            if (!local.Success) return McpToolResponse<WikiPageListPayload>.FromFailure(local);
            var localPages = local.Payload!
                .Select(page => new WikiPageSummaryPayload(page.Path, page.Title, page.ModifiedAt, page.FilePath))
                .ToList();
            return McpToolResponse<WikiPageListPayload>.Ok(
                $"Returned {localPages.Count} wiki page(s).", new WikiPageListPayload(localPages));
        }

        var denied = LinkedWikiReadDenied<WikiPageListPayload>(project, family);
        if (denied != null) return denied;
        if (capabilityContext.Profile == McpCapabilityProfile.RunWorker && linkedWikiContexts?.Configured == true)
        {
            var scoped = linkedWikiContexts.List(project, family);
            if (!scoped.Success) return McpToolResponse<WikiPageListPayload>.FromFailure(scoped);
            var scopedPages = scoped.Payload!.Items.Select(item => new WikiPageSummaryPayload(
                item.Resource.Path, item.Resource.Title, item.Resource.ModifiedAt,
                item.Resource.FilePath, ToOwner(item.Owner))).ToList();
            if (family)
            {
                var local = wikiService.ListPages();
                if (!local.Success) return McpToolResponse<WikiPageListPayload>.FromFailure(local);
                scopedPages.InsertRange(0, local.Payload!.Select(page => new WikiPageSummaryPayload(
                    page.Path, page.Title, page.ModifiedAt, page.FilePath)));
            }
            return McpToolResponse<WikiPageListPayload>.Ok(
                $"Returned {scopedPages.Count} wiki page(s).", new WikiPageListPayload(scopedPages));
        }
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
        if (IsLocalRead(project, false))
        {
            var local = wikiService.ReadPage(path);
            return local.Success
                ? McpToolResponse<WikiPagePayload>.Ok($"Wiki page {local.Payload!.Path} loaded.",
                    ToWikiPagePayload(local.Payload))
                : McpToolResponse<WikiPagePayload>.FromFailure(local);
        }

        var denied = LinkedWikiReadDenied<WikiPagePayload>(project, false);
        if (denied != null) return denied;
        if (capabilityContext.Profile == McpCapabilityProfile.RunWorker && linkedWikiContexts?.Configured == true)
        {
            var scoped = linkedWikiContexts.Get(path, project!);
            if (!scoped.Success) return McpToolResponse<WikiPagePayload>.FromFailure(scoped);
            var scopedItem = scoped.Payload!.Items.Single();
            return McpToolResponse<WikiPagePayload>.Ok($"Wiki page {scopedItem.Resource.Path} loaded.",
                ToWikiPagePayload(scopedItem.Resource, scopedItem.Owner, []));
        }
        var result = await linkedProjectReadService.GetWikiPageAsync(path, project, cancellationToken);
        if (!result.Success) return McpToolResponse<WikiPagePayload>.FromFailure(result);
        var item = result.Payload!.Items.Single();
        return McpToolResponse<WikiPagePayload>.Ok($"Wiki page {item.Resource.Path} loaded.",
            ToWikiPagePayload(item.Resource, item.Owner, result.Payload.Warnings));
    }

    [McpServerTool(Name = "outline_wiki_page", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Returns a wiki page body version and ATX markdown heading outline for targeted patching, with optional linked-project selection.")]
    public async Task<McpToolResponse<WikiPageOutlinePayload>> OutlineWikiPage(
        string path,
        [Description("Select current, parent, a stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        if (IsLocalRead(project, false))
        {
            var local = wikiService.OutlinePage(path);
            return local.Success
                ? McpToolResponse<WikiPageOutlinePayload>.Ok($"Outlined wiki page {local.Payload!.Path}.",
                    ToWikiPageOutlinePayload(local.Payload))
                : McpToolResponse<WikiPageOutlinePayload>.FromFailure(local);
        }

        var denied = LinkedWikiReadDenied<WikiPageOutlinePayload>(project, false);
        if (denied != null) return denied;
        if (capabilityContext.Profile == McpCapabilityProfile.RunWorker && linkedWikiContexts?.Configured == true)
        {
            var scoped = linkedWikiContexts.Outline(path, project!);
            if (!scoped.Success) return McpToolResponse<WikiPageOutlinePayload>.FromFailure(scoped);
            var scopedItem = scoped.Payload!.Items.Single();
            return McpToolResponse<WikiPageOutlinePayload>.Ok(
                $"Outlined wiki page {scopedItem.Resource.Path}.",
                ToWikiPageOutlinePayload(scopedItem.Resource, scopedItem.Owner, []));
        }

        var result = await linkedProjectReadService.OutlineWikiPageAsync(path, project, cancellationToken);
        if (!result.Success) return McpToolResponse<WikiPageOutlinePayload>.FromFailure(result);
        var item = result.Payload!.Items.Single();
        return McpToolResponse<WikiPageOutlinePayload>.Ok(
            $"Outlined wiki page {item.Resource.Path}.",
            ToWikiPageOutlinePayload(item.Resource, item.Owner, result.Payload.Warnings));
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
        if (IsLocalRead(project, family))
        {
            var local = wikiService.SearchPages(query, limit);
            if (!local.Success) return McpToolResponse<WikiSearchPayload>.FromFailure(local);
            var localPages = local.Payload!.Select(page => new WikiSearchResultPayload(
                page.Path, page.Title, page.ModifiedAt, page.FilePath, page.MatchCount, page.Snippet)).ToList();
            return McpToolResponse<WikiSearchPayload>.Ok(
                $"Returned {localPages.Count} wiki search result(s).", new WikiSearchPayload(localPages));
        }

        var denied = LinkedWikiReadDenied<WikiSearchPayload>(project, family);
        if (denied != null) return denied;
        if (capabilityContext.Profile == McpCapabilityProfile.RunWorker && linkedWikiContexts?.Configured == true)
        {
            var scoped = linkedWikiContexts.Search(query, limit, project, family);
            if (!scoped.Success) return McpToolResponse<WikiSearchPayload>.FromFailure(scoped);
            var scopedPages = scoped.Payload!.Items.Select(item => new WikiSearchResultPayload(
                item.Resource.Path, item.Resource.Title, item.Resource.ModifiedAt, item.Resource.FilePath,
                item.Resource.MatchCount, item.Resource.Snippet, ToOwner(item.Owner))).ToList();
            if (family)
            {
                var local = wikiService.SearchPages(query, limit);
                if (!local.Success) return McpToolResponse<WikiSearchPayload>.FromFailure(local);
                scopedPages.AddRange(local.Payload!.Select(page => new WikiSearchResultPayload(
                    page.Path, page.Title, page.ModifiedAt, page.FilePath, page.MatchCount, page.Snippet)));
                scopedPages = scopedPages.OrderByDescending(page => page.MatchCount)
                    .ThenBy(page => page.Path, StringComparer.Ordinal).Take(Math.Clamp(limit, 1, 100)).ToList();
            }
            return McpToolResponse<WikiSearchPayload>.Ok(
                $"Returned {scopedPages.Count} wiki search result(s).", new WikiSearchPayload(scopedPages));
        }
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
    public McpToolResponse<WikiPagePayload> CreateWikiPage(
        string path,
        string title,
        string body = "",
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Wiki.CreatePage(path, title, body),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<WikiPagePayload>.Ok($"Created wiki page {result.Payload!.Value.Path}.",
                ToWikiPagePayload(result.Payload.Value) with { Mutation = ToReceipt(result.Payload.Receipt) })
            : McpToolResponse<WikiPagePayload>.FromFailure(result);
    }

    [McpServerTool(Name = "update_wiki_page_markdown", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Replaces a wiki markdown file after validating frontmatter.")]
    public McpToolResponse<WikiPagePayload> UpdateWikiPageMarkdown(
        string path,
        string markdown,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Wiki.UpdatePageMarkdown(path, markdown),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<WikiPagePayload>.Ok($"Updated wiki page {result.Payload!.Value.Path}.",
                ToWikiPagePayload(result.Payload.Value) with { Mutation = ToReceipt(result.Payload.Receipt) })
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
        string markdown,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Wiki.PatchPageSection(
                path, version, headingId, ToOperationValue(operation), markdown),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<WikiPagePatchPayload>.Ok($"Patched wiki page {result.Payload!.Value.Page.Path}.",
                new WikiPagePatchPayload(
                    ToWikiPagePayload(result.Payload.Value.Page),
                    result.Payload.Value.Version,
                    ToReceipt(result.Payload.Receipt)))
            : McpToolResponse<WikiPagePatchPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "rename_wiki_page", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Renames a wiki page path, title, or both while preserving body and created timestamp.")]
    public McpToolResponse<WikiPagePayload> RenameWikiPage(
        string path,
        string newPath,
        string title,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => target.Wiki.RenamePage(path, newPath, title),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<WikiPagePayload>.Ok($"Renamed wiki page {result.Payload!.Value.Path}.",
                ToWikiPagePayload(result.Payload.Value) with { Mutation = ToReceipt(result.Payload.Receipt) })
            : McpToolResponse<WikiPagePayload>.FromFailure(result);
    }

    [McpServerTool(Name = "remove_wiki_page", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Permanently removes one wiki page.")]
    public McpToolResponse<MutatedPayload> RemoveWikiPage(
        string path,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var result = linkedProjectMutations.ExecuteAsync(
            project,
            target => ToPayload(target.Wiki.RemovePage(path)),
            MutationAccess,
            cancellationToken).GetAwaiter().GetResult();
        return result.Success
            ? McpToolResponse<MutatedPayload>.Ok($"Removed wiki page {path}.",
                new MutatedPayload(true, ToReceipt(result.Payload!.Receipt)))
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
    [Description("Adds a structured milestone deliverable to the selected write-trusted project.")]
    public Task<McpToolResponse<ActivationMutationPayload>> AddMilestone(
        string key,
        string title,
        string? priority = null,
        string? description = null,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => ToPayload(target.Config.AddMilestone(key, title, priority, description)),
            _ => $"Added milestone {key}.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "set_milestone_priority", Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Sets a milestone priority to none, low, medium, high, or urgent.")]
    public Task<McpToolResponse<ActivationMutationPayload>> SetMilestonePriority(
        string key,
        string priority,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => ToPayload(target.Config.SetMilestonePriority(key, priority)),
            _ => $"Updated milestone {key} priority.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "set_milestone_description", Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Sets the Markdown deliverable description for a milestone in the selected write-trusted project.")]
    public Task<McpToolResponse<ActivationMutationPayload>> SetMilestoneDescription(
        string key,
        string description,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => ToPayload(target.Config.SetMilestoneDescription(key, description)),
            _ => $"Updated milestone {key} description.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "remove_milestone", Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes an unused milestone.")]
    public Task<McpToolResponse<ActivationMutationPayload>> RemoveMilestone(
        string key,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => ToPayload(target.Config.RemoveMilestone(key)),
            _ => $"Removed milestone {key}.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "rename_milestone", Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Renames a milestone title without changing its key.")]
    public Task<McpToolResponse<ActivationMutationPayload>> RenameMilestone(
        string key,
        string title,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => ToPayload(target.Config.RenameMilestone(key, title)),
            _ => $"Renamed milestone {key}.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "add_activation_trigger", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Adds a reusable activation trigger definition to the selected write-trusted project.")]
    public async Task<McpToolResponse<ActivationMutationPayload>> AddActivationTrigger(
        string key,
        string title,
        IReadOnlyList<ActivationRequirementInputPayload> requirements,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ActivationMutationPayload>();
        if (denied != null) return denied;
        var parsed = ToActivationRequirements(requirements);
        if (!parsed.Success) return McpToolResponse<ActivationMutationPayload>.FromFailure(parsed);

        return await ExecuteTrustedTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.AddTrigger(key, title, parsed.Payload!),
            _ => $"Added activation trigger {key}.",
            result => new ActivationMutationDetailsPayload(result.AffectedMilestones),
            cancellationToken);
    }

    [McpServerTool(Name = "rename_activation_trigger", Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Renames an activation trigger without changing its key or requirements.")]
    public Task<McpToolResponse<ActivationMutationPayload>> RenameActivationTrigger(
        string key,
        string title,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.RenameTrigger(key, title),
            _ => $"Renamed activation trigger {key}.",
            result => new ActivationMutationDetailsPayload(result.AffectedMilestones),
            cancellationToken);

    [McpServerTool(Name = "remove_activation_trigger", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Removes an activation trigger that is not required by a milestone.")]
    public Task<McpToolResponse<ActivationMutationPayload>> RemoveActivationTrigger(
        string key,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.RemoveTrigger(key),
            _ => $"Removed activation trigger {key}.",
            result => new ActivationMutationDetailsPayload(result.AffectedMilestones),
            cancellationToken);

    [McpServerTool(Name = "set_activation_trigger_requirements", Destructive = true, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Replaces the requirements of an inactive activation trigger; an empty list makes it manual-only.")]
    public async Task<McpToolResponse<ActivationMutationPayload>> SetActivationTriggerRequirements(
        string key,
        IReadOnlyList<ActivationRequirementInputPayload> requirements,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ActivationMutationPayload>();
        if (denied != null) return denied;
        var parsed = ToActivationRequirements(requirements);
        if (!parsed.Success) return McpToolResponse<ActivationMutationPayload>.FromFailure(parsed);

        return await ExecuteTrustedTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.SetRequirements(key, parsed.Payload!),
            _ => $"Updated activation trigger {key} requirements.",
            result => new ActivationMutationDetailsPayload(result.AffectedMilestones),
            cancellationToken);
    }

    [McpServerTool(Name = "attach_activation_trigger_to_milestone", Destructive = false,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Makes a milestone require an activation trigger.")]
    public Task<McpToolResponse<ActivationMutationPayload>> AttachActivationTriggerToMilestone(
        string key,
        string milestone,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.AttachTrigger(key, milestone),
            _ => $"Attached activation trigger {key} to milestone {milestone}.",
            result => new ActivationMutationDetailsPayload(result.AffectedMilestones),
            cancellationToken);

    [McpServerTool(Name = "detach_activation_trigger_from_milestone", Destructive = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Removes an activation trigger requirement from a milestone.")]
    public Task<McpToolResponse<ActivationMutationPayload>> DetachActivationTriggerFromMilestone(
        string key,
        string milestone,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.DetachTrigger(key, milestone),
            _ => $"Detached activation trigger {key} from milestone {milestone}.",
            result => new ActivationMutationDetailsPayload(result.AffectedMilestones),
            cancellationToken);

    [McpServerTool(Name = "preview_activation_trigger_redefinition", ReadOnly = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Previews an active trigger requirement redefinition in the selected write-trusted project and returns the revision required to apply it.")]
    public async Task<McpToolResponse<ActivationTriggerRedefinitionPreviewPayload>> PreviewActivationTriggerRedefinition(
        string key,
        IReadOnlyList<ActivationRequirementInputPayload> requirements,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ActivationTriggerRedefinitionPreviewPayload>();
        if (denied != null) return denied;
        var parsed = ToActivationRequirements(requirements);
        if (!parsed.Success)
            return McpToolResponse<ActivationTriggerRedefinitionPreviewPayload>.FromFailure(parsed);

        var target = await linkedProjectMutations.ResolveTargetAsync(project, MutationAccess, cancellationToken);
        if (!target.Success)
            return McpToolResponse<ActivationTriggerRedefinitionPreviewPayload>.FromFailure(target);
        var result = target.Payload!.ActivationTriggers.PreviewRedefinition(key, parsed.Payload!);
        return result.Success
            ? McpToolResponse<ActivationTriggerRedefinitionPreviewPayload>.Ok(
                $"Previewed activation trigger {key} redefinition.",
                ToRedefinitionPreview(result.Payload!))
            : McpToolResponse<ActivationTriggerRedefinitionPreviewPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "redefine_activation_trigger", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Replaces an active trigger definition using a preview revision and explicit eligibility-loss confirmation.")]
    public async Task<McpToolResponse<ActivationMutationPayload>> RedefineActivationTrigger(
        string key,
        IReadOnlyList<ActivationRequirementInputPayload> requirements,
        string expectedRevision,
        bool allowDeactivation = false,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ActivationMutationPayload>();
        if (denied != null) return denied;
        var parsed = ToActivationRequirements(requirements);
        if (!parsed.Success) return McpToolResponse<ActivationMutationPayload>.FromFailure(parsed);

        return await ExecuteTrustedTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.RedefineTrigger(
                key, parsed.Payload!, expectedRevision, allowDeactivation),
            _ => $"Redefined activation trigger {key}.",
            result => new ActivationMutationDetailsPayload(result.AffectedMilestones),
            cancellationToken);
    }

    [McpServerTool(Name = "activate_activation_trigger", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Manually activates a manual-only activation trigger in the selected write-trusted project.")]
    public Task<McpToolResponse<ActivationMutationPayload>> ActivateActivationTrigger(
        string key,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.ActivateTrigger(key, null),
            _ => $"Activated activation trigger {key}.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "override_activation_trigger", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Activates a trigger with unmet requirements and records the override reason and waived requirements.")]
    public Task<McpToolResponse<ActivationMutationPayload>> OverrideActivationTrigger(
        string key,
        string reason,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.ActivateTrigger(key, reason),
            _ => $"Overrode activation trigger {key}.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "reset_activation_trigger", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Removes an activation record when the trigger's current requirements are not all satisfied.")]
    public Task<McpToolResponse<ActivationMutationPayload>> ResetActivationTrigger(
        string key,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.ResetTrigger(key),
            _ => $"Reset activation trigger {key}.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "reconcile_activation_triggers", Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Latches inactive triggers whose requirements are satisfied; dry-run reports impact without writing.")]
    public Task<McpToolResponse<ActivationMutationPayload>> ReconcileActivationTriggers(
        bool dryRun = false,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.ActivationTriggers.Reconcile(dryRun),
            result => result.DryRun
                ? "Previewed automatic activation reconciliation."
                : "Reconciled automatic activations.",
            result => new ActivationMutationDetailsPayload(
                AutomaticActivation: ToAutomaticActivationImpact(result.ActivationImpact)),
            cancellationToken);

    [McpServerTool(Name = "preview_milestone_delivery", ReadOnly = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Previews milestone delivery in the selected readable project and returns the revision required to deliver it.")]
    public async Task<McpToolResponse<MilestoneDeliveryPreviewPayload>> PreviewMilestoneDelivery(
        string key,
        string? reason = null,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<MilestoneDeliveryPreviewPayload>();
        if (denied != null) return denied;

        var target = await linkedProjectMutations.ResolveTargetAsync(
            project,
            LinkedProjectTargetAccess.ReadableLinkedProjects,
            cancellationToken);
        if (!target.Success)
            return McpToolResponse<MilestoneDeliveryPreviewPayload>.FromFailure(target);
        var result = target.Payload!.MilestoneDeliveries.PreviewDelivery(key, reason);
        return result.Success
            ? McpToolResponse<MilestoneDeliveryPreviewPayload>.Ok(
                $"Previewed milestone {key} delivery.", ToMilestoneDeliveryPreview(result.Payload!))
            : McpToolResponse<MilestoneDeliveryPreviewPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "deliver_milestone", Destructive = false, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Delivers a milestone using a preview revision and explicit exceptional-delivery confirmation.")]
    public Task<McpToolResponse<ActivationMutationPayload>> DeliverMilestone(
        string key,
        string expectedRevision,
        string? reason = null,
        bool allowExceptional = false,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.MilestoneDeliveries.DeliverMilestone(
                key, reason, expectedRevision, allowExceptional),
            _ => $"Delivered milestone {key}.",
            result => new ActivationMutationDetailsPayload(
                AutomaticActivation: ToAutomaticActivationImpact(result.ActivationImpact),
                ReleaseTransition: ToReleaseTransition(result.ReleaseTransition)),
            cancellationToken);

    [McpServerTool(Name = "reopen_milestone", Destructive = true, OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Removes a milestone delivery record and re-evaluates its activation lifecycle.")]
    public Task<McpToolResponse<ActivationMutationPayload>> ReopenMilestone(
        string key,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default) =>
        ExecuteTargetActivationMutationAsync(
            project,
            target => target.MilestoneDeliveries.ReopenMilestone(key),
            _ => $"Reopened milestone {key}.",
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_release_status", ReadOnly = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns release version, pending reconciliation, and latest evidence for the selected project.")]
    public async Task<McpToolResponse<ReleaseStatusPayload>> GetReleaseStatus(
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ReleaseStatusPayload>();
        if (denied != null) return denied;
        var target = await linkedProjectMutations.ResolveTargetAsync(
            project, LinkedProjectTargetAccess.ReadableLinkedProjects, cancellationToken);
        if (!target.Success) return McpToolResponse<ReleaseStatusPayload>.FromFailure(target);
        var result = target.Payload!.Releases.ReadStatus();
        return result.Success
            ? McpToolResponse<ReleaseStatusPayload>.Ok(
                result.Payload!.Enabled ? $"Release {result.Payload.Version} loaded." : "Release versioning is disabled.",
                new ReleaseStatusPayload(
                    result.Payload.Enabled,
                    result.Payload.Version?.ToString(),
                    ToReleaseTransition(result.Payload.PendingTransition),
                    ToReleaseTransition(result.Payload.LatestTransition)))
            : McpToolResponse<ReleaseStatusPayload>.FromFailure(result);
    }

    [McpServerTool(Name = "reconcile_release_version", Destructive = false, Idempotent = true,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Completes or clears the selected project's pending release transition; dry-run previews recovery.")]
    public async Task<McpToolResponse<ReleaseReconciliationPayload>> ReconcileReleaseVersion(
        bool dryRun = false,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ReleaseReconciliationPayload>();
        if (denied != null) return denied;
        var result = await linkedProjectMutations.ExecuteAsync(
            project, target => target.Releases.Reconcile(dryRun), MutationAccess, cancellationToken);
        if (!result.Success) return McpToolResponse<ReleaseReconciliationPayload>.FromFailure(result);
        return McpToolResponse<ReleaseReconciliationPayload>.Ok(
            result.Payload!.Value.Changed
                ? $"Release reconciliation {result.Payload.Value.Action}."
                : "No release transition is pending.",
            new ReleaseReconciliationPayload(
                result.Payload.Value.Changed,
                result.Payload.Value.Action,
                ToReleaseTransition(result.Payload.Value.Transition),
                result.Payload.Receipt.ChangedPaths.Count == 0 ? null : ToReceipt(result.Payload.Receipt)));
    }

    [McpServerTool(Name = "preview_major_version", ReadOnly = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Previews the explicit next-major release transition and returns its required revision.")]
    public async Task<McpToolResponse<MajorReleasePreviewPayload>> PreviewMajorVersion(
        string reason,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<MajorReleasePreviewPayload>();
        if (denied != null) return denied;
        var target = await linkedProjectMutations.ResolveTargetAsync(
            project, LinkedProjectTargetAccess.ReadableLinkedProjects, cancellationToken);
        if (!target.Success) return McpToolResponse<MajorReleasePreviewPayload>.FromFailure(target);
        var preview = target.Payload!.Releases.PreviewMajor(reason);
        return preview.Success
            ? McpToolResponse<MajorReleasePreviewPayload>.Ok(
                $"Previewed major release {preview.Payload!.Transition.ToVersion}.",
                new MajorReleasePreviewPayload(
                    preview.Payload.Revision,
                    ToReleaseTransition(preview.Payload.Transition)!))
            : McpToolResponse<MajorReleasePreviewPayload>.FromFailure(preview);
    }

    [McpServerTool(Name = "advance_major_version", Destructive = false,
        OpenWorld = false, UseStructuredContent = true)]
    [Description("Advances the selected project to its next major version using a current preview revision.")]
    public async Task<McpToolResponse<ReleaseReconciliationPayload>> AdvanceMajorVersion(
        string reason,
        string expectedRevision,
        [Description("Select current, parent, an exact stable project ID, or a unique linked-project alias.")]
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ReleaseReconciliationPayload>();
        if (denied != null) return denied;
        var result = await linkedProjectMutations.ExecuteAsync(
            project,
            target =>
            {
                var preview = target.Releases.PreviewMajor(reason);
                if (!preview.Success)
                    return AppResult<ReleaseVersionTransition>.Fail(preview.ErrorCode!, preview.Message!);
                if (!string.Equals(preview.Payload!.Revision, expectedRevision, StringComparison.Ordinal))
                    return AppResult<ReleaseVersionTransition>.Fail(
                        "release_major_stale", "Major release conditions changed. Preview the major version again.");
                var begin = target.Releases.Begin(preview.Payload);
                if (!begin.Success)
                    return AppResult<ReleaseVersionTransition>.Fail(begin.ErrorCode!, begin.Message!);
                var complete = target.Releases.Complete(preview.Payload);
                if (!complete.Success) _ = target.Releases.Rollback(preview.Payload);
                return complete;
            },
            MutationAccess,
            cancellationToken);
        if (!result.Success) return McpToolResponse<ReleaseReconciliationPayload>.FromFailure(result);
        return McpToolResponse<ReleaseReconciliationPayload>.Ok(
            $"Advanced release to {result.Payload!.Value.ToVersion}.",
            new ReleaseReconciliationPayload(
                true,
                "advanced-major",
                ToReleaseTransition(result.Payload.Value),
                ToReceipt(result.Payload.Receipt)));
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

    private static IReadOnlyList<MilestonePayload> ToMilestones(
        IReadOnlyDictionary<string, MilestoneDefinition> milestones)
    {
        return milestones
            .Select(milestone => new MilestonePayload(
                milestone.Key,
                milestone.Value.Title,
                PriorityLevel.TryNormalize(milestone.Value.Priority, out var priority)
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
            task.Dependencies.Completed,
            task.Dependencies.Unavailable,
            task.Dependencies.Invalid,
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
            task.Dependencies.Completed,
            task.Dependencies.Unavailable,
            task.Dependencies.Invalid,
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
            dependencies.Completed,
            dependencies.Unavailable,
            dependencies.Invalid,
            projectRoot.GetTaskFilePath(task.Id),
            markdown,
            task.Description);
    }

    private AppResult<TaskDetailPayload> ReadTaskAfterMutation(
        string taskId,
        ProjectMutationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var selector = string.Equals(receipt.ProjectId, ActiveProjectId, StringComparison.Ordinal) ||
                       string.Equals(receipt.ProjectId, "current", StringComparison.OrdinalIgnoreCase)
            ? null
            : receipt.ProjectId;
        var task = GetTask(taskId, selector, cancellationToken).GetAwaiter().GetResult();
        return task.Success
            ? AppResult<TaskDetailPayload>.Ok(task.Data!)
            : AppResult<TaskDetailPayload>.Fail(task.ErrorCode!, task.Message!);
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
        LinkedProjectResourceOwner? owner,
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
            task.Dependencies.Completed,
            task.Dependencies.Unavailable,
            task.Dependencies.Invalid,
            task.FilePath,
            task.Markdown ?? task.Task.ToMarkdown(),
            task.Task.Description,
            owner == null ? null : ToOwner(owner),
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

    private static WikiPageOutlinePayload ToWikiPageOutlinePayload(
        WikiPageOutlineData page,
        LinkedProjectResourceOwner? owner = null,
        IReadOnlyList<LinkedProjectFamilyWarning>? warnings = null)
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
                .ToList(),
            owner == null ? null : ToOwner(owner),
            warnings == null ? null : ToWarnings(warnings));
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsImplicitCurrent(string? project, bool family) =>
        !family && string.IsNullOrWhiteSpace(project);

    private bool IsLocalRead(string? project, bool family) =>
        IsImplicitCurrent(project, family) ||
        capabilityContext.Profile == McpCapabilityProfile.RunWorker && !family &&
        string.Equals(project?.Trim(), "current", StringComparison.OrdinalIgnoreCase);

    private McpToolResponse<T>? LinkedReadDenied<T>(string? project, bool family)
    {
        return capabilityContext.CanReadLinkedProjects(project, family)
            ? null
            : McpToolResponse<T>.Fail(
                "mcp_project_scope_denied",
                "The run-worker MCP profile may only read tasks from the current project.");
    }

    private ProjectScopedMcpToolResponse<T>? ProjectScopedLinkedReadDenied<T>(string? project)
    {
        return capabilityContext.CanReadLinkedProjects(project, false)
            ? null
            : ProjectScopedMcpToolResponse<T>.Fail(
                "mcp_project_scope_denied",
                "The run-worker MCP profile may only read project and activation data from the current project.");
    }

    private McpToolResponse<T>? LinkedWikiReadDenied<T>(string? project, bool family)
    {
        return capabilityContext.CanReadLinkedWikiProjects(project, family)
            ? null
            : McpToolResponse<T>.Fail(
                "mcp_project_scope_denied",
                "The run-worker MCP profile may only read the current project and explicitly granted linked wiki contexts.");
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

    private static AppResult<ProjectPayload> ToProjectPayload(ProjectRoot root)
    {
        if (!root.Exists || root.Config == null || root.RootPath == null)
            return AppResult<ProjectPayload>.Fail(
                "missing_project", "Project not found. Run pm init first.");

        return AppResult<ProjectPayload>.Ok(new ProjectPayload(
            root.Config.Name,
            root.RootPath,
            ToOptions(root.Config.TaskStates),
            ToOptions(root.Config.Tracks),
            ToMilestones(root.Config.Milestones)));
    }

    private static AppResult<ActivationSwitchboardPayload> ResolveActivationSwitchboard(ProjectRoot root)
    {
        var resolver = new MilestoneActivationResolver(root);
        var snapshot = resolver.ResolveCurrentProject();
        if (!snapshot.Success)
            return AppResult<ActivationSwitchboardPayload>.Fail(snapshot.ErrorCode!, snapshot.Message!);

        var tasksById = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        foreach (var task in root.GetAllTasks())
            tasksById.TryAdd(task.Id, task);
        var validator = new MilestoneActivationValidationService(
            root,
            new MilestoneActivationGraphService(),
            resolver);
        var issues = validator.Validate(root.Config!, tasksById);

        return AppResult<ActivationSwitchboardPayload>.Ok(new ActivationSwitchboardPayload(
            issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            snapshot.Payload!.ActivationTriggers.Select(ToActivationTriggerPayload).ToList(),
            snapshot.Payload.Milestones.Select(ToResolvedMilestonePayload).ToList(),
            issues.Select(ToValidationIssuePayload).ToList()));
    }

    private McpToolResponse<T>? ControlPlaneDenied<T>() =>
        capabilityContext.Profile == McpCapabilityProfile.Normal
            ? null
            : McpToolResponse<T>.Fail(
                "mcp_control_plane_denied",
                "The run-worker MCP profile cannot perform PM control-plane operations.");

    private Task<McpToolResponse<ActivationMutationPayload>> ExecuteTargetActivationMutationAsync<T>(
        string? project,
        Func<LinkedProjectMutationTarget, AppResult<T>> operation,
        Func<T, string> summary,
        Func<T, ActivationMutationDetailsPayload?>? impact = null,
        CancellationToken cancellationToken = default)
    {
        var denied = ControlPlaneDenied<ActivationMutationPayload>();
        return denied == null
            ? ExecuteTrustedTargetActivationMutationAsync(project, operation, summary, impact, cancellationToken)
            : Task.FromResult(denied);
    }

    private async Task<McpToolResponse<ActivationMutationPayload>> ExecuteTrustedTargetActivationMutationAsync<T>(
        string? project,
        Func<LinkedProjectMutationTarget, AppResult<T>> operation,
        Func<T, string> summary,
        Func<T, ActivationMutationDetailsPayload?>? impact = null,
        CancellationToken cancellationToken = default)
    {
        var result = await linkedProjectMutations.ExecuteAsync(
            project,
            target =>
            {
                var mutation = operation(target);
                if (!mutation.Success)
                    return AppResult<TargetActivationMutationResult<T>>.Fail(
                        mutation.ErrorCode!, mutation.Message!);

                var switchboard = ResolveActivationSwitchboard(target.Root);
                return switchboard.Success
                    ? AppResult<TargetActivationMutationResult<T>>.Ok(
                        new TargetActivationMutationResult<T>(mutation.Payload!, switchboard.Payload!))
                    : AppResult<TargetActivationMutationResult<T>>.Fail(
                        switchboard.ErrorCode!, switchboard.Message!);
            },
            MutationAccess,
            cancellationToken);
        if (!result.Success)
            return McpToolResponse<ActivationMutationPayload>.FromFailure(result);

        var receipt = result.Payload!.Receipt;
        var value = result.Payload.Value;
        return McpToolResponse<ActivationMutationPayload>.Ok(
            summary(value.Value),
            new ActivationMutationPayload(
                receipt.ChangedPaths.Count > 0,
                receipt.ChangedPaths.Count == 0 ? null : ToReceipt(receipt),
                value.Switchboard,
                impact?.Invoke(value.Value)));
    }

    private static AppResult<IReadOnlyList<ActivationRequirement>> ToActivationRequirements(
        IReadOnlyList<ActivationRequirementInputPayload>? requirements)
    {
        if (requirements == null)
            return AppResult<IReadOnlyList<ActivationRequirement>>.Fail(
                "invalid_activation_requirements", "Activation requirements are required.");

        var result = new List<ActivationRequirement>(requirements.Count);
        foreach (var requirement in requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.Source))
                return AppResult<IReadOnlyList<ActivationRequirement>>.Fail(
                    "missing_activation_requirement_source",
                    "Every activation requirement must have a source.");
            result.Add(new ActivationRequirement
            {
                Kind = requirement.Kind == ActivationRequirementInputKind.Task
                    ? ActivationRequirementKind.Task
                    : ActivationRequirementKind.Milestone,
                Source = requirement.Source.Trim(),
            });
        }

        return AppResult<IReadOnlyList<ActivationRequirement>>.Ok(result);
    }

    private sealed record TargetActivationMutationResult<T>(T Value, ActivationSwitchboardPayload Switchboard);

    private static ResolvedActivationTriggerPayload ToActivationTriggerPayload(
        ResolvedActivationTrigger trigger) =>
        new(
            trigger.Key,
            trigger.Title,
            trigger.IsActive,
            trigger.Activation == null
                ? null
                : new ResolvedActivationProvenancePayload(
                    trigger.Activation.At,
                    ToActivationModeValue(trigger.Activation.Mode),
                    trigger.Activation.Reason,
                    trigger.Activation.WaivedRequirements.Select(requirement =>
                        new ResolvedActivationRequirementReferencePayload(
                            ToRequirementKindValue(requirement.Kind), requirement.Source)).ToList()),
            trigger.SatisfiedRequirementCount,
            trigger.RequirementCount,
            trigger.RequirementsSatisfied,
            trigger.IsLatchedDespiteUnmetRequirements,
            trigger.Requirements.Select(requirement => new ResolvedActivationRequirementPayload(
                ToRequirementKindValue(requirement.Kind),
                requirement.Source,
                requirement.IsSatisfied,
                requirement.WasWaivedAtActivation)).ToList(),
            trigger.ConsumingMilestones);

    private static ResolvedMilestonePayload ToResolvedMilestonePayload(ResolvedMilestone milestone) =>
        new(
            milestone.Key,
            milestone.Title,
            milestone.Description,
            milestone.Priority,
            ToMilestoneLifecycleValue(milestone.Lifecycle),
            milestone.AssignedTaskCount,
            milestone.DoneTaskCount,
            milestone.RequiredActivationTriggers,
            milestone.UnmetActivationTriggers,
            milestone.Delivery == null
                ? null
                : new ResolvedMilestoneDeliveryPayload(
                    milestone.Delivery.At,
                    ToMilestoneDeliveryModeValue(milestone.Delivery.Mode),
                    milestone.Delivery.Reason,
                    milestone.Delivery.AcceptedTaskIds,
                    milestone.Delivery.IsValid));

    private static ProjectValidationIssuePayload ToValidationIssuePayload(ProjectValidationIssue issue) =>
        new(
            issue.Severity,
            issue.Code,
            issue.Message,
            issue.Path,
            issue.TaskId,
            issue.WikiPath,
            issue.State,
            issue.ProjectId,
            issue.ProjectAlias);

    private static ActivationTriggerRedefinitionPreviewPayload ToRedefinitionPreview(
        ActivationTriggerRedefinitionPreview preview) =>
        new(
            preview.TriggerKey,
            preview.Revision,
            preview.WillReactivateAutomatically,
            preview.RequiresConfirmation,
            preview.Milestones.Select(milestone => new ActivationTriggerMilestoneImpactPayload(
                milestone.MilestoneKey,
                ToMilestoneLifecycleValue(milestone.Before),
                ToMilestoneLifecycleValue(milestone.After),
                milestone.CurrentlyEligibleTaskIds,
                milestone.TaskIdsLosingEligibility)).ToList(),
            preview.CurrentlyEligibleTaskIds,
            preview.TaskIdsLosingEligibility);

    private static MilestoneDeliveryPreviewPayload ToMilestoneDeliveryPreview(MilestoneDeliveryPreview preview) =>
        new(
            preview.MilestoneKey,
            preview.Title,
            preview.Revision,
            ToMilestoneDeliveryModeValue(preview.Mode),
            preview.AssignedTaskCount,
            preview.DoneTaskCount,
            preview.UnfinishedTaskIds,
            preview.RequiresConfirmation);

    private static AutomaticActivationImpactPayload ToAutomaticActivationImpact(
        AutomaticActivationImpact impact) =>
        new(
            impact.ActivatedTriggers.Select(trigger => trigger.Key).ToList(),
            impact.MilestoneChanges.Select(change => new MilestoneLifecycleChangePayload(
                change.MilestoneKey,
                ToMilestoneLifecycleValue(change.Before),
                ToMilestoneLifecycleValue(change.After))).ToList());

    private static ReleaseTransitionPayload? ToReleaseTransition(ReleaseVersionTransition? transition) =>
        transition == null
            ? null
            : new ReleaseTransitionPayload(
                transition.At,
                transition.Kind,
                transition.FromVersion,
                transition.ToVersion,
                transition.Source,
                transition.Reason);

    private static string ToRequirementKindValue(ActivationRequirementKind kind) =>
        kind == ActivationRequirementKind.Task ? "task" : "milestone";

    private static string ToActivationModeValue(ActivationMode mode) => mode switch
    {
        ActivationMode.Automatic => "automatic",
        ActivationMode.Manual => "manual",
        ActivationMode.Override => "override",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static string ToMilestoneLifecycleValue(MilestoneLifecycle lifecycle) => lifecycle switch
    {
        MilestoneLifecycle.Delivered => "delivered",
        MilestoneLifecycle.Inactive => "inactive",
        MilestoneLifecycle.ReadyToDeliver => "ready_to_deliver",
        MilestoneLifecycle.Active => "active",
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null),
    };

    private static string ToMilestoneDeliveryModeValue(MilestoneDeliveryMode mode) => mode switch
    {
        MilestoneDeliveryMode.Ordinary => "ordinary",
        MilestoneDeliveryMode.Exceptional => "exceptional",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private LinkedProjectTargetAccess MutationAccess =>
        capabilityContext.Profile == McpCapabilityProfile.RunWorker
            ? LinkedProjectTargetAccess.CurrentProjectOnly
            : LinkedProjectTargetAccess.WriteTrustedLinkedProjects;

    private string? ActiveProjectId =>
        projectRoot.TryReadProjectId(out var projectId) ? projectId : null;

    private static ProjectMutationReceiptPayload ToReceipt(ProjectMutationReceipt receipt) =>
        new(receipt.ProjectId, receipt.ChangedPaths);

    private static AppResult<bool> ToPayload(AppResult result) =>
        result.Success
            ? AppResult<bool>.Ok(true)
            : AppResult<bool>.Fail(result.ErrorCode!, result.Message!);

    private static AppResult<bool> ToPayload<T>(AppResult<LifecycleMutationResult<T>> result) =>
        result.Success
            ? AppResult<bool>.Ok(true)
            : AppResult<bool>.Fail(result.ErrorCode!, result.Message!);

}
