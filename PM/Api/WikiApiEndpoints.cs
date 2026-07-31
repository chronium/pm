using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using PM.Application;

namespace PM.Api;

public sealed record WikiPageSummaryResponse(string Path, string Title, DateTime ModifiedAt);
public sealed record WikiSearchResultResponse(
    string Path,
    string Title,
    DateTime ModifiedAt,
    int MatchCount,
    string Snippet);
public sealed record WikiPageLocalMetadataResponse(string FilePath);
public sealed record WikiPageResponse(
    string Path,
    string Title,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string Body,
    string Revision,
    WikiPageLocalMetadataResponse LocalMetadata);
public sealed record CreateWikiPageRequest(string Path, string Title, string? Body = null);
public sealed record UpdateWikiPageBodyRequest(string Body);
public sealed record UpdateWikiPageMetadataRequest(string Path, string Title);

public static class WikiApiEndpoints
{
    public static void MapWikiApi(this RouteGroupBuilder api, WikiService wikiService,
        ResourceRevisionService revisions)
    {
        api.MapGet("/wiki/search", (HttpRequest request, int limit = 20) =>
            {
                var query = request.Query["query"].ToString();
                var result = wikiService.SearchPages(query, limit);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                return Results.Ok(result.Payload!.Select(item => new WikiSearchResultResponse(
                    item.Path,
                    item.Title,
                    BoardApiEndpoints.ToUtc(item.ModifiedAt),
                    item.MatchCount,
                    item.Snippet)).ToList());
            })
            .WithName("SearchWikiPages")
            .WithSummary("Search wiki pages")
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters ??= [];
                operation.Parameters.Insert(0, new OpenApiParameter
                {
                    Name = "query",
                    In = ParameterLocation.Query,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
                return Task.CompletedTask;
            })
            .Produces<IReadOnlyList<WikiSearchResultResponse>>()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/wiki/pages", (HttpRequest request) =>
            {
                var result = wikiService.ListPages();
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var pages = result.Payload!;
                var revision = revisions.GetWikiIndexRevision(pages);
                var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (conditional != null) return conditional;
                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(pages.Select(page => new WikiPageSummaryResponse(
                    page.Path,
                    page.Title,
                    BoardApiEndpoints.ToUtc(page.ModifiedAt))));
            })
            .WithName("ListWikiPages")
            .WithSummary("List wiki pages")
            .Produces<IReadOnlyList<WikiPageSummaryResponse>>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

        api.MapPost("/wiki/pages", async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<CreateWikiPageRequest>(request, cancellationToken);
                if (error != null) return error;

                var result = wikiService.CreatePage(input!.Path, input.Title, input.Body ?? string.Empty);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

                var response = CreateResponse(result.Payload!, revisions, request);
                if (response.Error != null) return response.Error;
                ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
                return Results.Created(PageLocation(response.Value.Path), response.Value);
            })
            .WithName("CreateWikiPage")
            .WithSummary("Create a wiki page")
            .Accepts<CreateWikiPageRequest>("application/json")
            .Produces<WikiPageResponse>(StatusCodes.Status201Created)
            .WithResponseETagMetadata(StatusCodes.Status201Created)
            .WithClientHeaderMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        api.MapGet("/wiki/pages/{**path}", (HttpRequest request, string path) =>
            ReadPage(request, path, wikiService, revisions))
            .WithName("GetWikiPage")
            .WithSummary("Get a wiki page")
            .Produces<WikiPageResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPut("/wiki/pages/{**path}", async (HttpRequest request, string path,
                CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<UpdateWikiPageBodyRequest>(request, cancellationToken);
                if (error != null) return error;
                if (input!.Body == null)
                    return ApiResults.Failure("invalid_wiki_page", "Wiki page body is required.", request.Path);

                var precondition = CheckPrecondition(request, path, revisions);
                if (precondition != null) return precondition;
                var result = wikiService.UpdatePageBody(path, input.Body);
                return Refreshed(request, result, revisions);
            })
            .WithName("UpdateWikiPageBody")
            .WithSummary("Update a wiki page body")
            .Accepts<UpdateWikiPageBodyRequest>("application/json")
            .Produces<WikiPageResponse>()
            .WithRevisionedMutationMetadata()
            .WithClientHeaderMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPatch("/wiki/pages/{**path}", async (HttpRequest request, string path,
                CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<UpdateWikiPageMetadataRequest>(request, cancellationToken);
                if (error != null) return error;

                var precondition = CheckPrecondition(request, path, revisions);
                if (precondition != null) return precondition;
                var result = wikiService.RenamePage(path, input!.Path, input.Title);
                return Refreshed(request, result, revisions);
            })
            .WithName("UpdateWikiPageMetadata")
            .WithSummary("Update wiki page metadata")
            .Accepts<UpdateWikiPageMetadataRequest>("application/json")
            .Produces<WikiPageResponse>()
            .WithRevisionedMutationMetadata()
            .WithClientHeaderMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        api.MapDelete("/wiki/pages/{**path}", (HttpRequest request, string path) =>
            {
                var precondition = CheckPrecondition(request, path, revisions);
                if (precondition != null) return precondition;
                var result = wikiService.RemovePage(path);
                return result.Success
                    ? Results.NoContent()
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("DeleteWikiPage")
            .WithSummary("Delete a wiki page")
            .Produces(StatusCodes.Status204NoContent)
            .WithRevisionedMutationMetadata(StatusCodes.Status204NoContent)
            .WithClientHeaderMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");
    }

    internal static IResult ReadPage(HttpRequest request, string path, WikiService wikiService,
        ResourceRevisionService revisions)
    {
        var result = wikiService.ReadPage(path);
        if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);

        var response = CreateResponse(result.Payload!, revisions, request);
        if (response.Error != null) return response.Error;
        var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, response.Value!.Revision);
        if (conditional != null) return conditional;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value.Revision);
        return Results.Ok(response.Value);
    }

    internal static IResult Refreshed(HttpRequest request, AppResult<WikiPageData> result,
        ResourceRevisionService revisions)
    {
        if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
        var response = CreateResponse(result.Payload!, revisions, request);
        if (response.Error != null) return response.Error;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
        return Results.Ok(response.Value);
    }

    internal static (WikiPageResponse? Value, IResult? Error) CreateResponse(WikiPageData page,
        ResourceRevisionService revisions, HttpRequest request)
    {
        var revision = revisions.GetWikiPageRevision(page.Path);
        if (!revision.Success)
            return (null, ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path));

        return (new WikiPageResponse(
            page.Path,
            page.Title,
            BoardApiEndpoints.ToUtc(page.CreatedAt),
            BoardApiEndpoints.ToUtc(page.ModifiedAt),
            page.Body,
            revision.Payload!,
            new WikiPageLocalMetadataResponse(page.FilePath)), null);
    }

    internal static IResult? CheckPrecondition(HttpRequest request, string path,
        ResourceRevisionService revisions)
    {
        var revision = revisions.GetWikiPageRevision(path);
        return revision.Success
            ? ApiPreconditions.RequireIfMatch(request, revision.Payload!)
            : ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
    }

    internal static string PageLocation(string path) =>
        $"{ApiV1Endpoints.Prefix}/wiki/pages/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";
}
