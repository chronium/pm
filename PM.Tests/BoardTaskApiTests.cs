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
    public async Task TaskApiPreservesCanonicalCrossProjectDependencies()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        const string qualifiedReference = "pm://project/prj_other/task/OTHER-0001";
        var task = TestData.Task("PM-0001", "Cross-project task", dependsOn: [qualifiedReference]);
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetFromJsonAsync<TaskResponse>("/api/v1/tasks/PM-0001");

            Assert.NotNull(response);
            Assert.Equal([qualifiedReference], response.Dependencies.DependsOn);
            Assert.Equal([qualifiedReference], response.Dependencies.Missing);
            Assert.False(response.Dependencies.Ready);
        }
    }

    [Fact]
    public async Task BoardReturnsNormalizedFiltersOptionsOrderedGroupsAndSummaries()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            name: "API Board",
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["m1"] = "First" },
            milestonePriorities: new() { ["m1"] = "high" });
        config.Milestones["m1"].Description = "Deliver the **first milestone**.";
        var root = await workspace.CreateProject(config);
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
            Assert.Equal("Deliver the **first milestone**.", milestoneGroup.GetProperty("description").GetString());
            Assert.Equal("active", milestoneGroup.GetProperty("lifecycle").GetString());
            Assert.Empty(milestoneGroup.GetProperty("requiredActivationTriggers").EnumerateArray());
            Assert.Empty(milestoneGroup.GetProperty("unmetActivationTriggers").EnumerateArray());
            var stateGroup = Assert.Single(milestoneGroup.GetProperty("states").EnumerateArray());
            Assert.Equal("review", stateGroup.GetProperty("key").GetString());
            var summary = Assert.Single(stateGroup.GetProperty("tasks").EnumerateArray());
            Assert.Equal("PM-0001", summary.GetProperty("id").GetString());
            Assert.Equal("high", summary.GetProperty("priority").GetString());
            Assert.Equal("milestone", summary.GetProperty("prioritySource").GetString());
            Assert.True(summary.GetProperty("dependencies").GetProperty("ready").GetBoolean());
            Assert.True(summary.GetProperty("activation").GetProperty("isEligible").GetBoolean());
            Assert.Equal("active",
                summary.GetProperty("activation").GetProperty("milestoneLifecycle").GetString());
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

            var detail = await client.GetFromJsonAsync<TaskResponse>("/api/v1/tasks/PM-0001");
            Assert.NotNull(detail);
            Assert.True(detail.Activation.IsEligible);
            Assert.Equal("active", detail.Activation.MilestoneLifecycle);
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
            Assert.Equal(2, navigation.ActivationEligibleCount);
            Assert.Equal(0, navigation.Tracks.Single(option => option.Key == "EMPTY").RemainingCount);
            Assert.Equal(0,
                navigation.Tracks.Single(option => option.Key == "EMPTY").ActivationEligibleCount);
            Assert.Equal(1, navigation.Milestones.Single(option => option.Key == "m1").RemainingCount);
            var milestone = navigation.Milestones.Single(option => option.Key == "m1");
            Assert.Equal(1, milestone.ActivationEligibleCount);
            Assert.Equal("active", milestone.Lifecycle);
            Assert.Equal(0, navigation.Milestones.Single(option => option.Key == "empty").RemainingCount);
            Assert.Equal(ApiPreconditions.FormatETag(navigation.Revision), response.Headers.ETag?.Tag);

            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/board/navigation");
            conditional.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);
        }
    }

    [Fact]
    public async Task BoardNavigationAndSearchExposeDeliveredWorkOnlyWhenRequested()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            tracks: new() { ["PM"] = "Product", ["OPS"] = "Operations" },
            milestones: new() { ["active"] = "Active", ["delivered"] = "Delivered" });
        config.Milestones["delivered"].Delivery = new MilestoneDelivery
        {
            At = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero),
            Mode = MilestoneDeliveryMode.Exceptional,
            Reason = "Accepted with open work.",
            AcceptedTaskIds = ["OPS-0001"],
        };
        var root = await workspace.CreateProject(config);
        var active = TestData.Task("PM-0001", "Needle active", milestone: "active");
        var delivered = TestData.Task("OPS-0001", "Needle delivered", track: "OPS", milestone: "delivered");
        root.WriteTask(active);
        root.WriteTask(delivered);
        root.UpdateTaskState(active, "todo");
        root.UpdateTaskState(delivered, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var defaultBoardResponse = await client.GetAsync("/api/v1/board");
            var defaultBoard = (await defaultBoardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
            var includedBoardResponse = await client.GetAsync("/api/v1/board?includeDelivered=true");
            var includedBoard = (await includedBoardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
            var defaultNavigation = (await client.GetFromJsonAsync<BoardNavigationResponse>(
                "/api/v1/board/navigation"))!;
            var includedNavigation = (await client.GetFromJsonAsync<BoardNavigationResponse>(
                "/api/v1/board/navigation?includeDelivered=true"))!;
            var defaultSearch = (await client.GetFromJsonAsync<List<TaskSearchResultResponse>>(
                "/api/v1/tasks/search?query=in%3Aall"))!;
            var includedSearch = (await client.GetFromJsonAsync<List<TaskSearchResultResponse>>(
                "/api/v1/tasks/search?query=in%3Aall&includeDelivered=true"))!;
            var direct = await client.GetFromJsonAsync<TaskResponse>("/api/v1/tasks/OPS-0001");

            Assert.False(defaultBoard.Filters.IncludeDelivered);
            Assert.True(includedBoard.Filters.IncludeDelivered);
            Assert.Equal(["PM-0001"], Tasks(defaultBoard).Select(task => task.Id));
            Assert.Equal(["PM-0001", "OPS-0001"], Tasks(includedBoard).Select(task => task.Id));
            Assert.Equal(["active"], defaultBoard.Milestones.Select(milestone => milestone.Key));
            Assert.Equal(["active", "delivered"], includedBoard.Milestones.Select(milestone => milestone.Key));
            Assert.NotEqual(defaultBoardResponse.Headers.ETag, includedBoardResponse.Headers.ETag);
            Assert.Equal(1, defaultNavigation.RemainingCount);
            Assert.Equal(0, defaultNavigation.Tracks.Single(track => track.Key == "OPS").RemainingCount);
            Assert.Equal(2, includedNavigation.RemainingCount);
            Assert.Equal(["PM-0001"], defaultSearch.Select(task => task.Id));
            Assert.Equal(["OPS-0001", "PM-0001"], includedSearch.Select(task => task.Id));
            Assert.Equal("OPS-0001", direct!.Id);

            root.Config!.Milestones["delivered"].Delivery = null;
            root.Config.WriteConfig(root);

            var reopened = (await client.GetFromJsonAsync<BoardResponse>("/api/v1/board"))!;
            Assert.Contains(Tasks(reopened), task => task.Id == "OPS-0001");
            Assert.Contains(reopened.Milestones, milestone => milestone.Key == "delivered");
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
    public async Task TaskNotesRequireCurrentRevisionAndReturnRefreshedTask()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Task", "Body");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var read = await client.GetAsync("/api/v1/tasks/PM-0001");
            using var missingPrecondition = Mutation(HttpMethod.Post, "/api/v1/tasks/PM-0001/notes",
                JsonContent.Create(new { note = "Rejected" }));
            Assert.Equal(HttpStatusCode.PreconditionRequired,
                (await client.SendAsync(missingPrecondition)).StatusCode);

            using var empty = Mutation(HttpMethod.Post, "/api/v1/tasks/PM-0001/notes",
                JsonContent.Create(new { note = " " }), read.Headers.ETag?.Tag);
            var emptyResponse = await client.SendAsync(empty);
            Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
            Assert.Equal("invalid_note", (await emptyResponse.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);

            using var append = Mutation(HttpMethod.Post, "/api/v1/tasks/PM-0001/notes",
                JsonContent.Create(new { note = "API note\ncontinued" }), read.Headers.ETag?.Tag);
            var response = await client.SendAsync(append);
            var updated = await response.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("## Notes", updated!.Description);
            Assert.Contains("API note\n  continued", updated.Description);
            Assert.NotEqual(read.Headers.ETag?.Tag, response.Headers.ETag?.Tag);

            using var stale = Mutation(HttpMethod.Post, "/api/v1/tasks/PM-0001/notes",
                JsonContent.Create(new { note = "Stale" }), read.Headers.ETag?.Tag);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);
        }
    }

    [Fact]
    public async Task NextTaskApiDefaultsToReadyAndSupportsTrackMilestoneScope()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            tracks: new() { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new() { ["m1"] = "First" }));
        var ready = TestData.Task("PM-0001", "Ready", track: "PM");
        var blocked = TestData.Task("BUILD-0001", "Blocked", track: "BUILD", milestone: "m1",
            dependsOn: ["BUILD-9999"]);
        root.WriteTask(ready);
        root.WriteTask(blocked);
        root.UpdateTaskState(ready, "todo");
        root.UpdateTaskState(blocked, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var readyResponse = await client.GetFromJsonAsync<NextTaskResponse>("/api/v1/tasks/next");
            Assert.True(readyResponse!.Found);
            Assert.Equal("PM-0001", readyResponse.Task!.Id);

            var scoped = await client.GetFromJsonAsync<NextTaskResponse>(
                "/api/v1/tasks/next?track=BUILD&milestone=m1");
            Assert.False(scoped!.Found);
            Assert.Null(scoped.Task);
            Assert.Contains("track BUILD and milestone m1", scoped.Reason);

            var includeBlocked = await client.GetFromJsonAsync<NextTaskResponse>(
                "/api/v1/tasks/next?track=BUILD&milestone=m1&readyOnly=false");
            Assert.Equal("BUILD-0001", includeBlocked!.Task!.Id);

            var invalid = await client.GetAsync("/api/v1/tasks/next?milestone=missing");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_milestone",
                (await invalid.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Fact]
    public async Task TaskUpdateSupportsOptionalAssignedAndUnassignedPlacement()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["m1"] = "First" }));
        var task = TestData.Task("PM-0001", "Task", track: "PM", milestone: "m1");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var read = await client.GetAsync("/api/v1/tasks/PM-0001");
            using var compatible = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new { title = "Compatible", state = "todo", description = "Body", priority = "inherit" }),
                read.Headers.ETag?.Tag);
            var compatibleResponse = await client.SendAsync(compatible);
            var preserved = await compatibleResponse.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal("PM", preserved!.Track);
            Assert.Equal("m1", preserved.Milestone);

            using var assigned = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new
                {
                    title = "Moved", state = "review", description = "Body", priority = "high",
                    placement = new { track = "BUILD", milestone = "m1" }
                }), compatibleResponse.Headers.ETag?.Tag);
            var assignedResponse = await client.SendAsync(assigned);
            var moved = await assignedResponse.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
            Assert.Equal("BUILD", moved!.Track);
            Assert.Equal("m1", moved.Milestone);
            Assert.Equal("review", moved.State);

            using var unassign = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                JsonContent.Create(new
                {
                    title = "Moved", state = "review", description = "Body", priority = "high",
                    placement = new { track = "BUILD", milestone = (string?)null }
                }), assignedResponse.Headers.ETag?.Tag);
            var unassignedResponse = await client.SendAsync(unassign);
            var unassigned = await unassignedResponse.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.Equal(HttpStatusCode.OK, unassignedResponse.StatusCode);
            Assert.Null(unassigned!.Milestone);
            Assert.NotEqual(moved.Revision, unassigned.Revision);
        }
    }

    [Theory]
    [InlineData("{\"track\":\"missing\",\"milestone\":null}", "invalid_track")]
    [InlineData("{\"track\":\"PM\",\"milestone\":\"missing\"}", "invalid_milestone")]
    [InlineData("{\"track\":\" \",\"milestone\":null}", "invalid_track")]
    public async Task TaskUpdateRejectsInvalidPlacement(string placementJson, string errorCode)
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Task");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var read = await client.GetAsync("/api/v1/tasks/PM-0001");
            var json = $$"""{"title":"Task","state":"todo","description":"","priority":"inherit","placement":{{placementJson}}}""";
            using var update = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                new StringContent(json, Encoding.UTF8, "application/json"), read.Headers.ETag?.Tag);
            var response = await client.SendAsync(update);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(errorCode, (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Theory]
    [InlineData("{\"track\":\"PM\"}")]
    [InlineData("{\"milestone\":null}")]
    public async Task TaskUpdateRejectsIncompletePlacement(string placementJson)
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Task");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var read = await client.GetAsync("/api/v1/tasks/PM-0001");
            var json = $$"""{"title":"Task","state":"todo","description":"","priority":"inherit","placement":{{placementJson}}}""";
            using var update = Mutation(HttpMethod.Put, "/api/v1/tasks/PM-0001",
                new StringContent(json, Encoding.UTF8, "application/json"), read.Headers.ETag?.Tag);
            var response = await client.SendAsync(update);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_json", (await response.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
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
            foreach (var path in new[]
                     {
                         "/api/v1/board",
                         "/api/v1/board/navigation",
                         "/api/v1/tasks/search",
                         "/api/v1/projects/{projectId}/board",
                         "/api/v1/projects/{projectId}/board/navigation",
                         "/api/v1/projects/{projectId}/tasks/search",
                     })
                Assert.Contains(paths.GetProperty(path).GetProperty("get").GetProperty("parameters").EnumerateArray(),
                    parameter => parameter.GetProperty("name").GetString() == "includeDelivered" &&
                                 (!parameter.TryGetProperty("required", out var required) ||
                                  !required.GetBoolean()));
            Assert.True(paths.TryGetProperty("/api/v1/tasks/{id}/state", out _));
            Assert.True(paths.TryGetProperty("/api/v1/tasks/{id}/notes", out var notes));
            Assert.True(notes.GetProperty("post").GetProperty("responses").GetProperty("200")
                .GetProperty("headers").TryGetProperty("ETag", out _));
            Assert.True(paths.TryGetProperty("/api/v1/tasks/next", out var next));
            Assert.Contains(next.GetProperty("get").GetProperty("parameters").EnumerateArray(),
                parameter => parameter.GetProperty("name").GetString() == "readyOnly");
            Assert.True(task.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out var deleted));
            Assert.False(deleted.TryGetProperty("headers", out _));
            var created = paths.GetProperty("/api/v1/tasks").GetProperty("post")
                .GetProperty("responses").GetProperty("201");
            Assert.True(created.GetProperty("headers").TryGetProperty("ETag", out _));
            var schemas = document.GetProperty("components").GetProperty("schemas");
            Assert.Contains("remainingCount", schemas.GetProperty("BoardNavigationResponse")
                .GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("activationEligibleCount", schemas.GetProperty("BoardNavigationResponse")
                .GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("activation", schemas.GetProperty("BoardTaskSummaryResponse")
                .GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("activation", schemas.GetProperty("TaskResponse")
                .GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("includeDelivered", schemas.GetProperty("BoardFilterResponse")
                .GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            var milestoneGroup = schemas.GetProperty("BoardMilestoneGroupResponse")
                .GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains("description", milestoneGroup);
            Assert.Contains("lifecycle", milestoneGroup);
            Assert.Contains("title", schemas.GetProperty("CreateTaskRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("priority", schemas.GetProperty("UpdateTaskRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("track", schemas.GetProperty("TaskPlacementRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("milestone", schemas.GetProperty("TaskPlacementRequest").GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("note", schemas.GetProperty("AppendTaskNoteRequest").GetProperty("required")
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

            var projectWide = await client.GetFromJsonAsync<List<TaskSearchResultResponse>>(
                "/api/v1/tasks/search?query=needle%20in%3Aall&track=BUILD");
            Assert.Equal(["BUILD-0001", "PM-0002"], projectWide!.Select(item => item.Id));

            var invalid = await client.GetAsync("/api/v1/tasks/search?query=state%3A");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_task_query",
                (await invalid.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    private static IEnumerable<BoardTaskSummaryResponse> Tasks(BoardResponse board) =>
        board.MilestoneGroups.SelectMany(group => group.States).SelectMany(state => state.Tasks);

    private static HttpRequestMessage Mutation(HttpMethod method, string path, HttpContent? content,
        string? etag = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "test");
        if (etag != null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }
}
