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
        LinkedProjectFamilyService familyService)
    {
        var projects = api.MapGroup("/projects/{projectId}")
            .AddEndpointFilter((context, next) => ResolveProject(context, next, familyService));

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
                    true,
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
    }

    private static async ValueTask<object?> ResolveProject(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        LinkedProjectFamilyService familyService)
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

        var board = new BoardService(member.Project);
        context.HttpContext.Items[ContextKey] = new LinkedProjectReadContext(
            member,
            member.Project,
            board,
            new ProjectConfigService(member.Project),
            new TaskService(member.Project, ReadOnlyNextIdService.Instance),
            new WikiService(member.Project),
            new ResourceRevisionService(member.Project, board));
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
        ResourceRevisionService Revisions);

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
