using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PM.Api;
using PM.Project;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task SettingsReadReturnsOrderedAggregateRevisionAndSupportsConditionalRequest()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            name: "Settings API",
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["m1"] = "First", ["m2"] = "Second" },
            milestonePriorities: new() { ["m2"] = "urgent" }));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/settings");
            var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Settings API", settings!.ProjectName);
            Assert.Equal("teal", settings.Accent);
            Assert.Equal(["todo", "review", "done"], settings.Statuses.Select(item => item.Key));
            Assert.Equal(["PM", "BUILD"], settings.Tracks.Select(item => item.Key));
            Assert.Equal(["m1", "m2"], settings.Milestones.Select(item => item.Key));
            Assert.Equal(["none", "urgent"], settings.Milestones.Select(item => item.Priority));
            Assert.Equal(["none", "low", "medium", "high", "urgent"], settings.PriorityOptions);
            Assert.Equal(ApiPreconditions.FormatETag(settings.Revision), response.Headers.ETag?.Tag);

            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/settings");
            conditional.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            var notModified = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Equal(response.Headers.ETag?.Tag, notModified.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task SettingsMutationsPersistAndReturnRefreshedAggregateRevisions()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var revision = (await client.GetFromJsonAsync<SettingsResponse>("/api/v1/settings"))!.Revision;

            var accentUpdated = await SendSettingsMutation(client, HttpMethod.Put, "/api/v1/settings/accent",
                new { accent = "purple" }, revision);
            Assert.Equal("purple", accentUpdated.Accent);
            revision = accentUpdated.Revision;

            var statusCreated = await SendSettingsMutation(client, HttpMethod.Post, "/api/v1/settings/statuses",
                new { key = "blocked", name = "Blocked" }, revision);
            Assert.Contains(statusCreated.Statuses, item => item.Key == "blocked" && item.Name == "Blocked");
            Assert.NotEqual(revision, statusCreated.Revision);
            revision = statusCreated.Revision;

            var statusRenamed = await SendSettingsMutation(client, HttpMethod.Put,
                "/api/v1/settings/statuses/blocked", new { name = "Waiting" }, revision);
            Assert.Contains(statusRenamed.Statuses, item => item.Key == "blocked" && item.Name == "Waiting");
            revision = statusRenamed.Revision;

            var statusDeleted = await SendSettingsMutation(client, HttpMethod.Delete,
                "/api/v1/settings/statuses/blocked", null, revision);
            Assert.DoesNotContain(statusDeleted.Statuses, item => item.Key == "blocked");
            revision = statusDeleted.Revision;

            var trackCreated = await SendSettingsMutation(client, HttpMethod.Post, "/api/v1/settings/tracks",
                new { key = "BUILD", name = "Build" }, revision);
            revision = trackCreated.Revision;
            var trackRenamed = await SendSettingsMutation(client, HttpMethod.Put,
                "/api/v1/settings/tracks/BUILD", new { name = "Engineering" }, revision);
            Assert.Contains(trackRenamed.Tracks, item => item.Key == "BUILD" && item.Name == "Engineering");
            revision = trackRenamed.Revision;
            var trackDeleted = await SendSettingsMutation(client, HttpMethod.Delete,
                "/api/v1/settings/tracks/BUILD", null, revision);
            Assert.DoesNotContain(trackDeleted.Tracks, item => item.Key == "BUILD");
            revision = trackDeleted.Revision;

            var milestoneCreated = await SendSettingsMutation(client, HttpMethod.Post,
                "/api/v1/settings/milestones", new { key = "m1", title = "Launch", priority = "HIGH" }, revision);
            Assert.Contains(milestoneCreated.Milestones,
                item => item.Key == "m1" && item.Title == "Launch" && item.Priority == "high");
            revision = milestoneCreated.Revision;
            var milestoneRenamed = await SendSettingsMutation(client, HttpMethod.Put,
                "/api/v1/settings/milestones/m1", new { title = "Release" }, revision);
            revision = milestoneRenamed.Revision;
            var priorityUpdated = await SendSettingsMutation(client, HttpMethod.Put,
                "/api/v1/settings/milestones/m1/priority", new { priority = "urgent" }, revision);
            Assert.Contains(priorityUpdated.Milestones,
                item => item.Key == "m1" && item.Title == "Release" && item.Priority == "urgent");
            revision = priorityUpdated.Revision;
            var milestoneDeleted = await SendSettingsMutation(client, HttpMethod.Delete,
                "/api/v1/settings/milestones/m1", null, revision);
            Assert.DoesNotContain(milestoneDeleted.Milestones, item => item.Key == "m1");

            var persisted = ProjectConfig.ReadConfig(root);
            Assert.Equal("purple", persisted.Accent);
            Assert.DoesNotContain("blocked", persisted.TaskStates.Keys);
            Assert.DoesNotContain("BUILD", persisted.Tracks.Keys);
            Assert.DoesNotContain("m1", persisted.Milestones.Keys);
            Assert.Equal(milestoneDeleted.Revision,
                (await client.GetFromJsonAsync<SettingsResponse>("/api/v1/settings"))!.Revision);
        }
    }

    [Fact]
    public async Task SettingsMutationsEnforceHeadersContentTypeJsonAndConfigRevision()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var revision = (await client.GetFromJsonAsync<SettingsResponse>("/api/v1/settings"))!.Revision;

            using var missingClient = new HttpRequestMessage(HttpMethod.Post, "/api/v1/settings/statuses")
            {
                Content = JsonContent.Create(new { key = "blocked", name = "Blocked" }),
            };
            Assert.Equal("missing_client_header", await SendErrorCode(client, missingClient, HttpStatusCode.BadRequest));

            using var unsupported = SettingsMutation(HttpMethod.Post, "/api/v1/settings/statuses",
                new StringContent("{}", Encoding.UTF8, "text/plain"), revision);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType,
                (await client.SendAsync(unsupported)).StatusCode);

            using var malformed = SettingsMutation(HttpMethod.Post, "/api/v1/settings/statuses",
                new StringContent("{", Encoding.UTF8, "application/json"), revision);
            Assert.Equal("invalid_json", await SendErrorCode(client, malformed, HttpStatusCode.BadRequest));

            using var missingRequired = SettingsMutation(HttpMethod.Post, "/api/v1/settings/statuses",
                JsonContent.Create(new { }), revision);
            Assert.Equal("invalid_json",
                await SendErrorCode(client, missingRequired, HttpStatusCode.BadRequest));

            using var missingMatch = SettingsMutation(HttpMethod.Post, "/api/v1/settings/statuses",
                JsonContent.Create(new { key = "blocked", name = "Blocked" }));
            Assert.Equal("precondition_required",
                await SendErrorCode(client, missingMatch, HttpStatusCode.PreconditionRequired));

            using var stale = SettingsMutation(HttpMethod.Post, "/api/v1/settings/statuses",
                JsonContent.Create(new { key = "blocked", name = "Blocked" }), "stale");
            Assert.Equal("precondition_failed",
                await SendErrorCode(client, stale, HttpStatusCode.PreconditionFailed));
            Assert.False(ProjectConfig.ReadConfig(root).TaskStates.ContainsKey("blocked"));
        }
    }

    [Fact]
    public async Task SettingsMutationsPreserveConfigurationFailureCodes()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["m1"] = "First" }));
        var task = TestData.Task("BUILD-0001", "Used", track: "BUILD", milestone: "m1");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        File.WriteAllText(Path.Combine(root.StatesPath, "review", ".keep"), "keep");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var revision = (await client.GetFromJsonAsync<SettingsResponse>("/api/v1/settings"))!.Revision;
            await AssertSettingsFailure(client, HttpMethod.Post, "/api/v1/settings/statuses",
                new { key = "todo", name = "Duplicate" }, revision, "duplicate_status", HttpStatusCode.Conflict);
            await AssertSettingsFailure(client, HttpMethod.Put, "/api/v1/settings/statuses/missing",
                new { name = "Missing" }, revision, "missing_status", HttpStatusCode.NotFound);
            await AssertSettingsFailure(client, HttpMethod.Delete, "/api/v1/settings/statuses/todo",
                null, revision, "status_in_use", HttpStatusCode.Conflict);
            await AssertSettingsFailure(client, HttpMethod.Delete, "/api/v1/settings/statuses/review",
                null, revision, "status_directory_not_empty", HttpStatusCode.Conflict);
            await AssertSettingsFailure(client, HttpMethod.Post, "/api/v1/settings/tracks",
                new { key = "PM", name = "Duplicate" }, revision, "duplicate_track", HttpStatusCode.Conflict);
            await AssertSettingsFailure(client, HttpMethod.Delete, "/api/v1/settings/tracks/BUILD",
                null, revision, "track_in_use", HttpStatusCode.Conflict);
            await AssertSettingsFailure(client, HttpMethod.Post, "/api/v1/settings/milestones",
                new { key = "m1", title = "Duplicate" }, revision, "duplicate_milestone", HttpStatusCode.Conflict);
            await AssertSettingsFailure(client, HttpMethod.Delete, "/api/v1/settings/milestones/m1",
                null, revision, "milestone_in_use", HttpStatusCode.Conflict);
            await AssertSettingsFailure(client, HttpMethod.Put, "/api/v1/settings/milestones/m1/priority",
                new { priority = "later" }, revision, "invalid_priority", HttpStatusCode.BadRequest);
            await AssertSettingsFailure(client, HttpMethod.Put, "/api/v1/settings/accent",
                new { accent = "infrared" }, revision, "invalid_accent", HttpStatusCode.BadRequest);
            await AssertSettingsFailure(client, HttpMethod.Delete, "/api/v1/settings/milestones/missing",
                null, revision, "missing_milestone", HttpStatusCode.NotFound);
        }

        using var singleWorkspace = new TempWorkingDirectory();
        var singleRoot = await singleWorkspace.CreateProject();
        singleRoot.Config!.TaskStates = new() { ["todo"] = "Todo" };
        singleRoot.Config.WriteConfig(singleRoot);
        var (singleApp, singleClient) = await CreateApiClient(singleRoot);
        await using (singleApp)
        using (singleClient)
        {
            var revision = (await singleClient.GetFromJsonAsync<SettingsResponse>("/api/v1/settings"))!.Revision;
            await AssertSettingsFailure(singleClient, HttpMethod.Delete, "/api/v1/settings/statuses/todo",
                null, revision, "last_status", HttpStatusCode.Conflict);
            await AssertSettingsFailure(singleClient, HttpMethod.Delete, "/api/v1/settings/tracks/PM",
                null, revision, "last_track", HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task ValidationReturnsFreshValidAndStructuredInvalidResults()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var valid = await client.GetFromJsonAsync<ValidationResponse>("/api/v1/validation");
            Assert.True(valid!.Valid);
            Assert.Empty(valid.Issues);

            var task = TestData.Task("PM-0001", "Invalid context", track: "missing");
            root.WriteTask(task);
            Directory.CreateDirectory(Path.Combine(root.StatesPath, "unknown"));
            File.WriteAllText(Path.Combine(root.StatesPath, "unknown", "PM-9999.ref"),
                "../../tasks/PM-9999.md");
            File.WriteAllText(Path.Combine(root.WikiPath, "bad.md"), "not markdown");

            var invalid = await client.GetFromJsonAsync<ValidationResponse>("/api/v1/validation");
            Assert.False(invalid!.Valid);
            var taskIssue = Assert.Single(invalid.Issues, issue => issue.Code == "unknown_task_track");
            Assert.Equal("error", taskIssue.Severity);
            Assert.Equal("PM-0001", taskIssue.TaskId);
            Assert.Equal(root.GetTaskFilePath("PM-0001"), taskIssue.Path);
            var stateIssue = Assert.Single(invalid.Issues, issue => issue.Code == "broken_ref_target");
            Assert.Equal("unknown", stateIssue.State);
            var wikiIssue = Assert.Single(invalid.Issues, issue => issue.Code == "invalid_wiki_markdown");
            Assert.Equal("bad", wikiIssue.WikiPath);
        }
    }

    [Fact]
    public async Task OpenApiDescribesSettingsValidationBodiesPreconditionsAndETags()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
            var paths = document.GetProperty("paths");
            var expected = new[]
            {
                "/api/v1/settings", "/api/v1/validation", "/api/v1/settings/statuses",
                "/api/v1/settings/statuses/{key}", "/api/v1/settings/tracks",
                "/api/v1/settings/tracks/{key}", "/api/v1/settings/milestones",
                "/api/v1/settings/milestones/{key}", "/api/v1/settings/milestones/{key}/priority",
            };
            Assert.All(expected, path => Assert.True(paths.TryGetProperty(path, out _), path));

            var read = paths.GetProperty("/api/v1/settings").GetProperty("get");
            Assert.Contains(read.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "If-None-Match");
            Assert.True(read.GetProperty("responses").GetProperty("200").GetProperty("headers")
                .TryGetProperty("ETag", out _));

            var create = paths.GetProperty("/api/v1/settings/statuses").GetProperty("post");
            Assert.True(create.TryGetProperty("requestBody", out _));
            Assert.Contains(create.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "If-Match" &&
                parameter.GetProperty("required").GetBoolean());
            Assert.Contains(create.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == ApiV1Endpoints.ClientHeader &&
                parameter.GetProperty("required").GetBoolean());
            Assert.True(create.GetProperty("responses").GetProperty("200").GetProperty("headers")
                .TryGetProperty("ETag", out _));
            Assert.True(create.GetProperty("responses").TryGetProperty("415", out _));
            Assert.True(create.GetProperty("responses").TryGetProperty("428", out _));
        }
    }

    private static async Task<SettingsResponse> SendSettingsMutation(HttpClient client, HttpMethod method,
        string path, object? body, string revision)
    {
        using var request = SettingsMutation(method, path, body == null ? null : JsonContent.Create(body), revision);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(settings);
        Assert.Equal(ApiPreconditions.FormatETag(settings.Revision), response.Headers.ETag?.Tag);
        return settings;
    }

    private static async Task AssertSettingsFailure(HttpClient client, HttpMethod method, string path,
        object? body, string revision, string errorCode, HttpStatusCode status)
    {
        using var request = SettingsMutation(method, path, body == null ? null : JsonContent.Create(body), revision);
        Assert.Equal(errorCode, await SendErrorCode(client, request, status));
    }

    private static HttpRequestMessage SettingsMutation(HttpMethod method, string path, HttpContent? content,
        string? revision = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation(ApiV1Endpoints.ClientHeader, "settings-tests");
        if (revision != null)
            request.Headers.TryAddWithoutValidation("If-Match", ApiPreconditions.FormatETag(revision));
        return request;
    }

    private static async Task<string> SendErrorCode(HttpClient client, HttpRequestMessage request,
        HttpStatusCode status)
    {
        var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode;
    }
}
