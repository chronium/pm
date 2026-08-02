using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;

namespace PM.Api;

public sealed record TaskLocalMetadataResponse(string FilePath);
public sealed record TaskResponse(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string PrioritySelection,
    string State,
    DependencyStatusResponse Dependencies,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string Description,
    string Revision,
    TaskLocalMetadataResponse LocalMetadata);
public sealed record CreateTaskRequest(string Title, string Track, string? Milestone = null, string? Description = null);
public sealed record TaskPlacementRequest
{
    public required string Track { get; init; }
    public required string? Milestone { get; init; }
}
public sealed record UpdateTaskRequest(
    string Title,
    string State,
    string Description,
    string Priority,
    TaskPlacementRequest? Placement = null);
public sealed record UpdateTaskStateRequest(string State);
public sealed record AppendTaskNoteRequest(string Note);
public sealed record TaskSearchResultResponse(
    string Id,
    string Title,
    string State,
    string Track,
    string? Milestone,
    int MatchCount,
    string Snippet);
public sealed record NextTaskResponse(bool Found, BoardTaskSummaryResponse? Task, string Reason);

public static class TaskApiEndpoints
{
    public static void MapTaskApi(this RouteGroupBuilder api, BoardService boardService,
        TaskService taskService, ResourceRevisionService revisions,
        LinkedProjectReadService? linkedReads = null)
    {
        api.MapGet("/tasks/search", (HttpRequest request, string query, int limit = 20,
                string? track = null, string? milestone = null, string? state = null) =>
            {
                var result = taskService.SearchTasks(query, limit, new TaskSearchContext(track, milestone, state));
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                return Results.Ok(result.Payload!.Select(item => new TaskSearchResultResponse(
                    item.Task.Id,
                    item.Task.Title,
                    item.State,
                    item.Track,
                    item.Milestone,
                    item.MatchCount,
                    item.Snippet)).ToList());
            })
            .WithName("SearchTasks")
            .WithSummary("Search tasks")
            .Produces<IReadOnlyList<TaskSearchResultResponse>>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/tasks/next", async (HttpRequest request, string? track = null,
                string? milestone = null, bool readyOnly = true,
                CancellationToken cancellationToken = default) =>
            {
                var query = new NextTaskQuery(Normalize(track), Normalize(milestone), readyOnly);
                if (linkedReads == null)
                {
                    var local = boardService.GetNextTask(query, BoardService.WebDescriptionPreviewLength);
                    if (!local.Success) return ApiResults.Failure(local.ErrorCode, local.Message, request.Path);
                    return Results.Ok(new NextTaskResponse(
                        local.Payload!.Found,
                        local.Payload.Task == null ? null : BoardApiEndpoints.ToSummary(local.Payload.Task),
                        local.Payload.Reason));
                }

                var result = await linkedReads.GetNextTaskAsync(
                    new LinkedProjectReadRequest(), query,
                    BoardService.WebDescriptionPreviewLength, cancellationToken);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var next = result.Payload!;
                return Results.Ok(new NextTaskResponse(
                    next.Found,
                    next.Task == null ? null : BoardApiEndpoints.ToSummary(next.Task),
                    next.Reason));
            })
            .WithName("GetNextTaskRecommendation")
            .WithSummary("Get the next recommended task")
            .Produces<NextTaskResponse>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/tasks/{id}", (HttpRequest request, string id, CancellationToken cancellationToken) =>
                ReadCurrentTask(request, id, boardService, revisions, linkedReads, cancellationToken))
            .WithName("GetTask")
            .WithSummary("Get task details")
            .Produces<TaskResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPost("/tasks", async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<CreateTaskRequest>(request, cancellationToken);
                if (error != null) return error;
                if (string.IsNullOrWhiteSpace(input!.Title)) return DomainFailure("invalid_title", "Task title is required.", request);
                if (string.IsNullOrWhiteSpace(input.Track)) return DomainFailure("invalid_track", "Task track is required.", request);

                var result = await taskService.CreateTask(input.Title, input.Track, input.Milestone,
                    input.Description ?? string.Empty, false, cancellationToken);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var response = await GetCurrentResponse(
                    result.Payload!.Id, boardService, revisions, linkedReads, request, cancellationToken);
                if (response.Error != null) return response.Error;
                ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
                return Results.Created($"/api/v1/tasks/{result.Payload.Id}", response.Value);
            })
            .WithName("CreateTask")
            .WithSummary("Create a task")
            .Accepts<CreateTaskRequest>("application/json")
            .Produces<TaskResponse>(StatusCodes.Status201Created)
            .WithResponseETagMetadata(StatusCodes.Status201Created)
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        api.MapPut("/tasks/{id}", async (HttpRequest request, string id, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<UpdateTaskRequest>(request, cancellationToken);
                if (error != null) return error;
                if (input!.Title == null) return DomainFailure("invalid_title", "Task title is required.", request);
                if (input.State == null) return DomainFailure("invalid_state", "Task state is required.", request);
                if (input.Description == null) return DomainFailure("invalid_description", "Task description is required.", request);
                if (input.Priority == null) return DomainFailure("invalid_priority", "Task priority is required.", request);
                if (!AcceptedPriorities.Contains(input.Priority.Trim()))
                    return DomainFailure("invalid_priority",
                        "Task priority must be inherit, none, low, medium, high, or urgent.", request);
                if (input.Placement != null && string.IsNullOrWhiteSpace(input.Placement.Track))
                    return DomainFailure("invalid_track", "Task track is required.", request);
                if (input.Placement?.Milestone != null && string.IsNullOrWhiteSpace(input.Placement.Milestone))
                    return DomainFailure("invalid_milestone", "Task milestone must be configured or null.", request);

                var precondition = CheckPrecondition(request, id, revisions);
                if (precondition != null) return precondition;
                var placement = input.Placement == null
                    ? null
                    : new TaskPlacementUpdate(input.Placement.Track, input.Placement.Milestone);
                var result = taskService.UpdateTaskDetails(id, input.Title, input.State, input.Description,
                    input.Priority, placement);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                return await RefreshedCurrent(
                    request, id, boardService, revisions, linkedReads, cancellationToken);
            })
            .WithName("UpdateTask")
            .WithSummary("Update task details")
            .Accepts<UpdateTaskRequest>("application/json")
            .Produces<TaskResponse>()
            .WithRevisionedMutationMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPut("/tasks/{id}/state", async (HttpRequest request, string id, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<UpdateTaskStateRequest>(request, cancellationToken);
                if (error != null) return error;
                if (input!.State == null) return DomainFailure("invalid_state", "Task state is required.", request);

                var precondition = CheckPrecondition(request, id, revisions);
                if (precondition != null) return precondition;
                var result = taskService.MoveTask(id, input.State);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                return await RefreshedCurrent(
                    request, id, boardService, revisions, linkedReads, cancellationToken);
            })
            .WithName("UpdateTaskState")
            .WithSummary("Update a task state")
            .Accepts<UpdateTaskStateRequest>("application/json")
            .Produces<TaskResponse>()
            .WithRevisionedMutationMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPost("/tasks/{id}/notes", async (HttpRequest request, string id,
                CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<AppendTaskNoteRequest>(request, cancellationToken);
                if (error != null) return error;
                if (input!.Note == null)
                    return DomainFailure("invalid_note", "Task note is required.", request);

                var precondition = CheckPrecondition(request, id, revisions);
                if (precondition != null) return precondition;
                var result = taskService.AppendTaskNote(id, input.Note);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                return await RefreshedCurrent(
                    request, id, boardService, revisions, linkedReads, cancellationToken);
            })
            .WithName("AppendTaskNote")
            .WithSummary("Append a note to a task")
            .Accepts<AppendTaskNoteRequest>("application/json")
            .Produces<TaskResponse>()
            .WithRevisionedMutationMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapDelete("/tasks/{id}", (HttpRequest request, string id) =>
            {
                var precondition = CheckPrecondition(request, id, revisions);
                if (precondition != null) return precondition;
                var result = taskService.RemoveTask(id);
                return result.Success
                    ? Results.NoContent()
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("DeleteTask")
            .WithSummary("Delete a task")
            .Produces(StatusCodes.Status204NoContent)
            .WithRevisionedMutationMetadata(StatusCodes.Status204NoContent)
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");
    }

    internal static IResult ReadTask(HttpRequest request, string id, BoardService boardService,
        ResourceRevisionService revisions)
    {
        var response = GetResponse(id, boardService, revisions, request);
        if (response.Error != null) return response.Error;
        var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, response.Value!.Revision);
        if (conditional != null) return conditional;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value.Revision);
        return Results.Ok(response.Value);
    }

    private static async Task<IResult> ReadCurrentTask(
        HttpRequest request,
        string id,
        BoardService boardService,
        ResourceRevisionService revisions,
        LinkedProjectReadService? linkedReads,
        CancellationToken cancellationToken)
    {
        var response = await GetCurrentResponse(
            id, boardService, revisions, linkedReads, request, cancellationToken);
        if (response.Error != null) return response.Error;
        var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, response.Value!.Revision);
        if (conditional != null) return conditional;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value.Revision);
        return Results.Ok(response.Value);
    }

    internal static IResult Refreshed(HttpRequest request, string id, BoardService boardService,
        ResourceRevisionService revisions)
    {
        var response = GetResponse(id, boardService, revisions, request);
        if (response.Error != null) return response.Error;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
        return Results.Ok(response.Value);
    }

    private static async Task<IResult> RefreshedCurrent(
        HttpRequest request,
        string id,
        BoardService boardService,
        ResourceRevisionService revisions,
        LinkedProjectReadService? linkedReads,
        CancellationToken cancellationToken)
    {
        var response = await GetCurrentResponse(
            id, boardService, revisions, linkedReads, request, cancellationToken);
        if (response.Error != null) return response.Error;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
        return Results.Ok(response.Value);
    }

    internal static (TaskResponse? Value, IResult? Error) GetResponse(string id, BoardService boardService,
        ResourceRevisionService revisions, HttpRequest request)
    {
        var task = boardService.GetTask(id);
        if (!task.Success) return (null, ApiResults.Failure(task.ErrorCode, task.Message, request.Path));
        var revision = revisions.GetTaskRevision(id);
        if (!revision.Success) return (null, ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path));
        return (ToResponse(task.Payload!, revision.Payload!), null);
    }

    private static async Task<(TaskResponse? Value, IResult? Error)> GetCurrentResponse(
        string id,
        BoardService boardService,
        ResourceRevisionService revisions,
        LinkedProjectReadService? linkedReads,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var task = boardService.GetTask(id);
        if (!task.Success) return (null, ApiResults.Failure(task.ErrorCode, task.Message, request.Path));
        var item = task.Payload!;
        if (linkedReads != null)
        {
            var enriched = await linkedReads.EnrichCurrentTaskAsync(item, cancellationToken);
            if (!enriched.Success)
                return (null, ApiResults.Failure(enriched.ErrorCode, enriched.Message, request.Path));
            item = enriched.Payload!;
        }

        var revision = revisions.GetTaskRevision(id);
        if (!revision.Success) return (null, ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path));
        return (ToResponse(item, revision.Payload!), null);
    }

    internal static TaskResponse ToResponse(BoardTask item, string revision) => new(
            item.Task.Id,
            item.Task.Title,
            item.Track,
            item.Milestone,
            item.Priority,
            item.PrioritySource,
            item.Task.Priority ?? "inherit",
            item.State,
            BoardApiEndpoints.ToDependencies(item.Dependencies),
            BoardApiEndpoints.ToUtc(item.Task.CreatedAt),
            BoardApiEndpoints.ToUtc(item.Task.ModifiedAt),
            item.Task.Description,
            revision,
            new TaskLocalMetadataResponse(item.FilePath));

    internal static IResult? CheckPrecondition(HttpRequest request, string id, ResourceRevisionService revisions)
    {
        var revision = revisions.GetTaskRevision(id);
        return revision.Success
            ? ApiPreconditions.RequireIfMatch(request, revision.Payload!)
            : ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
    }

    internal static IResult DomainFailure(string code, string message, HttpRequest request) =>
        ApiResults.Failure(code, message, request.Path);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static readonly HashSet<string> AcceptedPriorities =
        ["inherit", "none", "low", "medium", "high", "urgent"];
}
