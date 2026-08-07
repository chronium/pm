using PM.Project;
using PM.Tasks;

namespace PM.Application;

public enum LinkedProjectReadScope
{
    Current,
    Project,
    Family,
}

public sealed record LinkedProjectReadRequest(
    LinkedProjectReadScope Scope = LinkedProjectReadScope.Current,
    string? ProjectSelector = null)
{
    public static AppResult<LinkedProjectReadRequest> FromOptions(string? projectSelector, bool family)
    {
        var normalized = string.IsNullOrWhiteSpace(projectSelector) ? null : projectSelector.Trim();
        if (family && normalized != null)
            return AppResult<LinkedProjectReadRequest>.Fail(
                "invalid_project_scope", "Project selection and family scope cannot be used together.");

        return AppResult<LinkedProjectReadRequest>.Ok(family
            ? new LinkedProjectReadRequest(LinkedProjectReadScope.Family)
            : normalized == null || string.Equals(normalized, "current", StringComparison.OrdinalIgnoreCase)
                ? new LinkedProjectReadRequest()
                : new LinkedProjectReadRequest(LinkedProjectReadScope.Project, normalized));
    }
}

public sealed record LinkedProjectGitMetadata(string? Revision, bool? Dirty);

public sealed record LinkedProjectResourceOwner(
    string ProjectId,
    string ProjectName,
    string? Alias,
    LinkedProjectRelationship Relationship,
    string? Revision,
    bool? Dirty);

public sealed record LinkedProjectResource<T>(LinkedProjectResourceOwner Owner, T Resource);

public sealed record LinkedProjectReadResult<T>(
    IReadOnlyList<LinkedProjectResource<T>> Items,
    IReadOnlyList<LinkedProjectFamilyWarning> Warnings,
    bool Truncated = false);

public sealed record LinkedProjectNextTaskResult(
    bool Found,
    BoardTask? Task,
    LinkedProjectResourceOwner? Owner,
    string Reason,
    IReadOnlyList<LinkedProjectFamilyWarning> Warnings);

public interface ILinkedProjectGitInspector
{
    Task<LinkedProjectGitMetadata> InspectAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}

public sealed class LinkedProjectReadService
{
    public const int MaximumListResultCount = 5_000;
    private const int MaximumSearchResultCount = 100;
    private readonly ProjectRoot activeProject;
    private readonly LinkedProjectFamilyService familyService;
    private readonly INextIdService nextIdService;
    private readonly TaskServiceFactory taskServices;
    private readonly ILinkedProjectGitInspector gitInspector;
    private readonly LinkedProjectTaskGraphService taskGraphService;
    private readonly int maximumListResultCount;

    public LinkedProjectReadService(
        ProjectRoot activeProject,
        LinkedProjectFamilyService familyService,
        INextIdService nextIdService,
        ILinkedProjectGitInspector gitInspector,
        TaskServiceFactory taskServices)
        : this(activeProject, familyService, nextIdService, gitInspector, taskServices, MaximumListResultCount)
    {
    }

    public LinkedProjectReadService(
        ProjectRoot activeProject,
        LinkedProjectFamilyService familyService,
        INextIdService nextIdService,
        ILinkedProjectGitInspector gitInspector,
        TaskServiceFactory taskServices,
        int maximumListResultCount)
    {
        this.activeProject = activeProject;
        this.familyService = familyService;
        this.nextIdService = nextIdService;
        this.taskServices = taskServices;
        this.gitInspector = gitInspector;
        taskGraphService = new LinkedProjectTaskGraphService(familyService);
        this.maximumListResultCount = Math.Clamp(maximumListResultCount, 1, MaximumListResultCount);
    }

    public async Task<AppResult<LinkedProjectReadResult<BoardTask>>> ListTasksAsync(
        LinkedProjectReadRequest request,
        BoardQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new BoardQuery();
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success) return Failure<BoardTask>(targets);

        var warnings = new ReadWarningCollector(targets.Payload!.Warnings);
        var items = new List<LinkedProjectResource<BoardTask>>();
        var truncated = false;
        foreach (var member in targets.Payload.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Scope == LinkedProjectReadScope.Family && !Supports(member.Project!, query)) continue;

            var result = CreateBoardService(member.Project!).GetBoard(query);
            if (!result.Success)
            {
                if (!CanContinue(request, member))
                    return Failure<BoardTask>(result.ErrorCode, result.Message);
                warnings.Add(ReadFailure(member, "tasks", result.ErrorCode));
                continue;
            }

            var owner = await BuildOwnerAsync(member, cancellationToken);
            foreach (var task in result.Payload!.Tasks)
            {
                if (items.Count == maximumListResultCount)
                {
                    truncated = true;
                    break;
                }

                items.Add(new LinkedProjectResource<BoardTask>(owner, task));
            }

            if (truncated) break;
        }

        if (truncated)
            warnings.Add(TruncationWarning(targets.Payload.ActiveProjectId, "task", maximumListResultCount));
        items = await EnrichTasksAsync(items, warnings, cancellationToken);
        return AppResult<LinkedProjectReadResult<BoardTask>>.Ok(
            new LinkedProjectReadResult<BoardTask>(items, warnings.Items, truncated));
    }

    public async Task<AppResult<BoardData>> EnrichCurrentBoardAsync(
        BoardData board,
        CancellationToken cancellationToken = default)
    {
        if (!board.Tasks.Any(task => HasQualifiedDependency(task.Task)) ||
            !activeProject.TryReadProjectId(out var projectId))
            return AppResult<BoardData>.Ok(board);

        var graph = await taskGraphService.BuildAsync(cancellationToken);
        if (!graph.Success)
            return AppResult<BoardData>.Fail(graph.ErrorCode!, graph.Message!);

        BoardTask Enrich(BoardTask task) => task with
        {
            Dependencies = graph.Payload!.GetDependencyStatus(projectId, task.Task),
        };

        return AppResult<BoardData>.Ok(board with
        {
            Tasks = board.Tasks.Select(Enrich).ToList(),
            MilestoneGroups = board.MilestoneGroups.Select(group => group with
            {
                States = group.States.Select(state => state with
                {
                    Tasks = state.Tasks.Select(Enrich).ToList(),
                }).ToList(),
            }).ToList(),
        });
    }

    public async Task<AppResult<BoardTask>> EnrichCurrentTaskAsync(
        BoardTask task,
        CancellationToken cancellationToken = default)
    {
        if (!HasQualifiedDependency(task.Task) || !activeProject.TryReadProjectId(out var projectId))
            return AppResult<BoardTask>.Ok(task);

        var graph = await taskGraphService.BuildAsync(cancellationToken);
        return graph.Success
            ? AppResult<BoardTask>.Ok(task with
            {
                Dependencies = graph.Payload!.GetDependencyStatus(projectId, task.Task),
            })
            : AppResult<BoardTask>.Fail(graph.ErrorCode!, graph.Message!);
    }

    public async Task<AppResult<LinkedProjectReadResult<BoardTask>>> GetTaskAsync(
        string taskId,
        string? projectSelector = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return AppResult<LinkedProjectReadResult<BoardTask>>.Fail(
                "invalid_task", "Task ID is required.");

        var request = string.IsNullOrWhiteSpace(projectSelector) ||
                      string.Equals(projectSelector.Trim(), "current", StringComparison.OrdinalIgnoreCase)
            ? new LinkedProjectReadRequest()
            : new LinkedProjectReadRequest(LinkedProjectReadScope.Project, projectSelector);
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success) return Failure<BoardTask>(targets);

        var member = targets.Payload!.Members.Single();
        cancellationToken.ThrowIfCancellationRequested();
        var result = CreateBoardService(member.Project!).GetTask(taskId.Trim());
        if (!result.Success) return Failure<BoardTask>(result.ErrorCode, result.Message);
        if (!member.Project!.TryReadTaskFile(taskId.Trim(), out var markdown))
            return Failure<BoardTask>("missing_task", $"Task {taskId.Trim()} not found.");

        var owner = await BuildOwnerAsync(member, cancellationToken);
        var warnings = new ReadWarningCollector(targets.Payload.Warnings);
        var items = await EnrichTasksAsync(
            [new LinkedProjectResource<BoardTask>(owner, result.Payload! with { Markdown = markdown })],
            warnings,
            cancellationToken);
        return AppResult<LinkedProjectReadResult<BoardTask>>.Ok(
            new LinkedProjectReadResult<BoardTask>(
                items,
                warnings.Items));
    }

    public async Task<AppResult<LinkedProjectReadResult<TaskSearchResult>>> SearchTasksAsync(
        string query,
        int limit = 20,
        LinkedProjectReadRequest? request = null,
        TaskSearchContext? context = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new LinkedProjectReadRequest();
        limit = Math.Clamp(limit, 1, MaximumSearchResultCount);
        var parsedQuery = TaskSearchQueryParser.Parse(query);
        if (!parsedQuery.Success)
            return Failure<TaskSearchResult>(parsedQuery.ErrorCode, parsedQuery.Message);
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success) return Failure<TaskSearchResult>(targets);

        var warnings = new ReadWarningCollector(targets.Payload!.Warnings);
        var indexed = new List<(int ProjectIndex, LinkedProjectResource<TaskSearchResult> Item)>();
        for (var index = 0; index < targets.Payload.Members.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var member = targets.Payload.Members[index];
            if (request.Scope == LinkedProjectReadScope.Family && !Supports(member.Project!, context)) continue;

            var result = taskServices.Create(member.Project!, nextIdService).SearchTasks(query, limit, context);
            if (!result.Success)
            {
                if (!CanContinue(request, member))
                    return Failure<TaskSearchResult>(result.ErrorCode, result.Message);
                warnings.Add(ReadFailure(member, "task search", result.ErrorCode));
                continue;
            }

            var owner = await BuildOwnerAsync(member, cancellationToken);
            indexed.AddRange(result.Payload!.Select(item =>
                (index, new LinkedProjectResource<TaskSearchResult>(owner, item))));
        }

        var items = indexed
            .OrderByDescending(entry => entry.Item.Resource.MatchCount)
            .ThenBy(entry => entry.ProjectIndex)
            .ThenBy(entry => entry.Item.Resource.Task.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(entry => entry.Item)
            .ToList();
        items = await EnrichSearchResultsAsync(items, warnings, cancellationToken);
        return AppResult<LinkedProjectReadResult<TaskSearchResult>>.Ok(
            new LinkedProjectReadResult<TaskSearchResult>(items, warnings.Items));
    }

    public async Task<AppResult<LinkedProjectNextTaskResult>> GetNextTaskAsync(
        LinkedProjectReadRequest request,
        NextTaskQuery query,
        int descriptionPreviewLength = BoardService.CliDescriptionPreviewLength,
        CancellationToken cancellationToken = default)
    {
        if (request.Scope == LinkedProjectReadScope.Current &&
            !activeProject.TryReadProjectId(out _))
        {
            var local = CreateBoardService(activeProject).GetNextTask(query, descriptionPreviewLength);
            return local.Success
                ? AppResult<LinkedProjectNextTaskResult>.Ok(new LinkedProjectNextTaskResult(
                    local.Payload!.Found, local.Payload.Task, null, local.Payload.Reason, []))
                : AppResult<LinkedProjectNextTaskResult>.Fail(local.ErrorCode!, local.Message!);
        }

        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success)
            return AppResult<LinkedProjectNextTaskResult>.Fail(targets.ErrorCode!, targets.Message!);

        var members = targets.Payload!.Members;
        if (request.Scope == LinkedProjectReadScope.Family)
        {
            if (query.Track != null && !members.Any(member => member.Project!.Config!.Tracks.ContainsKey(query.Track)))
                return AppResult<LinkedProjectNextTaskResult>.Fail(
                    "invalid_track", $"Track {query.Track} was not found in any available family project.");
            if (query.Milestone != null &&
                !members.Any(member => member.Project!.Config!.Milestones.ContainsKey(query.Milestone)))
                return AppResult<LinkedProjectNextTaskResult>.Fail(
                    "invalid_milestone", $"Milestone {query.Milestone} was not found in any available family project.");
        }

        var warnings = new ReadWarningCollector(targets.Payload.Warnings);
        var candidates = new List<RecommendationCandidate>();
        var activationEligibleCount = 0;
        var activationExcludedCount = 0;
        ResolvedMilestone? scopedMilestone = null;
        for (var projectIndex = 0; projectIndex < members.Count; projectIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var member = members[projectIndex];
            if (request.Scope == LinkedProjectReadScope.Family &&
                !Supports(member.Project!, new BoardQuery(query.Track, query.Milestone)))
                continue;

            var board = CreateBoardService(member.Project!).GetBoard(
                new BoardQuery(query.Track, query.Milestone), descriptionPreviewLength);
            if (!board.Success)
            {
                if (!CanContinue(request, member))
                    return AppResult<LinkedProjectNextTaskResult>.Fail(board.ErrorCode!, board.Message!);
                warnings.Add(ReadFailure(member, "task recommendations", board.ErrorCode));
                continue;
            }

            if (request.Scope != LinkedProjectReadScope.Family && query.Milestone != null)
                scopedMilestone = board.Payload!.MilestoneActivation.Milestones.SingleOrDefault(
                    milestone => string.Equals(milestone.Key, query.Milestone, StringComparison.Ordinal));

            var owner = await BuildOwnerAsync(member, cancellationToken);
            var remaining = board.Payload!.Tasks
                .Where(task => !string.Equals(task.State, "done", StringComparison.Ordinal))
                .ToList();
            activationEligibleCount += remaining.Count(task => task.Activation.IsEligible);
            activationExcludedCount += remaining.Count(task => !task.Activation.IsEligible);
            candidates.AddRange(remaining
                .Where(task => task.Activation.IsEligible)
                .Select(task => new RecommendationCandidate(
                    member, owner, task, projectIndex,
                    GetStateIndex(member.Project!, task),
                    GetMilestoneIndex(member.Project!, task),
                    GetTaskOrderIndex(member.Project!, task))));
        }

        var resources = candidates
            .Select(candidate => new LinkedProjectResource<BoardTask>(candidate.Owner, candidate.Task))
            .ToList();
        var enriched = await EnrichTasksAsync(resources, warnings, cancellationToken, force: true);
        var dependencies = enriched.ToDictionary(
            item => new ProjectTaskKey(item.Owner.ProjectId, item.Resource.Task.Id),
            item => item.Resource.Dependencies);
        candidates = candidates.Select(candidate => candidate with
            {
                Task = candidate.Task with
                {
                    Dependencies = dependencies[new ProjectTaskKey(
                        candidate.Owner.ProjectId, candidate.Task.Task.Id)],
                },
            })
            .Where(candidate => !query.ReadyOnly || candidate.Task.Dependencies.Ready)
            .ToList();

        var selected = candidates
            .OrderBy(candidate => candidate.Task.Dependencies.Ready ? 0 : 1)
            .ThenByDescending(candidate => PriorityLevel.Rank(candidate.Task.Priority))
            .ThenBy(candidate => candidate.StateIndex)
            .ThenBy(candidate => string.Equals(
                candidate.Owner.ProjectId, targets.Payload.ActiveProjectId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(candidate => candidate.ProjectIndex)
            .ThenBy(candidate => candidate.MilestoneIndex)
            .ThenBy(candidate => candidate.TaskOrderIndex)
            .ThenByDescending(candidate => candidate.Task.Task.ModifiedAt)
            .ThenBy(candidate => candidate.Owner.ProjectId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Task.Task.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (selected == null)
            return AppResult<LinkedProjectNextTaskResult>.Ok(new LinkedProjectNextTaskResult(
                false, null, null,
                BuildNoRecommendationReason(
                    request, query, scopedMilestone, activationEligibleCount, activationExcludedCount),
                warnings.Items));

        var reason = $"Selected {selected.Task.Priority} priority task in " +
                     $"{selected.Owner.ProjectName} ({selected.Owner.ProjectId}), state {selected.Task.State}; " +
                     $"{selected.Task.Dependencies.Summary}." +
                     BoardService.BuildActivationSelectionContext(selected.Task);
        return AppResult<LinkedProjectNextTaskResult>.Ok(new LinkedProjectNextTaskResult(
            true, selected.Task, selected.Owner, reason, warnings.Items));
    }

    public async Task<AppResult<LinkedProjectReadResult<WikiPageSummary>>> ListWikiPagesAsync(
        LinkedProjectReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success) return Failure<WikiPageSummary>(targets);

        var warnings = new ReadWarningCollector(targets.Payload!.Warnings);
        var items = new List<LinkedProjectResource<WikiPageSummary>>();
        var truncated = false;
        foreach (var member in targets.Payload.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new WikiService(member.Project!).ListPages();
            if (!result.Success)
            {
                if (!CanContinue(request, member))
                    return Failure<WikiPageSummary>(result.ErrorCode, result.Message);
                warnings.Add(ReadFailure(member, "wiki pages", result.ErrorCode));
                continue;
            }

            var owner = await BuildOwnerAsync(member, cancellationToken);
            foreach (var page in result.Payload!)
            {
                if (items.Count == maximumListResultCount)
                {
                    truncated = true;
                    break;
                }

                items.Add(new LinkedProjectResource<WikiPageSummary>(owner, page));
            }

            if (truncated) break;
        }

        if (truncated)
            warnings.Add(TruncationWarning(targets.Payload.ActiveProjectId, "wiki page", maximumListResultCount));
        return AppResult<LinkedProjectReadResult<WikiPageSummary>>.Ok(
            new LinkedProjectReadResult<WikiPageSummary>(items, warnings.Items, truncated));
    }

    public async Task<AppResult<LinkedProjectReadResult<WikiPageData>>> GetWikiPageAsync(
        string path,
        string? projectSelector = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return AppResult<LinkedProjectReadResult<WikiPageData>>.Fail(
                "invalid_wiki_path", "Wiki page path is required.");

        var request = string.IsNullOrWhiteSpace(projectSelector) ||
                      string.Equals(projectSelector.Trim(), "current", StringComparison.OrdinalIgnoreCase)
            ? new LinkedProjectReadRequest()
            : new LinkedProjectReadRequest(LinkedProjectReadScope.Project, projectSelector);
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success) return Failure<WikiPageData>(targets);

        var member = targets.Payload!.Members.Single();
        cancellationToken.ThrowIfCancellationRequested();
        var result = new WikiService(member.Project!).ReadPage(path.Trim());
        if (!result.Success) return Failure<WikiPageData>(result.ErrorCode, result.Message);

        var owner = await BuildOwnerAsync(member, cancellationToken);
        return AppResult<LinkedProjectReadResult<WikiPageData>>.Ok(
            new LinkedProjectReadResult<WikiPageData>(
                [new LinkedProjectResource<WikiPageData>(owner, result.Payload!)],
                targets.Payload.Warnings));
    }

    public async Task<AppResult<LinkedProjectReadResult<WikiPageOutlineData>>> OutlineWikiPageAsync(
        string path,
        string? projectSelector = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return AppResult<LinkedProjectReadResult<WikiPageOutlineData>>.Fail(
                "invalid_wiki_path", "Wiki page path is required.");

        var request = string.IsNullOrWhiteSpace(projectSelector) ||
                      string.Equals(projectSelector.Trim(), "current", StringComparison.OrdinalIgnoreCase)
            ? new LinkedProjectReadRequest()
            : new LinkedProjectReadRequest(LinkedProjectReadScope.Project, projectSelector);
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success) return Failure<WikiPageOutlineData>(targets);

        var member = targets.Payload!.Members.Single();
        cancellationToken.ThrowIfCancellationRequested();
        var result = new WikiService(member.Project!).OutlinePage(path.Trim());
        if (!result.Success) return Failure<WikiPageOutlineData>(result.ErrorCode, result.Message);

        var owner = await BuildOwnerAsync(member, cancellationToken);
        return AppResult<LinkedProjectReadResult<WikiPageOutlineData>>.Ok(
            new LinkedProjectReadResult<WikiPageOutlineData>(
                [new LinkedProjectResource<WikiPageOutlineData>(owner, result.Payload!)],
                targets.Payload.Warnings));
    }

    public async Task<AppResult<LinkedProjectReadResult<WikiSearchResult>>> SearchWikiPagesAsync(
        string query,
        int limit = 20,
        LinkedProjectReadRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new LinkedProjectReadRequest();
        limit = Math.Clamp(limit, 1, MaximumSearchResultCount);
        if (string.IsNullOrWhiteSpace(query))
            return AppResult<LinkedProjectReadResult<WikiSearchResult>>.Fail(
                "invalid_wiki_query", "Wiki search query is required.");
        var targets = await ResolveTargetsAsync(request, cancellationToken);
        if (!targets.Success) return Failure<WikiSearchResult>(targets);

        var warnings = new ReadWarningCollector(targets.Payload!.Warnings);
        var indexed = new List<(int ProjectIndex, LinkedProjectResource<WikiSearchResult> Item)>();
        for (var index = 0; index < targets.Payload.Members.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var member = targets.Payload.Members[index];
            var result = new WikiService(member.Project!).SearchPages(query, limit);
            if (!result.Success)
            {
                if (!CanContinue(request, member))
                    return Failure<WikiSearchResult>(result.ErrorCode, result.Message);
                warnings.Add(ReadFailure(member, "wiki search", result.ErrorCode));
                continue;
            }

            var owner = await BuildOwnerAsync(member, cancellationToken);
            indexed.AddRange(result.Payload!.Select(item =>
                (index, new LinkedProjectResource<WikiSearchResult>(owner, item))));
        }

        var items = indexed
            .OrderByDescending(entry => entry.Item.Resource.MatchCount)
            .ThenBy(entry => entry.ProjectIndex)
            .ThenBy(entry => entry.Item.Resource.Path, StringComparer.Ordinal)
            .Take(limit)
            .Select(entry => entry.Item)
            .ToList();
        return AppResult<LinkedProjectReadResult<WikiSearchResult>>.Ok(
            new LinkedProjectReadResult<WikiSearchResult>(items, warnings.Items));
    }

    private async Task<AppResult<ResolvedTargets>> ResolveTargetsAsync(
        LinkedProjectReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Scope))
            return AppResult<ResolvedTargets>.Fail(
                "invalid_project_scope", "Linked-project read scope is invalid.");
        if (!activeProject.Exists || activeProject.Config == null)
            return AppResult<ResolvedTargets>.Fail(
                "missing_project", "Project not found. Run pm init first.");
        if (!activeProject.TryReadProjectId(out var activeProjectId))
            return AppResult<ResolvedTargets>.Fail(
                "missing_project_id", "The active project has no valid stable project ID.");

        var selectsCurrent = !string.IsNullOrWhiteSpace(request.ProjectSelector) &&
                             string.Equals(request.ProjectSelector.Trim(), "current",
                                 StringComparison.OrdinalIgnoreCase);
        if (request.Scope == LinkedProjectReadScope.Current ||
            request.Scope == LinkedProjectReadScope.Project && selectsCurrent)
        {
            if (request.Scope == LinkedProjectReadScope.Current &&
                !string.IsNullOrWhiteSpace(request.ProjectSelector) &&
                !string.Equals(request.ProjectSelector.Trim(), "current", StringComparison.OrdinalIgnoreCase))
                return AppResult<ResolvedTargets>.Fail(
                    "invalid_project_selector", "Current scope only accepts the current project selector.");
            return AppResult<ResolvedTargets>.Ok(new ResolvedTargets(
                activeProjectId,
                [CurrentMember(activeProjectId)],
                []));
        }

        if (request.Scope == LinkedProjectReadScope.Project && string.IsNullOrWhiteSpace(request.ProjectSelector))
            return AppResult<ResolvedTargets>.Fail(
                "missing_project_selector", "Project scope requires a project selector.");
        if (request.Scope == LinkedProjectReadScope.Family && !string.IsNullOrWhiteSpace(request.ProjectSelector))
            return AppResult<ResolvedTargets>.Fail(
                "invalid_project_selector", "Family scope does not accept a project selector.");

        cancellationToken.ThrowIfCancellationRequested();
        var family = await familyService.ResolveAsync(cancellationToken);
        if (!family.Success)
            return AppResult<ResolvedTargets>.Fail(family.ErrorCode!, family.Message!);

        var resolvedFamily = family.Payload!;
        if (request.Scope == LinkedProjectReadScope.Family)
            return AppResult<ResolvedTargets>.Ok(new ResolvedTargets(
                activeProjectId,
                resolvedFamily.Members.Where(member => member.Readable && member.Project != null).ToList(),
                resolvedFamily.Warnings));

        var selected = LinkedProjectFamilyService.SelectMember(resolvedFamily, request.ProjectSelector!);
        if (!selected.Success)
            return AppResult<ResolvedTargets>.Fail(selected.ErrorCode!, selected.Message!);
        var member = selected.Payload!;
        if (!member.Readable || member.Project == null)
        {
            var repair = member.RepairAction == null
                ? string.Empty
                : $" Run {member.RepairAction.DisplayCommand}.";
            return AppResult<ResolvedTargets>.Fail(
                "linked_project_unavailable",
                $"Linked project {member.ProjectId} ({member.Alias}) is " +
                $"{LinkedProjectFamilyService.Format(member.Status)}.{repair}");
        }

        return AppResult<ResolvedTargets>.Ok(new ResolvedTargets(
            activeProjectId,
            [member],
            resolvedFamily.Warnings));
    }

    private async Task<List<LinkedProjectResource<BoardTask>>> EnrichTasksAsync(
        List<LinkedProjectResource<BoardTask>> items,
        ReadWarningCollector warnings,
        CancellationToken cancellationToken,
        bool force = false)
    {
        if (!force && !items.Any(item => HasQualifiedDependency(item.Resource.Task))) return items;

        var graph = await taskGraphService.BuildAsync(cancellationToken);
        if (!graph.Success)
        {
            warnings.Add(new LinkedProjectFamilyWarning(
                graph.ErrorCode ?? "dependency_graph_unavailable",
                graph.Message ?? "The linked task dependency graph could not be resolved.",
                items.FirstOrDefault()?.Owner.ProjectId ?? "current",
                items.FirstOrDefault()?.Owner.ProjectId ?? "current",
                items.FirstOrDefault()?.Owner.Alias,
                LinkedProjectResolutionStatus.Invalid));
            return items;
        }

        foreach (var warning in graph.Payload!.Warnings) warnings.Add(warning);
        return items.Select(item => item with
            {
                Resource = item.Resource with
                {
                    Dependencies = graph.Payload.GetDependencyStatus(item.Owner.ProjectId, item.Resource.Task),
                },
            })
            .ToList();
    }

    private async Task<List<LinkedProjectResource<TaskSearchResult>>> EnrichSearchResultsAsync(
        List<LinkedProjectResource<TaskSearchResult>> items,
        ReadWarningCollector warnings,
        CancellationToken cancellationToken)
    {
        if (!items.Any(item => HasQualifiedDependency(item.Resource.Task))) return items;

        var graph = await taskGraphService.BuildAsync(cancellationToken);
        if (!graph.Success)
        {
            warnings.Add(new LinkedProjectFamilyWarning(
                graph.ErrorCode ?? "dependency_graph_unavailable",
                graph.Message ?? "The linked task dependency graph could not be resolved.",
                items.FirstOrDefault()?.Owner.ProjectId ?? "current",
                items.FirstOrDefault()?.Owner.ProjectId ?? "current",
                items.FirstOrDefault()?.Owner.Alias,
                LinkedProjectResolutionStatus.Invalid));
            return items;
        }

        foreach (var warning in graph.Payload!.Warnings) warnings.Add(warning);
        return items.Select(item => item with
            {
                Resource = item.Resource with
                {
                    Dependencies = graph.Payload.GetDependencyStatus(item.Owner.ProjectId, item.Resource.Task),
                },
            })
            .ToList();
    }

    private static bool HasQualifiedDependency(TaskItem task) =>
        task.DependencyIds.Any(value =>
            TaskDependencyReference.TryParse(value, out var dependency, out _) &&
            dependency!.ProjectId != null);

    private static int GetStateIndex(ProjectRoot project, BoardTask task)
    {
        var index = 0;
        foreach (var state in project.Config!.TaskStates.Keys)
        {
            if (string.Equals(state, task.State, StringComparison.Ordinal)) return index;
            index++;
        }

        return int.MaxValue;
    }

    private static int GetMilestoneIndex(ProjectRoot project, BoardTask task)
    {
        if (task.Milestone == null) return project.Config!.Milestones.Count;
        var index = 0;
        foreach (var milestone in project.Config!.Milestones.Keys)
        {
            if (string.Equals(milestone, task.Milestone, StringComparison.Ordinal)) return index;
            index++;
        }

        return project.Config.Milestones.Count + 1;
    }

    private static int GetTaskOrderIndex(ProjectRoot project, BoardTask task)
    {
        var order = project.ReadTaskOrder().Orders.FirstOrDefault(entry =>
            string.Equals(entry.Track, task.Track, StringComparison.Ordinal) &&
            string.Equals(entry.State, task.State, StringComparison.Ordinal) &&
            string.Equals(Normalize(entry.Milestone), Normalize(task.Milestone), StringComparison.Ordinal));
        if (order == null) return int.MaxValue;
        var index = order.TaskIds.ToList().FindIndex(id => string.Equals(id, task.Task.Id, StringComparison.Ordinal));
        return index >= 0 ? index : int.MaxValue;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BoardService CreateBoardService(ProjectRoot project) =>
        new(project, new MilestoneActivationResolver(project));

    private static string BuildNoRecommendationReason(
        LinkedProjectReadRequest request,
        NextTaskQuery query,
        ResolvedMilestone? scopedMilestone,
        int activationEligibleCount,
        int activationExcludedCount)
    {
        var scope = request.Scope switch
        {
            LinkedProjectReadScope.Family => " in the linked project family",
            LinkedProjectReadScope.Project => $" in project {request.ProjectSelector}",
            _ => " in the active project",
        };
        var filters = new List<string>();
        if (query.Track != null) filters.Add($"track {query.Track}");
        if (query.Milestone != null) filters.Add($"milestone {query.Milestone}");
        var filter = filters.Count == 0 ? string.Empty : $" for {string.Join(" and ", filters)}";
        if (scopedMilestone?.Lifecycle == MilestoneLifecycle.Inactive)
            return $"No activation-eligible task found{scope}{filter}; milestone {scopedMilestone.Key} is inactive; " +
                   $"unmet activation triggers: {string.Join(", ", scopedMilestone.UnmetActivationTriggers)}.";
        if (scopedMilestone?.Lifecycle == MilestoneLifecycle.Delivered)
            return $"No activation-eligible task found{scope}{filter}; milestone {scopedMilestone.Key} is delivered.";
        if (activationEligibleCount == 0 && activationExcludedCount > 0)
        {
            var noun = activationExcludedCount == 1 ? "task is" : "tasks are";
            return $"No activation-eligible task found{scope}{filter}; {activationExcludedCount} remaining " +
                   $"{noun} excluded by inactive or delivered milestones.";
        }
        return query.ReadyOnly
            ? $"No dependency-ready actionable task found{scope}{filter}."
            : $"No actionable task found{scope}{filter}.";
    }

    private LinkedProjectFamilyMember CurrentMember(string activeProjectId) =>
        new(activeProjectId, activeProject.Config!.Name, "current", LinkedProjectRelationship.Current,
            LinkedProjectResolutionStatus.Available, LinkedProjectResolutionSource.ActiveProject,
            true, true, activeProject, activeProject.RepositoryPath);

    private async Task<LinkedProjectResourceOwner> BuildOwnerAsync(
        LinkedProjectFamilyMember member,
        CancellationToken cancellationToken)
    {
        var git = await gitInspector.InspectAsync(member.Project!.RepositoryPath, cancellationToken);
        return new LinkedProjectResourceOwner(
            member.ProjectId,
            member.Name,
            member.Alias,
            member.Relationship,
            git.Revision,
            git.Dirty);
    }

    private static bool Supports(ProjectRoot project, BoardQuery query) =>
        (string.IsNullOrWhiteSpace(query.Track) || project.Config!.Tracks.ContainsKey(query.Track)) &&
        (string.IsNullOrWhiteSpace(query.Milestone) || project.Config!.Milestones.ContainsKey(query.Milestone)) &&
        (string.IsNullOrWhiteSpace(query.State) || project.Config!.TaskStates.ContainsKey(query.State));

    private static bool Supports(ProjectRoot project, TaskSearchContext? context) =>
        context == null ||
        (string.IsNullOrWhiteSpace(context.Track) || project.Config!.Tracks.ContainsKey(context.Track)) &&
        (string.IsNullOrWhiteSpace(context.Milestone) || project.Config!.Milestones.ContainsKey(context.Milestone)) &&
        (string.IsNullOrWhiteSpace(context.State) || project.Config!.TaskStates.ContainsKey(context.State));

    private static bool CanContinue(LinkedProjectReadRequest request, LinkedProjectFamilyMember member) =>
        request.Scope == LinkedProjectReadScope.Family &&
        member.Relationship != LinkedProjectRelationship.Current;

    private static LinkedProjectFamilyWarning ReadFailure(
        LinkedProjectFamilyMember member,
        string resource,
        string? errorCode) =>
        new(errorCode ?? "linked_project_read_failed",
            $"Linked project {member.ProjectId} could not provide {resource}.",
            member.ProjectId,
            member.ProjectId,
            member.Alias,
            LinkedProjectResolutionStatus.Invalid);

    private static LinkedProjectFamilyWarning TruncationWarning(
        string activeProjectId,
        string resource,
        int maximumResultCount) =>
        new("linked_project_results_truncated",
            $"Federated {resource} results were truncated after {maximumResultCount} records.",
            activeProjectId,
            activeProjectId,
            "current",
            LinkedProjectResolutionStatus.Available);

    private static AppResult<LinkedProjectReadResult<T>> Failure<T>(AppResult<ResolvedTargets> result) =>
        AppResult<LinkedProjectReadResult<T>>.Fail(result.ErrorCode!, result.Message!);

    private static AppResult<LinkedProjectReadResult<T>> Failure<T>(string? errorCode, string? message) =>
        AppResult<LinkedProjectReadResult<T>>.Fail(
            errorCode ?? "linked_project_read_failed",
            message ?? "Linked-project read failed.");

    private sealed record ResolvedTargets(
        string ActiveProjectId,
        IReadOnlyList<LinkedProjectFamilyMember> Members,
        IReadOnlyList<LinkedProjectFamilyWarning> Warnings);

    private sealed record RecommendationCandidate(
        LinkedProjectFamilyMember Member,
        LinkedProjectResourceOwner Owner,
        BoardTask Task,
        int ProjectIndex,
        int StateIndex,
        int MilestoneIndex,
        int TaskOrderIndex);

    private readonly record struct ProjectTaskKey(string ProjectId, string TaskId);

    private sealed class ReadWarningCollector
    {
        private readonly List<LinkedProjectFamilyWarning> warnings = [];
        private bool truncated;

        public ReadWarningCollector(IEnumerable<LinkedProjectFamilyWarning> initial)
        {
            foreach (var warning in initial) Add(warning);
        }

        public IReadOnlyList<LinkedProjectFamilyWarning> Items => warnings;

        public void Add(LinkedProjectFamilyWarning warning)
        {
            if (warnings.Count < LinkedProjectFamilyService.MaximumWarningCount - 1)
            {
                warnings.Add(warning);
                return;
            }

            if (truncated || warnings.Count >= LinkedProjectFamilyService.MaximumWarningCount) return;
            truncated = true;
            warnings.Add(new LinkedProjectFamilyWarning(
                "linked_project_warnings_truncated",
                $"Additional linked-project read warnings were omitted after {warnings.Count} entries.",
                warning.DeclaringProjectId,
                warning.TargetProjectId,
                warning.Alias,
                LinkedProjectResolutionStatus.Invalid));
        }
    }
}
