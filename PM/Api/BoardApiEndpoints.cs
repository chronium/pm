using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;

namespace PM.Api;

public sealed record BoardFilterResponse(string? Track, string? Milestone, string? State);
public sealed record BoardOptionResponse(string Key, string Name, string Priority);
public sealed record DependencyStatusResponse(
    bool Ready,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> WaitingOn,
    IReadOnlyList<string> Missing,
    string Summary);
public sealed record BoardTaskSummaryResponse(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string State,
    DependencyStatusResponse Dependencies,
    string DescriptionPreview,
    DateTime ModifiedAt);
public sealed record BoardStateGroupResponse(string Key, string Name, IReadOnlyList<BoardTaskSummaryResponse> Tasks);
public sealed record BoardMilestoneGroupResponse(string? Key, string Name, IReadOnlyList<BoardStateGroupResponse> States);
public sealed record BoardResponse(
    string ProjectName,
    BoardFilterResponse Filters,
    IReadOnlyList<BoardOptionResponse> Tracks,
    IReadOnlyList<BoardOptionResponse> Milestones,
    IReadOnlyList<BoardOptionResponse> States,
    IReadOnlyList<BoardMilestoneGroupResponse> MilestoneGroups,
    string Revision);

public static class BoardApiEndpoints
{
    public static void MapBoardApi(this RouteGroupBuilder api, BoardService boardService,
        ResourceRevisionService revisions)
    {
        api.MapGet("/board", (HttpRequest request, string? track, string? milestone, string? state) =>
            {
                var query = new BoardQuery(Normalize(track), Normalize(milestone), Normalize(state));
                var result = boardService.GetBoard(query);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var revisionResult = revisions.GetBoardRevision(result.Payload!);
                if (!revisionResult.Success)
                    return ApiResults.Failure(revisionResult.ErrorCode, revisionResult.Message, request.Path);
                var revision = revisionResult.Payload!;
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (conditional != null) return conditional;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(ToResponse(result.Payload!, revision));
            })
            .WithName("GetBoard")
            .WithSummary("Get the task board")
            .Produces<BoardResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");
    }

    private static BoardResponse ToResponse(BoardData board, string revision) => new(
        board.ProjectName,
        new BoardFilterResponse(board.Query.Track, board.Query.Milestone, board.Query.State),
        board.Tracks.Select(ToOption).ToList(),
        board.Milestones.Select(ToOption).ToList(),
        board.States.Select(ToOption).ToList(),
        board.MilestoneGroups.Select(group => new BoardMilestoneGroupResponse(
            group.Key,
            group.Name,
            group.States.Select(state => new BoardStateGroupResponse(
                state.Key,
                state.Name,
                state.Tasks.Select(ToSummary).ToList())).ToList())).ToList(),
        revision);

    internal static BoardTaskSummaryResponse ToSummary(BoardTask task) => new(
        task.Task.Id,
        task.Task.Title,
        task.Track,
        task.Milestone,
        task.Priority,
        task.PrioritySource,
        task.State,
        ToDependencies(task.Dependencies),
        task.DescriptionPreview,
        ToUtc(task.Task.ModifiedAt));

    internal static DependencyStatusResponse ToDependencies(DependencyStatus status) => new(
        status.Ready, status.DependsOn, status.WaitingOn, status.Missing, status.Summary);

    private static BoardOptionResponse ToOption(BoardOption option) =>
        new(option.Key, option.Name, option.Priority);

    internal static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : value.ToUniversalTime();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
