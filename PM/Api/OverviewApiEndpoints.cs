using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;

namespace PM.Api;

[JsonConverter(typeof(JsonStringEnumConverter<OverviewDocumentStatusResponse>))]
public enum OverviewDocumentStatusResponse
{
    [JsonStringEnumMemberName("disabled")]
    Disabled,
    [JsonStringEnumMemberName("ready")]
    Ready,
    [JsonStringEnumMemberName("invalid")]
    Invalid,
}

public sealed record OverviewDocumentResponse(
    OverviewDocumentStatusResponse Status,
    string? ProjectId,
    string ProjectName,
    string DocumentTitle,
    OverviewCompositionResponse? Composition,
    IReadOnlyList<OverviewIssueResponse> Issues,
    string Revision);

public sealed record OverviewIssueResponse(string Code, string Message, string Path);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "layout")]
[JsonDerivedType(typeof(SingleOverviewCompositionResponse), "single")]
[JsonDerivedType(typeof(SplitOverviewCompositionResponse), "split")]
public abstract record OverviewCompositionResponse;

public sealed record SingleOverviewCompositionResponse(
    IReadOnlyList<OverviewSectionResponse> Sections) : OverviewCompositionResponse;

public sealed record SplitOverviewCompositionResponse(
    IReadOnlyList<OverviewSectionResponse> Primary,
    IReadOnlyList<OverviewSectionResponse> Secondary,
    IReadOnlyList<OverviewSectionResponse> After) : OverviewCompositionResponse;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HeroOverviewSectionResponse), "hero")]
[JsonDerivedType(typeof(MilestoneOverviewSectionResponse), "milestone")]
[JsonDerivedType(typeof(TasksOverviewSectionResponse), "tasks")]
[JsonDerivedType(typeof(WikiOverviewSectionResponse), "wiki")]
[JsonDerivedType(typeof(MarkdownOverviewSectionResponse), "markdown")]
[JsonDerivedType(typeof(CopyrightOverviewSectionResponse), "copyright")]
public abstract record OverviewSectionResponse;

public sealed record HeroOverviewSectionResponse(
    string Title,
    string? Description) : OverviewSectionResponse;

public sealed record MilestoneOverviewSectionResponse(
    string Title,
    OverviewMilestoneResponse? Milestone) : OverviewSectionResponse;

public sealed record TasksOverviewSectionResponse(
    string Title,
    IReadOnlyList<BoardTaskSummaryResponse> Tasks) : OverviewSectionResponse;

public sealed record WikiOverviewSectionResponse(
    string Title,
    IReadOnlyList<OverviewWikiPageResponse> Pages) : OverviewSectionResponse;

public sealed record MarkdownOverviewSectionResponse(
    string Title,
    string SourcePath,
    string Body) : OverviewSectionResponse;

public sealed record CopyrightOverviewSectionResponse(string Notice) : OverviewSectionResponse;

public sealed record OverviewMilestoneResponse(
    string Key,
    string Title,
    string Description,
    string Priority,
    string Lifecycle,
    int AssignedTaskCount,
    int DoneTaskCount,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers);

public sealed record OverviewWikiPageResponse(string Path, string Title, DateTime ModifiedAt);

public static class OverviewApiEndpoints
{
    public static void MapOverviewApi(this RouteGroupBuilder api, OverviewService overviewService)
    {
        var endpoint = api.MapGet("/overview", (HttpRequest request, CancellationToken cancellationToken) =>
            Read(request, overviewService, null, cancellationToken));
        ConfigureEndpoint(endpoint, "GetOverview", false);
    }

    internal static void MapLinkedOverviewApi(
        this RouteGroupBuilder api,
        OverviewService overviewService)
    {
        var endpoint = api.MapGet("/overview",
            (HttpRequest request, string projectId, CancellationToken cancellationToken) =>
                Read(request, overviewService, projectId, cancellationToken));
        ConfigureEndpoint(endpoint, "GetLinkedProjectOverview", true);
    }

    private static async Task<IResult> Read(
        HttpRequest request,
        OverviewService overviewService,
        string? projectSelector,
        CancellationToken cancellationToken)
    {
        var result = await overviewService.ResolveAsync(projectSelector, cancellationToken);
        if (!result.Success)
            return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

        var response = ToResponse(result.Payload!);
        var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, response.Revision);
        if (conditional != null) return conditional;

        ApiPreconditions.SetETag(request.HttpContext.Response, response.Revision);
        return Results.Ok(response);
    }

    private static void ConfigureEndpoint(
        RouteHandlerBuilder endpoint,
        string operationName,
        bool linkedProject)
    {
        endpoint
            .WithName(operationName)
            .WithSummary(linkedProject ? "Get a linked project's Overview" : "Get the project Overview")
            .Produces<OverviewDocumentResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");
        if (linkedProject)
            endpoint.Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");
    }

    internal static OverviewDocumentResponse ToResponse(OverviewDocument document) => new(
        document.Status switch
        {
            OverviewDocumentStatus.Disabled => OverviewDocumentStatusResponse.Disabled,
            OverviewDocumentStatus.Ready => OverviewDocumentStatusResponse.Ready,
            OverviewDocumentStatus.Invalid => OverviewDocumentStatusResponse.Invalid,
            _ => throw new ArgumentOutOfRangeException(nameof(document), document.Status, null),
        },
        document.ProjectId,
        document.ProjectName,
        document.DocumentTitle,
        document.Composition == null ? null : ToResponse(document.Composition),
        document.Issues.Select(issue => new OverviewIssueResponse(
            issue.Code, issue.Message, issue.Path)).ToList(),
        document.Revision);

    private static OverviewCompositionResponse ToResponse(OverviewComposition composition) => composition switch
    {
        SingleOverviewComposition single => new SingleOverviewCompositionResponse(
            single.Sections.Select(ToResponse).ToList()),
        SplitOverviewComposition split => new SplitOverviewCompositionResponse(
            split.Primary.Select(ToResponse).ToList(),
            split.Secondary.Select(ToResponse).ToList(),
            split.After.Select(ToResponse).ToList()),
        _ => throw new ArgumentOutOfRangeException(nameof(composition), composition, null),
    };

    private static OverviewSectionResponse ToResponse(OverviewSection section) => section switch
    {
        HeroOverviewSection hero => new HeroOverviewSectionResponse(hero.Title, hero.Description),
        MilestoneOverviewSection milestone => new MilestoneOverviewSectionResponse(
            milestone.Title,
            milestone.Milestone == null ? null : ToResponse(milestone.Milestone)),
        TasksOverviewSection tasks => new TasksOverviewSectionResponse(
            tasks.Title, tasks.Tasks.Select(ToResponse).ToList()),
        WikiOverviewSection wiki => new WikiOverviewSectionResponse(
            wiki.Title, wiki.Pages.Select(ToResponse).ToList()),
        MarkdownOverviewSection markdown => new MarkdownOverviewSectionResponse(
            markdown.Title, markdown.SourcePath, markdown.Body),
        CopyrightOverviewSection copyright => new CopyrightOverviewSectionResponse(copyright.Notice),
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    private static OverviewMilestoneResponse ToResponse(OverviewMilestone milestone) => new(
        milestone.Key,
        milestone.Title,
        milestone.Description,
        milestone.Priority,
        BoardApiEndpoints.ToLifecycleValue(milestone.Lifecycle),
        milestone.AssignedTaskCount,
        milestone.DoneTaskCount,
        milestone.RequiredActivationTriggers,
        milestone.UnmetActivationTriggers);

    private static BoardTaskSummaryResponse ToResponse(OverviewTask task) => new(
        task.Id,
        task.Title,
        task.Track,
        task.Milestone,
        task.Priority,
        task.PrioritySource,
        task.State,
        BoardApiEndpoints.ToDependencies(task.Dependencies),
        BoardApiEndpoints.ToActivation(task.Activation),
        task.DescriptionPreview,
        BoardApiEndpoints.ToUtc(task.ModifiedAt));

    private static OverviewWikiPageResponse ToResponse(OverviewWikiPage page) => new(
        page.Path,
        page.Title,
        BoardApiEndpoints.ToUtc(page.ModifiedAt));
}
