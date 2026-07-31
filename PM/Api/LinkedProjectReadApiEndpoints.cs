using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Api;

public sealed record LinkedProjectContextResponse(
    string ProjectId,
    string Name,
    string Accent,
    string Relationship,
    bool ReadOnly,
    string Revision);

public static class LinkedProjectReadApiEndpoints
{
    private const string ContextKey = "PM.LinkedProjectReadContext";

    public static void MapLinkedProjectReadApi(
        this RouteGroupBuilder api,
        LinkedProjectFamilyService familyService,
        LinkedProjectMutationService mutations)
    {
        var projects = api.MapGroup("/projects/{projectId}")
            .AddEndpointFilter((context, next) => ResolveProject(context, next, familyService, mutations));

        projects.MapGet("/project", (HttpRequest request) =>
            {
                var context = GetContext(request);
                var revision = context.Revisions.GetProjectConfigRevision();
                if (!revision.Success)
                    return ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision.Payload!);
                if (conditional != null) return conditional;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision.Payload!);
                return Results.Ok(new LinkedProjectContextResponse(
                    context.Member.ProjectId,
                    context.Member.Name,
                    context.Root.Config!.Accent,
                    LinkedProjectFamilyService.Format(context.Member.Relationship),
                    context.MutationTarget == null,
                    revision.Payload!));
            })
            .WithName("GetLinkedProjectContext")
            .WithSummary("Get readable linked-project metadata")
            .Produces<LinkedProjectContextResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        projects.MapGet("/settings", (HttpRequest request) =>
                SettingsApiEndpoints.Read(request, GetContext(request).Config, GetContext(request).Revisions))
            .WithName("GetLinkedProjectSettings")
            .WithSummary("Get readable linked-project configuration")
            .Produces<SettingsResponse>()
            .WithRevisionedReadMetadata();

        projects.MapGet("/board/navigation", (HttpRequest request) =>
            {
                var context = GetContext(request);
                var result = context.Board.GetNavigation();
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var revision = context.Revisions.GetBoardRevision(result.Payload!.Board);
                if (!revision.Success)
                    return ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision.Payload!);
                if (conditional != null) return conditional;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision.Payload!);
                return Results.Ok(BoardApiEndpoints.ToNavigationResponse(result.Payload, revision.Payload!));
            })
            .WithName("GetLinkedProjectBoardNavigation")
            .Produces<BoardNavigationResponse>()
            .WithRevisionedReadMetadata();

        projects.MapGet("/board", (HttpRequest request, string? track, string? milestone, string? state) =>
            {
                var context = GetContext(request);
                var query = new BoardQuery(Normalize(track), Normalize(milestone), Normalize(state));
                var result = context.Board.GetBoard(query);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var revision = context.Revisions.GetBoardRevision(result.Payload!);
                if (!revision.Success)
                    return ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision.Payload!);
                if (conditional != null) return conditional;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision.Payload!);
                return Results.Ok(BoardApiEndpoints.ToResponse(result.Payload!, revision.Payload!));
            })
            .WithName("GetLinkedProjectBoard")
            .Produces<BoardResponse>()
            .WithRevisionedReadMetadata();

        projects.MapGet("/tasks/search", (HttpRequest request, string query, int limit = 20,
                string? track = null, string? milestone = null, string? state = null) =>
            {
                var result = GetContext(request).Tasks.SearchTasks(
                    query, limit, new TaskSearchContext(track, milestone, state));
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                return Results.Ok(result.Payload!.Select(item => new TaskSearchResultResponse(
                    item.Task.Id, item.Task.Title, item.State, item.Track, item.Milestone,
                    item.MatchCount, item.Snippet)).ToList());
            })
            .WithName("SearchLinkedProjectTasks")
            .Produces<IReadOnlyList<TaskSearchResultResponse>>();

        projects.MapGet("/tasks/next", (HttpRequest request, string? track = null,
                string? milestone = null, bool readyOnly = true) =>
            {
                var result = GetContext(request).Board.GetNextTask(new NextTaskQuery(
                    Normalize(track), Normalize(milestone), readyOnly), BoardService.WebDescriptionPreviewLength);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var next = result.Payload!;
                return Results.Ok(new NextTaskResponse(
                    next.Found,
                    next.Task == null ? null : BoardApiEndpoints.ToSummary(next.Task),
                    next.Reason));
            })
            .WithName("GetLinkedProjectNextTaskRecommendation")
            .Produces<NextTaskResponse>();

        projects.MapGet("/tasks/{id}", (HttpRequest request, string id) =>
                TaskApiEndpoints.ReadTask(request, id, GetContext(request).Board, GetContext(request).Revisions))
            .WithName("GetLinkedProjectTask")
            .Produces<TaskResponse>()
            .WithRevisionedReadMetadata();

        MapTaskMutations(projects, mutations);

        projects.MapGet("/wiki/search", (HttpRequest request, int limit = 20) =>
            {
                var result = GetContext(request).Wiki.SearchPages(request.Query["query"].ToString(), limit);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                return Results.Ok(result.Payload!.Select(item => new WikiSearchResultResponse(
                    item.Path, item.Title, BoardApiEndpoints.ToUtc(item.ModifiedAt),
                    item.MatchCount, item.Snippet)).ToList());
            })
            .WithName("SearchLinkedProjectWikiPages")
            .Produces<IReadOnlyList<WikiSearchResultResponse>>();

        projects.MapGet("/wiki/pages", (HttpRequest request) =>
            {
                var context = GetContext(request);
                var result = context.Wiki.ListPages();
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var revision = context.Revisions.GetWikiIndexRevision(result.Payload!);
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (conditional != null) return conditional;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(result.Payload!.Select(page => new WikiPageSummaryResponse(
                    page.Path, page.Title, BoardApiEndpoints.ToUtc(page.ModifiedAt))));
            })
            .WithName("ListLinkedProjectWikiPages")
            .Produces<IReadOnlyList<WikiPageSummaryResponse>>()
            .WithRevisionedReadMetadata();

        projects.MapGet("/wiki/pages/{**path}", (HttpRequest request, string path) =>
                WikiApiEndpoints.ReadPage(request, path, GetContext(request).Wiki, GetContext(request).Revisions))
            .WithName("GetLinkedProjectWikiPage")
            .Produces<WikiPageResponse>()
            .WithRevisionedReadMetadata();

        MapWikiMutations(projects, mutations);
    }

    private static void MapTaskMutations(RouteGroupBuilder projects, LinkedProjectMutationService mutations)
    {
        projects.MapPost("/tasks", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var (input, error) = await ApiJsonRequest.Read<CreateTaskRequest>(request, cancellationToken);
            if (error != null) return error;
            if (string.IsNullOrWhiteSpace(input!.Title))
                return TaskApiEndpoints.DomainFailure("invalid_title", "Task title is required.", request);
            if (string.IsNullOrWhiteSpace(input.Track))
                return TaskApiEndpoints.DomainFailure("invalid_track", "Task track is required.", request);

            using var tracker = mutations.Track(writable.Context!.MutationTarget!);
            var result = await writable.Context.Tasks.CreateTask(input.Title, input.Track, input.Milestone,
                input.Description ?? string.Empty, false, cancellationToken);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            var response = TaskApiEndpoints.GetResponse(result.Payload!.Id, writable.Context.Board,
                writable.Context.Revisions, request);
            if (response.Error != null) return response.Error;
            ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return Results.Created($"{request.Path}/{Uri.EscapeDataString(result.Payload.Id)}", response.Value);
        })
        .WithName("CreateLinkedProjectTask")
        .Accepts<CreateTaskRequest>("application/json")
        .Produces<TaskResponse>(StatusCodes.Status201Created)
        .WithResponseETagMetadata(StatusCodes.Status201Created)
        .WithClientHeaderMetadata();

        projects.MapPut("/tasks/{id}", async (HttpRequest request, string id,
            CancellationToken cancellationToken) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var (input, error) = await ApiJsonRequest.Read<UpdateTaskRequest>(request, cancellationToken);
            if (error != null) return error;
            if (input!.Title == null) return TaskApiEndpoints.DomainFailure("invalid_title", "Task title is required.", request);
            if (input.State == null) return TaskApiEndpoints.DomainFailure("invalid_state", "Task state is required.", request);
            if (input.Description == null) return TaskApiEndpoints.DomainFailure("invalid_description", "Task description is required.", request);
            if (input.Priority == null || !TaskApiEndpoints.AcceptedPriorities.Contains(input.Priority.Trim()))
                return TaskApiEndpoints.DomainFailure("invalid_priority",
                    "Task priority must be inherit, none, low, medium, high, or urgent.", request);
            if (input.Placement != null && string.IsNullOrWhiteSpace(input.Placement.Track))
                return TaskApiEndpoints.DomainFailure("invalid_track", "Task track is required.", request);
            if (input.Placement?.Milestone != null && string.IsNullOrWhiteSpace(input.Placement.Milestone))
                return TaskApiEndpoints.DomainFailure("invalid_milestone", "Task milestone must be configured or null.", request);
            var precondition = TaskApiEndpoints.CheckPrecondition(request, id, writable.Context!.Revisions);
            if (precondition != null) return precondition;

            using var tracker = mutations.Track(writable.Context.MutationTarget!);
            var placement = input.Placement == null
                ? null
                : new TaskPlacementUpdate(input.Placement.Track, input.Placement.Milestone);
            var result = writable.Context.Tasks.UpdateTaskDetails(id, input.Title, input.State, input.Description,
                input.Priority, placement);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return TaskApiEndpoints.Refreshed(request, id, writable.Context.Board, writable.Context.Revisions);
        })
        .WithName("UpdateLinkedProjectTask")
        .Accepts<UpdateTaskRequest>("application/json")
        .Produces<TaskResponse>()
        .WithRevisionedMutationMetadata()
        .WithClientHeaderMetadata();

        projects.MapPut("/tasks/{id}/state", async (HttpRequest request, string id,
            CancellationToken cancellationToken) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var (input, error) = await ApiJsonRequest.Read<UpdateTaskStateRequest>(request, cancellationToken);
            if (error != null) return error;
            if (input!.State == null)
                return TaskApiEndpoints.DomainFailure("invalid_state", "Task state is required.", request);
            var precondition = TaskApiEndpoints.CheckPrecondition(request, id, writable.Context!.Revisions);
            if (precondition != null) return precondition;

            using var tracker = mutations.Track(writable.Context.MutationTarget!);
            var result = writable.Context.Tasks.MoveTask(id, input.State);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return TaskApiEndpoints.Refreshed(request, id, writable.Context.Board, writable.Context.Revisions);
        })
        .WithName("UpdateLinkedProjectTaskState")
        .Accepts<UpdateTaskStateRequest>("application/json")
        .Produces<TaskResponse>()
        .WithRevisionedMutationMetadata()
        .WithClientHeaderMetadata();

        projects.MapPost("/tasks/{id}/notes", async (HttpRequest request, string id,
            CancellationToken cancellationToken) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var (input, error) = await ApiJsonRequest.Read<AppendTaskNoteRequest>(request, cancellationToken);
            if (error != null) return error;
            if (input!.Note == null)
                return TaskApiEndpoints.DomainFailure("invalid_note", "Task note is required.", request);
            var precondition = TaskApiEndpoints.CheckPrecondition(request, id, writable.Context!.Revisions);
            if (precondition != null) return precondition;

            using var tracker = mutations.Track(writable.Context.MutationTarget!);
            var result = writable.Context.Tasks.AppendTaskNote(id, input.Note);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return TaskApiEndpoints.Refreshed(request, id, writable.Context.Board, writable.Context.Revisions);
        })
        .WithName("AppendLinkedProjectTaskNote")
        .Accepts<AppendTaskNoteRequest>("application/json")
        .Produces<TaskResponse>()
        .WithRevisionedMutationMetadata()
        .WithClientHeaderMetadata();

        projects.MapDelete("/tasks/{id}", (HttpRequest request, string id) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var precondition = TaskApiEndpoints.CheckPrecondition(request, id, writable.Context!.Revisions);
            if (precondition != null) return precondition;
            using var tracker = mutations.Track(writable.Context.MutationTarget!);
            var result = writable.Context.Tasks.RemoveTask(id);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return Results.NoContent();
        })
        .WithName("DeleteLinkedProjectTask")
        .Produces(StatusCodes.Status204NoContent)
        .WithRevisionedMutationMetadata(StatusCodes.Status204NoContent)
        .WithClientHeaderMetadata();
    }

    private static void MapWikiMutations(RouteGroupBuilder projects, LinkedProjectMutationService mutations)
    {
        projects.MapPost("/wiki/pages", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var (input, error) = await ApiJsonRequest.Read<CreateWikiPageRequest>(request, cancellationToken);
            if (error != null) return error;
            using var tracker = mutations.Track(writable.Context!.MutationTarget!);
            var result = writable.Context.Wiki.CreatePage(input!.Path, input.Title, input.Body ?? string.Empty);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            var response = WikiApiEndpoints.CreateResponse(result.Payload!, writable.Context.Revisions, request);
            if (response.Error != null) return response.Error;
            ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return Results.Created($"{request.Path}/{EncodeWikiPath(response.Value.Path)}", response.Value);
        })
        .WithName("CreateLinkedProjectWikiPage")
        .Accepts<CreateWikiPageRequest>("application/json")
        .Produces<WikiPageResponse>(StatusCodes.Status201Created)
        .WithResponseETagMetadata(StatusCodes.Status201Created)
        .WithClientHeaderMetadata();

        projects.MapPut("/wiki/pages/{**path}", async (HttpRequest request, string path,
            CancellationToken cancellationToken) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var (input, error) = await ApiJsonRequest.Read<UpdateWikiPageBodyRequest>(request, cancellationToken);
            if (error != null) return error;
            if (input!.Body == null)
                return ApiResults.Failure("invalid_wiki_page", "Wiki page body is required.", request.Path);
            var precondition = WikiApiEndpoints.CheckPrecondition(request, path, writable.Context!.Revisions);
            if (precondition != null) return precondition;
            using var tracker = mutations.Track(writable.Context.MutationTarget!);
            var result = writable.Context.Wiki.UpdatePageBody(path, input.Body);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return WikiApiEndpoints.Refreshed(request, result, writable.Context.Revisions);
        })
        .WithName("UpdateLinkedProjectWikiPageBody")
        .Accepts<UpdateWikiPageBodyRequest>("application/json")
        .Produces<WikiPageResponse>()
        .WithRevisionedMutationMetadata()
        .WithClientHeaderMetadata();

        projects.MapPatch("/wiki/pages/{**path}", async (HttpRequest request, string path,
            CancellationToken cancellationToken) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var (input, error) = await ApiJsonRequest.Read<UpdateWikiPageMetadataRequest>(request, cancellationToken);
            if (error != null) return error;
            var precondition = WikiApiEndpoints.CheckPrecondition(request, path, writable.Context!.Revisions);
            if (precondition != null) return precondition;
            using var tracker = mutations.Track(writable.Context.MutationTarget!);
            var result = writable.Context.Wiki.RenamePage(path, input!.Path, input.Title);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return WikiApiEndpoints.Refreshed(request, result, writable.Context.Revisions);
        })
        .WithName("UpdateLinkedProjectWikiPageMetadata")
        .Accepts<UpdateWikiPageMetadataRequest>("application/json")
        .Produces<WikiPageResponse>()
        .WithRevisionedMutationMetadata()
        .WithClientHeaderMetadata();

        projects.MapDelete("/wiki/pages/{**path}", (HttpRequest request, string path) =>
        {
            var writable = GetWritableContext(request);
            if (writable.Error != null) return writable.Error;
            var precondition = WikiApiEndpoints.CheckPrecondition(request, path, writable.Context!.Revisions);
            if (precondition != null) return precondition;
            using var tracker = mutations.Track(writable.Context.MutationTarget!);
            var result = writable.Context.Wiki.RemovePage(path);
            if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
            return Results.NoContent();
        })
        .WithName("DeleteLinkedProjectWikiPage")
        .Produces(StatusCodes.Status204NoContent)
        .WithRevisionedMutationMetadata(StatusCodes.Status204NoContent)
        .WithClientHeaderMetadata();
    }

    private static (LinkedProjectReadContext? Context, IResult? Error) GetWritableContext(HttpRequest request)
    {
        var context = GetContext(request);
        if (context.MutationTarget != null) return (context, null);
        var failure = context.MutationFailure;
        return (null, ApiResults.Failure(
            failure?.ErrorCode ?? "linked_project_write_untrusted",
            failure?.Message ?? $"Linked project {context.Member.ProjectId} is read-only until local write trust is granted.",
            request.Path));
    }

    private static string EncodeWikiPath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static async ValueTask<object?> ResolveProject(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        LinkedProjectFamilyService familyService,
        LinkedProjectMutationService mutations)
    {
        var projectId = context.HttpContext.Request.RouteValues["projectId"]?.ToString();
        var family = await familyService.ResolveAsync(context.HttpContext.RequestAborted);
        if (!family.Success)
            return ApiResults.Failure(family.ErrorCode, family.Message, context.HttpContext.Request.Path);

        var member = family.Payload!.Members.FirstOrDefault(candidate =>
            string.Equals(candidate.ProjectId, projectId, StringComparison.Ordinal));
        if (member == null)
            return ApiResults.Failure(
                "unknown_linked_project",
                $"Project {projectId} is not declared in the active linked-project family.",
                context.HttpContext.Request.Path);
        if (!member.Readable || member.Project == null)
        {
            var repair = member.RepairAction?.DisplayCommand;
            var detail = $"Project {member.Name} is currently unavailable." +
                         (string.IsNullOrWhiteSpace(repair) ? string.Empty : $" Run {repair} to repair the link.");
            return ApiResults.Failure("linked_project_unavailable", detail, context.HttpContext.Request.Path);
        }
        if (!member.Project.TryReloadConfig())
            return ApiResults.Failure(
                "linked_project_unavailable",
                $"Project {member.Name} has an invalid project configuration.",
                context.HttpContext.Request.Path);

        LinkedProjectMutationTarget? mutationTarget = null;
        AppResult<LinkedProjectMutationTarget>? mutationFailure = null;
        if (member.WriteTrusted)
        {
            var resolved = await mutations.ResolveTargetAsync(member.ProjectId,
                cancellationToken: context.HttpContext.RequestAborted);
            if (resolved.Success) mutationTarget = resolved.Payload;
            else mutationFailure = resolved;
        }

        var board = mutationTarget?.Board ?? new BoardService(member.Project);
        context.HttpContext.Items[ContextKey] = new LinkedProjectReadContext(
            member,
            member.Project,
            board,
            new ProjectConfigService(member.Project),
            mutationTarget?.Tasks ?? new TaskService(member.Project, ReadOnlyNextIdService.Instance),
            mutationTarget?.Wiki ?? new WikiService(member.Project),
            mutationTarget?.Revisions ?? new ResourceRevisionService(member.Project, board),
            mutationTarget,
            mutationFailure);
        return await next(context);
    }

    private static LinkedProjectReadContext GetContext(HttpRequest request) =>
        (LinkedProjectReadContext)request.HttpContext.Items[ContextKey]!;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record LinkedProjectReadContext(
        LinkedProjectFamilyMember Member,
        ProjectRoot Root,
        BoardService Board,
        ProjectConfigService Config,
        TaskService Tasks,
        WikiService Wiki,
        ResourceRevisionService Revisions,
        LinkedProjectMutationTarget? MutationTarget,
        AppResult<LinkedProjectMutationTarget>? MutationFailure);

    private sealed class ReadOnlyNextIdService : INextIdService
    {
        public static readonly ReadOnlyNextIdService Instance = new();
        private ReadOnlyNextIdService() { }

        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default) =>
            Task.FromException<int>(new NotSupportedException("Linked-project reads cannot allocate task IDs."));
        public Task<int> PeekNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default) =>
            Task.FromException<int>(new NotSupportedException("Linked-project reads cannot allocate task IDs."));
        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);
        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProjectRegistration>(new NotSupportedException("Linked-project reads cannot register projects."));
        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}

internal static class ProjectMutationApiHeaders
{
    public const string ProjectId = "X-PM-Project-Id";
    public const string ChangedPath = "X-PM-Changed-Path";

    public static void Set(HttpResponse response, ProjectMutationReceipt receipt)
    {
        response.Headers[ProjectId] = receipt.ProjectId;
        foreach (var path in receipt.ChangedPaths)
            response.Headers.Append(ChangedPath, path);
    }
}
