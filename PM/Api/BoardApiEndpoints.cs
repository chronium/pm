using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;

namespace PM.Api;

public sealed record BoardFilterResponse(
    string? Track,
    string? Milestone,
    string? State,
    bool IncludeDelivered);
public sealed record BoardOptionResponse(string Key, string Name, string Priority);
public sealed record BoardNavigationOptionResponse(
    string Key,
    string Name,
    int RemainingCount,
    int ActivationEligibleCount);
public sealed record BoardMilestoneNavigationOptionResponse(
    string Key,
    string Name,
    int RemainingCount,
    int ActivationEligibleCount,
    string Lifecycle,
    IReadOnlyList<string> UnmetActivationTriggers);
public sealed record BoardNavigationResponse(
    int RemainingCount,
    int ActivationEligibleCount,
    IReadOnlyList<BoardNavigationOptionResponse> Tracks,
    IReadOnlyList<BoardMilestoneNavigationOptionResponse> Milestones,
    string Revision);
public sealed record DependencyStatusResponse(
    bool Ready,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> WaitingOn,
    IReadOnlyList<string> Missing,
    string Summary);
public sealed record TaskActivationEligibilityResponse(
    bool IsEligible,
    string? MilestoneLifecycle,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers,
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
    TaskActivationEligibilityResponse Activation,
    string DescriptionPreview,
    DateTime ModifiedAt);
public sealed record BoardStateGroupResponse(string Key, string Name, IReadOnlyList<BoardTaskSummaryResponse> Tasks);
public sealed record BoardMilestoneGroupResponse(
    string? Key,
    string Name,
    string Description,
    string? Lifecycle,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers,
    IReadOnlyList<BoardStateGroupResponse> States);
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
        ResourceRevisionService revisions,
        LinkedProjectReadService? linkedReads = null)
    {
        api.MapGet("/board/navigation", (HttpRequest request, bool includeDelivered = false) =>
            {
                var result = boardService.GetNavigation(includeDelivered);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var revisionResult = revisions.GetBoardRevision(result.Payload!.Board);
                if (!revisionResult.Success)
                    return ApiResults.Failure(revisionResult.ErrorCode, revisionResult.Message, request.Path);
                var revision = revisionResult.Payload!;
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (conditional != null) return conditional;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(ToNavigationResponse(result.Payload, revision));
            })
            .WithName("GetBoardNavigation")
            .WithSummary("Get task scope navigation")
            .Produces<BoardNavigationResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/board", async (HttpRequest request, string? track, string? milestone, string? state,
                bool includeDelivered = false,
                CancellationToken cancellationToken = default) =>
            {
                var query = new BoardQuery(
                    Normalize(track), Normalize(milestone), Normalize(state), includeDelivered);
                var result = boardService.GetBoard(query);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var board = result.Payload!;
                if (linkedReads != null)
                {
                    var enriched = await linkedReads.EnrichBoardAsync(board, cancellationToken: cancellationToken);
                    if (!enriched.Success)
                        return ApiResults.Failure(enriched.ErrorCode, enriched.Message, request.Path);
                    board = enriched.Payload!;
                }

                var revisionResult = revisions.GetBoardRevision(board);
                if (!revisionResult.Success)
                    return ApiResults.Failure(revisionResult.ErrorCode, revisionResult.Message, request.Path);
                var revision = revisionResult.Payload!;
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (conditional != null) return conditional;

                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(ToResponse(board, revision));
            })
            .WithName("GetBoard")
            .WithSummary("Get the task board")
            .Produces<BoardResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");
    }

    internal static BoardNavigationResponse ToNavigationResponse(
        BoardNavigationData navigation,
        string revision) => new(
        navigation.RemainingCount,
        navigation.ActivationEligibleCount,
        navigation.Tracks.Select(ToNavigationOption).ToList(),
        navigation.Milestones.Select(ToNavigationOption).ToList(),
        revision);

    internal static BoardResponse ToResponse(BoardData board, string revision) => new(
        board.ProjectName,
        new BoardFilterResponse(
            board.Query.Track,
            board.Query.Milestone,
            board.Query.State,
            board.Query.IncludeDelivered),
        board.Tracks.Select(ToOption).ToList(),
        board.Milestones.Select(ToOption).ToList(),
        board.States.Select(ToOption).ToList(),
        board.MilestoneGroups.Select(group => new BoardMilestoneGroupResponse(
            group.Key,
            group.Name,
            group.Description,
            group.Lifecycle == null ? null : ToLifecycleValue(group.Lifecycle.Value),
            group.RequiredActivationTriggers,
            group.UnmetActivationTriggers,
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
        ToActivation(task.Activation),
        task.DescriptionPreview,
        ToUtc(task.Task.ModifiedAt));

    internal static DependencyStatusResponse ToDependencies(DependencyStatus status) => new(
        status.Ready,
        status.DependsOn,
        status.WaitingOn,
        status.Missing.Concat(status.Unavailable).Concat(status.Invalid).ToList(),
        status.Summary);

    internal static TaskActivationEligibilityResponse ToActivation(TaskActivationEligibility activation) => new(
        activation.IsEligible,
        activation.MilestoneLifecycle == null ? null : ToLifecycleValue(activation.MilestoneLifecycle.Value),
        activation.RequiredActivationTriggers,
        activation.UnmetActivationTriggers,
        activation.Summary);

    private static BoardOptionResponse ToOption(BoardOption option) =>
        new(option.Key, option.Name, option.Priority);

    private static BoardNavigationOptionResponse ToNavigationOption(BoardNavigationOption option) =>
        new(option.Key, option.Name, option.RemainingCount, option.ActivationEligibleCount);

    private static BoardMilestoneNavigationOptionResponse ToNavigationOption(
        BoardMilestoneNavigationOption option) =>
        new(option.Key, option.Name, option.RemainingCount, option.ActivationEligibleCount,
            ToLifecycleValue(option.Lifecycle), option.UnmetActivationTriggers);

    internal static string ToLifecycleValue(MilestoneLifecycle lifecycle) => lifecycle switch
    {
        MilestoneLifecycle.ReadyToDeliver => "ready_to_deliver",
        MilestoneLifecycle.Delivered => "delivered",
        MilestoneLifecycle.Inactive => "inactive",
        MilestoneLifecycle.Active => "active",
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null),
    };

    internal static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : value.ToUniversalTime();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
