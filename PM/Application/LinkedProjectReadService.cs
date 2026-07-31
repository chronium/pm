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
    private readonly ILinkedProjectGitInspector gitInspector;
    private readonly int maximumListResultCount;

    public LinkedProjectReadService(
        ProjectRoot activeProject,
        LinkedProjectFamilyService familyService,
        INextIdService nextIdService,
        ILinkedProjectGitInspector gitInspector)
        : this(activeProject, familyService, nextIdService, gitInspector, MaximumListResultCount)
    {
    }

    public LinkedProjectReadService(
        ProjectRoot activeProject,
        LinkedProjectFamilyService familyService,
        INextIdService nextIdService,
        ILinkedProjectGitInspector gitInspector,
        int maximumListResultCount)
    {
        this.activeProject = activeProject;
        this.familyService = familyService;
        this.nextIdService = nextIdService;
        this.gitInspector = gitInspector;
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

            var result = new BoardService(member.Project!).GetBoard(query);
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
        return AppResult<LinkedProjectReadResult<BoardTask>>.Ok(
            new LinkedProjectReadResult<BoardTask>(items, warnings.Items, truncated));
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
        var result = new BoardService(member.Project!).GetTask(taskId.Trim());
        if (!result.Success) return Failure<BoardTask>(result.ErrorCode, result.Message);
        if (!member.Project!.TryReadTaskFile(taskId.Trim(), out var markdown))
            return Failure<BoardTask>("missing_task", $"Task {taskId.Trim()} not found.");

        var owner = await BuildOwnerAsync(member, cancellationToken);
        return AppResult<LinkedProjectReadResult<BoardTask>>.Ok(
            new LinkedProjectReadResult<BoardTask>(
                [new LinkedProjectResource<BoardTask>(owner, result.Payload! with { Markdown = markdown })],
                targets.Payload.Warnings));
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

            var result = new TaskService(member.Project!, nextIdService).SearchTasks(query, limit, context);
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
        return AppResult<LinkedProjectReadResult<TaskSearchResult>>.Ok(
            new LinkedProjectReadResult<TaskSearchResult>(items, warnings.Items));
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

        var selected = SelectMember(resolvedFamily, request.ProjectSelector!);
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

    private LinkedProjectFamilyMember CurrentMember(string activeProjectId) =>
        new(activeProjectId, activeProject.Config!.Name, "current", LinkedProjectRelationship.Current,
            LinkedProjectResolutionStatus.Available, LinkedProjectResolutionSource.ActiveProject,
            true, true, activeProject, activeProject.RepositoryPath);

    private static AppResult<LinkedProjectFamilyMember> SelectMember(
        LinkedProjectFamily family,
        string selector)
    {
        selector = selector.Trim();
        if (string.Equals(selector, "current", StringComparison.OrdinalIgnoreCase))
            return AppResult<LinkedProjectFamilyMember>.Ok(
                family.Members.Single(member => member.Relationship == LinkedProjectRelationship.Current));
        if (string.Equals(selector, "parent", StringComparison.OrdinalIgnoreCase))
        {
            var parent = family.Members.SingleOrDefault(member =>
                member.Relationship == LinkedProjectRelationship.Parent);
            return parent == null
                ? AppResult<LinkedProjectFamilyMember>.Fail(
                    "unknown_linked_project", "This project has no linked parent.")
                : AppResult<LinkedProjectFamilyMember>.Ok(parent);
        }

        var byId = family.Members.SingleOrDefault(member =>
            string.Equals(member.ProjectId, selector, StringComparison.Ordinal));
        if (byId != null) return AppResult<LinkedProjectFamilyMember>.Ok(byId);

        var byAlias = family.Members
            .Where(member => member.Alias != null &&
                             string.Equals(member.Alias, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byAlias.Count == 1) return AppResult<LinkedProjectFamilyMember>.Ok(byAlias[0]);
        if (byAlias.Count > 1)
            return AppResult<LinkedProjectFamilyMember>.Fail(
                "ambiguous_linked_project",
                $"Linked-project selector {selector} is ambiguous; use a stable project ID.");

        var candidates = string.Join(", ", family.Members.Take(12).Select(member =>
            member.Alias == null ? member.ProjectId : $"{member.Alias} ({member.ProjectId})"));
        return AppResult<LinkedProjectFamilyMember>.Fail(
            "unknown_linked_project",
            $"Linked project {selector} was not found. Available projects: {candidates}.");
    }

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
