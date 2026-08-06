using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PM.Api;
using PM.Wiki;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task WikiSearchReturnsRankedLimitedResultsWithoutLocalPaths()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.WriteWikiPage(Page("guides/rendering", "Rendering", "Render pipeline and render output"));
        root.WriteWikiPage(Page("render-notes", "Notes", "A render checklist"));
        root.WriteWikiPage(Page("unrelated", "Other", "Nothing relevant"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/wiki/search?query=render&limit=1");
            var results = await response.Content.ReadFromJsonAsync<List<WikiSearchResultResponse>>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = Assert.Single(results!);
            Assert.Equal("guides/rendering", result.Path);
            Assert.Equal("Rendering", result.Title);
            Assert.True(result.MatchCount >= 4);
            Assert.Contains("render", result.Snippet, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(DateTimeKind.Utc, result.ModifiedAt.Kind);

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement[0];
            Assert.False(json.TryGetProperty("filePath", out _));
            Assert.Equal(new[] { "path", "title", "modifiedAt", "matchCount", "snippet" },
                json.EnumerateObject().Select(property => property.Name));
        }
    }

    [Fact]
    public async Task WikiSearchReturnsStandardProblemsForMissingQueryAndInvalidMarkdown()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var absent = await client.GetAsync("/api/v1/wiki/search");
            Assert.Equal(HttpStatusCode.BadRequest, absent.StatusCode);
            Assert.Equal("application/problem+json", absent.Content.Headers.ContentType?.MediaType);
            Assert.Equal("invalid_wiki_query",
                (await absent.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);

            var missing = await client.GetAsync("/api/v1/wiki/search?query=%20");
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal("invalid_wiki_query",
                (await missing.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);

            await File.WriteAllTextAsync(Path.Combine(root.WikiPath, "broken.md"), "not front matter");
            var invalid = await client.GetAsync("/api/v1/wiki/search?query=broken");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_wiki_markdown",
                (await invalid.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Fact]
    public async Task WikiListReturnsOrderedFlatPagesWithoutSyntheticFolders()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.WriteWikiPage(Page("z-last", "Last", "Last body"));
        root.WriteWikiPage(Page("architecture/rendering/canvas", "Canvas", "Nested body"));
        root.WriteWikiPage(Page("architecture/overview", "Overview", "Overview body"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/wiki/pages");
            var pages = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Headers.ETag);
            Assert.Equal(new[] { "architecture/overview", "architecture/rendering/canvas", "z-last" },
                pages.EnumerateArray().Select(page => page.GetProperty("path").GetString()));
            Assert.All(pages.EnumerateArray(), page =>
            {
                Assert.True(page.TryGetProperty("title", out _));
                Assert.True(page.TryGetProperty("modifiedAt", out _));
                Assert.False(page.TryGetProperty("body", out _));
                Assert.False(page.TryGetProperty("localMetadata", out _));
            });
            Assert.DoesNotContain(pages.EnumerateArray(), page =>
                page.GetProperty("path").GetString() is "architecture" or "architecture/rendering");
        }
    }

    [Fact]
    public async Task WikiListRevisionIsDeterministicAndHonorsConditionalReads()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.WriteWikiPage(Page("b", "Second", "Body"));
        root.WriteWikiPage(Page("a", "First", "Body"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var first = await client.GetAsync("/api/v1/wiki/pages");
            var firstTag = first.Headers.ETag?.Tag;
            Assert.Matches("^\"[0-9a-f]{64}\"$", firstTag!);

            using var matching = new HttpRequestMessage(HttpMethod.Get, "/api/v1/wiki/pages");
            matching.Headers.TryAddWithoutValidation("If-None-Match", firstTag);
            var unchanged = await client.SendAsync(matching);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
            Assert.Equal(firstTag, unchanged.Headers.ETag?.Tag);
            Assert.Equal(string.Empty, await unchanged.Content.ReadAsStringAsync());

            root.WriteWikiPage(Page("a", "Changed", "Body"));
            using var stale = new HttpRequestMessage(HttpMethod.Get, "/api/v1/wiki/pages");
            stale.Headers.TryAddWithoutValidation("If-None-Match", firstTag);
            var changed = await client.SendAsync(stale);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.NotEqual(firstTag, changed.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task WikiListReturnsEmptyArrayForEmptyWiki()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var pages = await client.GetFromJsonAsync<List<WikiPageSummaryResponse>>("/api/v1/wiki/pages");
            Assert.Empty(pages!);
        }
    }

    [Fact]
    public async Task WikiCreateAndReadReturnCanonicalBodyRevisionMetadataAndEncodedLocation()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var create = WikiMutation(HttpMethod.Post, "/api/v1/wiki/pages",
                JsonContent.Create(new { path = " guides/C# & APIs.md ", title = " API Guide ", body = "# Intro\n\nRaw <b>Markdown</b>" }));
            var createdResponse = await client.SendAsync(create);
            var createdJson = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync()).RootElement;

            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            Assert.Equal("/api/v1/wiki/pages/guides/C%23%20%26%20APIs",
                createdResponse.Headers.Location?.OriginalString);
            Assert.Equal("guides/C# & APIs", createdJson.GetProperty("path").GetString());
            Assert.Equal("API Guide", createdJson.GetProperty("title").GetString());
            Assert.Equal("# Intro\n\nRaw <b>Markdown</b>", createdJson.GetProperty("body").GetString());
            Assert.Matches("^[0-9a-f]{64}$", createdJson.GetProperty("revision").GetString()!);
            Assert.Equal(ApiPreconditions.FormatETag(createdJson.GetProperty("revision").GetString()!),
                createdResponse.Headers.ETag?.Tag);
            Assert.Equal(Path.Combine(root.WikiPath, "guides", "C# & APIs.md"),
                createdJson.GetProperty("localMetadata").GetProperty("filePath").GetString());
            Assert.False(createdJson.TryGetProperty("markdown", out _));
            Assert.False(createdJson.TryGetProperty("html", out _));

            var readResponse = await client.GetAsync("/api/v1/wiki/pages/guides/C%23%20%26%20APIs");
            var read = await readResponse.Content.ReadFromJsonAsync<WikiPageResponse>();
            Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
            Assert.Equal(createdJson.GetProperty("revision").GetString(), read!.Revision);
            Assert.Equal(DateTimeKind.Utc, read.CreatedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, read.ModifiedAt.Kind);

            using var conditional = new HttpRequestMessage(HttpMethod.Get,
                "/api/v1/wiki/pages/guides/C%23%20%26%20APIs");
            conditional.Headers.TryAddWithoutValidation("If-None-Match", readResponse.Headers.ETag?.Tag);
            var notModified = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Equal(string.Empty, await notModified.Content.ReadAsStringAsync());
            Assert.Equal(readResponse.Headers.ETag?.Tag, notModified.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task WikiCreateDefaultsBodyAndRejectsDuplicatesInvalidInputAndMissingClient()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var create = WikiMutation(HttpMethod.Post, "/api/v1/wiki/pages",
                JsonContent.Create(new { path = "notes.md", title = "Notes" }));
            var response = await client.SendAsync(create);
            Assert.Equal(string.Empty, (await response.Content.ReadFromJsonAsync<WikiPageResponse>())!.Body);
            Assert.Equal("/api/v1/wiki/pages/notes", response.Headers.Location?.OriginalString);

            using var duplicate = WikiMutation(HttpMethod.Post, "/api/v1/wiki/pages",
                JsonContent.Create(new { path = "notes", title = "Again" }));
            Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(duplicate)).StatusCode);

            using var invalidPath = WikiMutation(HttpMethod.Post, "/api/v1/wiki/pages",
                JsonContent.Create(new { path = "../escape", title = "Escape" }));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidPath)).StatusCode);

            using var invalidTitle = WikiMutation(HttpMethod.Post, "/api/v1/wiki/pages",
                JsonContent.Create(new { path = "untitled", title = " " }));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidTitle)).StatusCode);

            var missingClient = await client.PostAsJsonAsync("/api/v1/wiki/pages",
                new { path = "clientless", title = "Clientless" });
            Assert.Equal(HttpStatusCode.BadRequest, missingClient.StatusCode);
            Assert.Equal("missing_client_header",
                (await missingClient.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Fact]
    public async Task WikiBodyUpdatePreservesMetadataAndReturnsNewRevision()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var modifiedAt = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc);
        root.WriteWikiPage(new WikiPage
        {
            Path = "guides/start",
            Title = "Start",
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            Body = "Before",
        });
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var beforeResponse = await client.GetAsync("/api/v1/wiki/pages/guides/start");
            var before = await beforeResponse.Content.ReadFromJsonAsync<WikiPageResponse>();
            using var update = WikiMutation(HttpMethod.Put, "/api/v1/wiki/pages/guides/start",
                JsonContent.Create(new { body = "After" }), beforeResponse.Headers.ETag?.Tag);
            var updatedResponse = await client.SendAsync(update);
            var updated = await updatedResponse.Content.ReadFromJsonAsync<WikiPageResponse>();

            Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
            Assert.Equal(before!.Path, updated!.Path);
            Assert.Equal(before.Title, updated.Title);
            Assert.Equal(createdAt, updated.CreatedAt);
            Assert.True(updated.ModifiedAt > modifiedAt);
            Assert.Equal("After", updated.Body);
            Assert.NotEqual(before.Revision, updated.Revision);
            Assert.Equal(ApiPreconditions.FormatETag(updated.Revision), updatedResponse.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task WikiMetadataUpdatesTitlePathAndBothAndRejectsDestinationConflict()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.WriteWikiPage(Page("source/page", "Source", "Body"));
        root.WriteWikiPage(Page("occupied", "Occupied", "Other"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var firstRead = await client.GetAsync("/api/v1/wiki/pages/source/page");
            using var titleOnly = WikiMutation(HttpMethod.Patch, "/api/v1/wiki/pages/source/page",
                JsonContent.Create(new { path = "source/page", title = "Retitled" }), firstRead.Headers.ETag?.Tag);
            var titleResponse = await client.SendAsync(titleOnly);
            var titled = await titleResponse.Content.ReadFromJsonAsync<WikiPageResponse>();
            Assert.Equal("source/page", titled!.Path);
            Assert.Equal("Retitled", titled.Title);

            using var pathOnly = WikiMutation(HttpMethod.Patch, "/api/v1/wiki/pages/source/page",
                JsonContent.Create(new { path = "reference/page", title = "Retitled" }), titleResponse.Headers.ETag?.Tag);
            var pathResponse = await client.SendAsync(pathOnly);
            var moved = await pathResponse.Content.ReadFromJsonAsync<WikiPageResponse>();
            Assert.Equal("reference/page", moved!.Path);
            Assert.Equal("Retitled", moved.Title);
            Assert.False(File.Exists(Path.Combine(root.WikiPath, "source", "page.md")));
            Assert.False(Directory.Exists(Path.Combine(root.WikiPath, "source")));

            using var combined = WikiMutation(HttpMethod.Patch, "/api/v1/wiki/pages/reference/page",
                JsonContent.Create(new { path = "final/page", title = "Final" }), pathResponse.Headers.ETag?.Tag);
            var combinedResponse = await client.SendAsync(combined);
            var final = await combinedResponse.Content.ReadFromJsonAsync<WikiPageResponse>();
            Assert.Equal("final/page", final!.Path);
            Assert.Equal("Final", final.Title);
            Assert.Equal(ApiPreconditions.FormatETag(final.Revision), combinedResponse.Headers.ETag?.Tag);

            using var conflict = WikiMutation(HttpMethod.Patch, "/api/v1/wiki/pages/final/page",
                JsonContent.Create(new { path = "occupied", title = "Conflict" }), combinedResponse.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(conflict)).StatusCode);
        }
    }

    [Fact]
    public async Task WikiMutationsEnforcePreconditionsJsonAndContentTypeAndDeleteCleansDirectories()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.WriteWikiPage(Page("nested/deep/page", "Page", "Body"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var missing = WikiMutation(HttpMethod.Put, "/api/v1/wiki/pages/nested/deep/page",
                JsonContent.Create(new { body = "No" }));
            Assert.Equal(HttpStatusCode.PreconditionRequired, (await client.SendAsync(missing)).StatusCode);

            using var stale = WikiMutation(HttpMethod.Delete, "/api/v1/wiki/pages/nested/deep/page", null, "\"stale\"");
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);

            using var malformed = WikiMutation(HttpMethod.Put, "/api/v1/wiki/pages/nested/deep/page",
                new StringContent("{", Encoding.UTF8, "application/json"), "*");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(malformed)).StatusCode);

            using var unsupported = WikiMutation(HttpMethod.Put, "/api/v1/wiki/pages/nested/deep/page",
                new StringContent("body", Encoding.UTF8, "text/plain"), "*");
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.SendAsync(unsupported)).StatusCode);

            using var delete = WikiMutation(HttpMethod.Delete, "/api/v1/wiki/pages/nested/deep/page", null, "*");
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
            Assert.False(Directory.Exists(Path.Combine(root.WikiPath, "nested")));
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync("/api/v1/wiki/pages/nested/deep/page")).StatusCode);
        }
    }

    [Fact]
    public async Task WikiInvalidRevisionPathRemainsInvalidAndOpenApiDescribesContracts()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var revisions = new ResourceRevisionService(root, TestBoardServices.Create(root));
        Assert.Equal("invalid_wiki_path", revisions.GetWikiPageRevision("../escape").ErrorCode);

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
            var paths = document.GetProperty("paths");
            var search = paths.GetProperty("/api/v1/wiki/search").GetProperty("get");
            Assert.Equal("SearchWikiPages", search.GetProperty("operationId").GetString());
            Assert.Contains(search.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "query" &&
                parameter.GetProperty("required").GetBoolean());
            Assert.Contains(search.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "limit" &&
                (!parameter.TryGetProperty("required", out var required) || !required.GetBoolean()));
            Assert.True(paths.TryGetProperty("/api/v1/wiki/pages", out var collection));
            Assert.Equal("ListWikiPages", collection.GetProperty("get").GetProperty("operationId").GetString());
            Assert.Equal("CreateWikiPage", collection.GetProperty("post").GetProperty("operationId").GetString());
            var resource = paths.EnumerateObject().Single(path =>
                path.Name.StartsWith("/api/v1/wiki/pages/{", StringComparison.Ordinal)).Value;
            Assert.Equal("GetWikiPage", resource.GetProperty("get").GetProperty("operationId").GetString());
            Assert.Equal("UpdateWikiPageBody", resource.GetProperty("put").GetProperty("operationId").GetString());
            Assert.Equal("UpdateWikiPageMetadata", resource.GetProperty("patch").GetProperty("operationId").GetString());
            Assert.Equal("DeleteWikiPage", resource.GetProperty("delete").GetProperty("operationId").GetString());
            Assert.Contains(collection.GetProperty("post").GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == ApiV1Endpoints.ClientHeader &&
                parameter.GetProperty("required").GetBoolean());
            Assert.Contains(resource.GetProperty("put").GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == ApiV1Endpoints.ClientHeader &&
                parameter.GetProperty("required").GetBoolean());
            Assert.True(resource.GetProperty("get").GetProperty("responses").TryGetProperty("304", out _));
            Assert.True(resource.GetProperty("put").GetProperty("responses").TryGetProperty("428", out _));
            Assert.True(collection.GetProperty("post").GetProperty("responses").GetProperty("201")
                .GetProperty("headers").TryGetProperty("ETag", out _));
            var collectionGet = collection.GetProperty("get");
            Assert.Contains(collectionGet.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "If-None-Match");
            Assert.True(collectionGet.GetProperty("responses").GetProperty("200")
                .GetProperty("headers").TryGetProperty("ETag", out _));
            Assert.True(collectionGet.GetProperty("responses").GetProperty("304")
                .GetProperty("headers").TryGetProperty("ETag", out _));

            var schemas = document.GetProperty("components").GetProperty("schemas");
            var searchSchema = schemas.GetProperty("WikiSearchResultResponse");
            Assert.Contains("snippet", searchSchema.GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("path", schemas.GetProperty("CreateWikiPageRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.DoesNotContain("body", schemas.GetProperty("CreateWikiPageRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("body", schemas.GetProperty("UpdateWikiPageBodyRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("title", schemas.GetProperty("UpdateWikiPageMetadataRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
        }
    }

    private static WikiPage Page(string path, string title, string body) => new()
    {
        Path = path,
        Title = title,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        Body = body,
    };

    private static HttpRequestMessage WikiMutation(HttpMethod method, string path, HttpContent? content,
        string? etag = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "test");
        if (etag != null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }
}
