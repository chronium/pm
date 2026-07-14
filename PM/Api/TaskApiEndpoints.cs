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
public sealed record UpdateTaskRequest(string Title, string State, string Description, string Priority);
public sealed record UpdateTaskStateRequest(string State);

public static class TaskApiEndpoints
{
    public static void MapTaskApi(this RouteGroupBuilder api, BoardService boardService,
        TaskService taskService, ResourceRevisionService revisions)
    {
        api.MapGet("/tasks/{id}", (HttpRequest request, string id) => ReadTask(request, id, boardService, revisions))
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

                var response = GetResponse(result.Payload!.Id, boardService, revisions, request);
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

                var precondition = CheckPrecondition(request, id, revisions);
                if (precondition != null) return precondition;
                var result = taskService.UpdateTaskDetails(id, input.Title, input.State, input.Description, input.Priority);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                return Refreshed(request, id, boardService, revisions);
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
                return Refreshed(request, id, boardService, revisions);
            })
            .WithName("UpdateTaskState")
            .WithSummary("Update a task state")
            .Accepts<UpdateTaskStateRequest>("application/json")
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

    private static IResult ReadTask(HttpRequest request, string id, BoardService boardService,
        ResourceRevisionService revisions)
    {
        var response = GetResponse(id, boardService, revisions, request);
        if (response.Error != null) return response.Error;
        var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, response.Value!.Revision);
        if (conditional != null) return conditional;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value.Revision);
        return Results.Ok(response.Value);
    }

    private static IResult Refreshed(HttpRequest request, string id, BoardService boardService,
        ResourceRevisionService revisions)
    {
        var response = GetResponse(id, boardService, revisions, request);
        if (response.Error != null) return response.Error;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
        return Results.Ok(response.Value);
    }

    private static (TaskResponse? Value, IResult? Error) GetResponse(string id, BoardService boardService,
        ResourceRevisionService revisions, HttpRequest request)
    {
        var task = boardService.GetTask(id);
        if (!task.Success) return (null, ApiResults.Failure(task.ErrorCode, task.Message, request.Path));
        var revision = revisions.GetTaskRevision(id);
        if (!revision.Success) return (null, ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path));
        var item = task.Payload!;
        return (new TaskResponse(
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
            revision.Payload!,
            new TaskLocalMetadataResponse(item.FilePath)), null);
    }

    private static IResult? CheckPrecondition(HttpRequest request, string id, ResourceRevisionService revisions)
    {
        var revision = revisions.GetTaskRevision(id);
        return revision.Success
            ? ApiPreconditions.RequireIfMatch(request, revision.Payload!)
            : ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
    }

    private static IResult DomainFailure(string code, string message, HttpRequest request) =>
        ApiResults.Failure(code, message, request.Path);

    private static readonly HashSet<string> AcceptedPriorities =
        ["inherit", "none", "low", "medium", "high", "urgent"];
}
