using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PM.Api;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task BoardReturnsNormalizedFiltersOptionsOrderedGroupsAndSummaries()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            name: "API Board",
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["m1"] = "First" },
            milestonePriorities: new() { ["m1"] = "high" }));
        var dependency = TestData.Task("BUILD-0001", "Dependency", track: "BUILD");
        var task = TestData.Task("PM-0001", "Main", "# Heading\n\nDetails", milestone: "m1",
            dependsOn: [dependency.Id]);
        root.WriteTask(dependency);
        root.WriteTask(task);
        root.UpdateTaskState(dependency, "done");
        root.UpdateTaskState(task, "review");

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/board?track=%20PM%20&milestone=m1&state=review");
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("API Board", body.GetProperty("projectName").GetString());
            Assert.Equal("PM", body.GetProperty("filters").GetProperty("track").GetString());
            Assert.Equal(2, body.GetProperty("tracks").GetArrayLength());
            var milestoneGroup = Assert.Single(body.GetProperty("milestoneGroups").EnumerateArray());
            Assert.Equal("m1", milestoneGroup.GetProperty("key").GetString());
            var stateGroup = Assert.Single(milestoneGroup.GetProperty("states").EnumerateArray());
            Assert.Equal("review", stateGroup.GetProperty("key").GetString());
            var summary = Assert.Single(stateGroup.GetProperty("tasks").EnumerateArray());
            Assert.Equal("PM-0001", summary.GetProperty("id").GetString());
            Assert.Equal("high", summary.GetProperty("priority").GetString());
            Assert.Equal("milestone", summary.GetProperty("prioritySource").GetString());
            Assert.True(summary.GetProperty("dependencies").GetProperty("ready").GetBoolean());
            Assert.Equal("Heading", summary.GetProperty("descriptionPreview").GetString());
            Assert.False(summary.TryGetProperty("localMetadata", out _));
            Assert.False(summary.TryGetProperty("filePath", out _));
            Assert.False(summary.TryGetProperty("description", out _));
            Assert.Equal(ApiPreconditions.FormatETag(body.GetProperty("revision").GetString()!),
                response.Headers.ETag?.Tag);

            using var conditional = new HttpRequestMessage(HttpMethod.Get,
                "/api/v1/board?track=PM&milestone=m1&state=review");
            conditional.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);
        }
    }

    [Fact]
    public async Task BoardRejectsInvalidFiltersAndKeepsEmptyStateGroups()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var valid = await client.GetFromJsonAsync<BoardResponse>("/api/v1/board");
            Assert.NotNull(valid);
            var unassigned = Assert.Single(valid.MilestoneGroups);
            Assert.Equal(3, unassigned.States.Count);
            Assert.All(unassigned.States, state => Assert.Empty(state.Tasks));

            var invalid = await client.GetAsync("/api/v1/board?state=missing");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_state", (await invalid.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Fact]
    public async Task BoardNavigationReturnsCountsAndSupportsConditionalReads()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            tracks: new() { ["PM"] = "Product", ["EMPTY"] = "Empty" },
            milestones: new() { ["m1"] = "First", ["empty"] = "Empty" }));
        var assigned = TestData.Task("PM-0001", "Assigned", milestone: "m1");
        var unassigned = TestData.Task("PM-0002", "Unassigned");
        root.WriteTask(assigned);
        root.WriteTask(unassigned);
        root.UpdateTaskState(assigned, "todo");
        root.UpdateTaskState(unassigned, "review");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/board/navigation");
            var navigation = await response.Content.ReadFromJsonAsync<BoardNavigationResponse>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, navigation!.RemainingCount);
            Assert.Equal(0, navigation.Tracks.Single(option => option.Key == "EMPTY").RemainingCount);
            Assert.Equal(1, navigation.Milestones.Single(option => option.Key == "m1").RemainingCount);
            Assert.Equal(0, navigation.Milestones.Single(option => option.Key == "empty").RemainingCount);
            Assert.Equal(ApiPreconditions.FormatETag(navigation.Revision), response.Headers.ETag?.Tag);

            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/board/navigation");
            conditional.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);
        }
    }

    [Fact]
    public async Task TaskDetailReturnsResolvedAndEditableMetadataAndSupportsConditionalRead()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new() { ["m1"] = "First" },
            milestonePriorities: new() { ["m1"] = "urgent" }));
        var task = TestData.Task("PM-0001", "Task", "Body only", milestone: "m1");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/tasks/PM-0001");
            var body = await response.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("urgent", body!.Priority);
            Assert.Equal("milestone", body.PrioritySource);
            Assert.Equal("inherit", body.PrioritySelection);
            Assert.Equal("Body only", body.Description);
            Assert.Equal(root.GetTaskFilePath(task.Id), body.LocalMetadata.FilePath);
            Assert.Equal(ApiPreconditions.FormatETag(body.Revision), response.Headers.ETag?.Tag);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tasks/PM-0001");
            request.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(request)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync("/api/v1/tasks/PM-9999")).StatusCode);
        }
    }

    [Fact]
    public async Task TaskCreateUpdateMoveAndDeleteRoundTripWithRevisions()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var create = Mutation(HttpMethod.Post, "/api/v1/tasks",
                JsonContent.Create(new { title = "Created", track = "PM", milestone = (string?)null,
                    description = "Initial" }));
            var createdResponse = await client.SendAsync(create);
            var created = await createdResponse.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            Assert.Equal("/api/v1/tasks/PM-0001", createdResponse.Headers.Location?.OriginalString);
            Assert.Equal("todo", created!.State);
            Assert.Equal("Initial", created.Description);
            Assert.Equal(ApiPreconditions.FormatETag(created.Revision), createdResponse.Headers.ETag?.Tag);

            using var missingPrecondition = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new { title = "Rejected", state = "review", description = "Rejected", priority = "high" }));
            Assert.Equal(HttpStatusCode.PreconditionRequired,
                (await client.SendAsync(missingPrecondition)).StatusCode);

            using var staleUpdate = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new { title = "Rejected", state = "review", description = "Rejected", priority = "high" }),
                "\"stale\"");
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(staleUpdate)).StatusCode);

            using var update = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new { title = "Updated", state = "review", description = "Changed", priority = "high" }),
                createdResponse.Headers.ETag?.Tag);
            var updatedResponse = await client.SendAsync(update);
            var updated = await updatedResponse.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
            Assert.Equal("Updated", updated!.Title);
            Assert.Equal("review", updated.State);
            Assert.Equal("high", updated.PrioritySelection);
            Assert.NotEqual(created.Revision, updated.Revision);

            using var move = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001/state",
                JsonContent.Create(new { state = "done" }), updatedResponse.Headers.ETag?.Tag);
            var movedResponse = await client.SendAsync(move);
            var moved = await movedResponse.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal("done", moved!.State);

            using var inherit = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new { title = "Updated", state = "done", description = "Changed", priority = "inherit" }),
                movedResponse.Headers.ETag?.Tag);
            var inheritedResponse = await client.SendAsync(inherit);
            var inherited = await inheritedResponse.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal("inherit", inherited!.PrioritySelection);

            using var delete = Mutation(HttpMethod.Delete, "/api/v1/tasks/PM-0001", null,
                inheritedResponse.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync("/api/v1/tasks/PM-0001")).StatusCode);
        }
    }

    [Fact]
    public async Task TaskCreationMapsDomainValidationAndNextIdFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root, nextIdService: new ApiNextIdService(false));
        await using (app)
        using (client)
        {
            using var missingTitle = Mutation(HttpMethod.Post, "/api/v1/tasks",
                JsonContent.Create(new { track = "PM" }));
            var missingTitleResponse = await client.SendAsync(missingTitle);
            Assert.Equal(HttpStatusCode.BadRequest, missingTitleResponse.StatusCode);
            Assert.Equal("invalid_title",
                (await missingTitleResponse.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);

            using var invalidTrack = Mutation(HttpMethod.Post, "/api/v1/tasks",
                JsonContent.Create(new { title = "Invalid", track = "missing" }));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidTrack)).StatusCode);

            using var unavailable = Mutation(HttpMethod.Post, "/api/v1/tasks",
                JsonContent.Create(new { title = "Unavailable", track = "PM" }));
            var unavailableResponse = await client.SendAsync(unavailable);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableResponse.StatusCode);
            Assert.Equal("next_id_unavailable",
                (await unavailableResponse.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    public async Task TaskMutationsMapEmptyOrMalformedJsonToStableProblem(string json)
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var request = Mutation(HttpMethod.Post, "/api/v1/tasks",
                new StringContent(json, Encoding.UTF8, "application/json"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_json", (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Fact]
    public async Task DeleteAcceptsNoBodyAndRejectsNonJsonSuppliedBody()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Delete");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var read = await client.GetAsync("/api/v1/tasks/PM-0001");
            using var invalid = Mutation(HttpMethod.Delete, "/api/v1/tasks/PM-0001",
                new StringContent("body", Encoding.UTF8, "text/plain"), read.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.SendAsync(invalid)).StatusCode);
            Assert.True(File.Exists(root.GetTaskFilePath(task.Id)));

            using var compatible = Mutation(HttpMethod.Delete, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new { }), read.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(compatible)).StatusCode);
            Assert.False(File.Exists(root.GetTaskFilePath(task.Id)));
        }
    }

    [Fact]
    public async Task OpenApiDescribesBoardAndTaskContractsAndMutationEtags()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
            var paths = document.GetProperty("paths");
            Assert.True(paths.TryGetProperty("/api/v1/board", out _));
            Assert.True(paths.TryGetProperty("/api/v1/board/navigation", out _));
            Assert.True(paths.TryGetProperty("/api/v1/tasks/{id}", out var task));
            Assert.True(paths.TryGetProperty("/api/v1/tasks/search", out var search));
            Assert.Contains(search.GetProperty("get").GetProperty("parameters").EnumerateArray(),
                parameter => parameter.GetProperty("name").GetString() == "query" &&
                             parameter.GetProperty("required").GetBoolean());
            Assert.True(paths.TryGetProperty("/api/v1/tasks/{id}/state", out _));
            Assert.True(task.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out var deleted));
            Assert.False(deleted.TryGetProperty("headers", out _));
            var created = paths.GetProperty("/api/v1/tasks").GetProperty("post")
                .GetProperty("responses").GetProperty("201");
            Assert.True(created.GetProperty("headers").TryGetProperty("ETag", out _));
            var schemas = document.GetProperty("components").GetProperty("schemas");
            Assert.Contains("remainingCount", schemas.GetProperty("BoardNavigationResponse")
                .GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("title", schemas.GetProperty("CreateTaskRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("priority", schemas.GetProperty("UpdateTaskRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
        }
    }

    [Fact]
    public async Task TaskSearchApiUsesStructuredQueryAndIntersectsBoardContext()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["M1"] = "First" }));
        var visible = TestData.Task("BUILD-0001", "Needle <safe>", "Description & context", "BUILD", "M1");
        var filtered = TestData.Task("PM-0002", "Needle other", "Other", "PM", "M1");
        root.WriteTask(visible);
        root.WriteTask(filtered);
        root.UpdateTaskState(visible, "todo");
        root.UpdateTaskState(filtered, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync(
                "/api/v1/tasks/search?query=needle%20milestone%3AM1&track=BUILD&state=todo&limit=1");
            var results = await response.Content.ReadFromJsonAsync<List<TaskSearchResultResponse>>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = Assert.Single(results!);
            Assert.Equal("BUILD-0001", result.Id);
            Assert.Equal("Needle <safe>", result.Title);
            Assert.Contains("Needle", result.Snippet);
            Assert.True(result.MatchCount > 0);

            var numericId = await client.GetFromJsonAsync<List<TaskSearchResultResponse>>(
                "/api/v1/tasks/search?query=id%3A%202");
            Assert.Equal("PM-0002", Assert.Single(numericId!).Id);

            var invalid = await client.GetAsync("/api/v1/tasks/search?query=state%3A");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_task_query",
                (await invalid.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    private static HttpRequestMessage Mutation(HttpMethod method, string path, HttpContent? content,
        string? etag = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "test");
        if (etag != null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }
}
