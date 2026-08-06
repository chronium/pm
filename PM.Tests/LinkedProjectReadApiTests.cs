using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PM.Api;
using PM.Application;
using PM.Project;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task LinkedWikiSearchReturnsOwnedFamilyResultsAndWarnings()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await workspace.CreateProject(TestData.Config(name: "Games"));
        await WriteProjectId(active, "prj_games");
        var child = await CreateLinkedProject(
            Path.Combine(workspace.Path, "royale"), "prj_royale", "Royale");
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_royale", "royale", "royale"),
                Declaration("prj_missing", "missing", "missing"),
            ],
        });
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });
        Assert.True(new WikiService(active).CreatePage("shared/current", "Current guide", "needle").Success);
        Assert.True(new WikiService(child).CreatePage("shared/royale", "Royale guide", "needle").Success);

        var family = LinkedFamily(active, workspace);
        var (app, client) = await CreateApiClient(active, linkedProjectFamilyService: family);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync(
                "/api/v1/project/links/wiki/search?query=needle&limit=10");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<LinkedWikiSearchResponse>())!;

            Assert.Equal(["prj_games", "prj_royale"], result.Pages.Select(page => page.ProjectId));
            Assert.Equal("royale", result.Pages[1].Alias);
            Assert.Equal("child", result.Pages[1].Relationship);
            Assert.Contains(result.Warnings, warning => warning.TargetProjectId == "prj_missing");

            var invalid = await client.GetAsync(
                "/api/v1/project/links/wiki/search?query=%20");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

            var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
            var operation = document.GetProperty("paths")
                .GetProperty("/api/v1/project/links/wiki/search")
                .GetProperty("get");
            Assert.Equal("SearchLinkedProjectFamilyWikiPages", operation.GetProperty("operationId").GetString());
            Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "query" &&
                parameter.GetProperty("required").GetBoolean());
        }
    }

    [Fact]
    public async Task LinkedProjectReadApiServesProjectBoardTasksWikiAndSettingsWithoutWrites()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await workspace.CreateProject(TestData.Config(name: "Games"));
        await WriteProjectId(active, "prj_games");
        var child = await CreateLinkedProject(
            Path.Combine(workspace.Path, "royale"), "prj_royale", "Royale");
        var sibling = await CreateLinkedProject(
            Path.Combine(workspace.Path, "starfall"), "prj_starfall", "Starfall");
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_royale", "royale", "royale"),
                Declaration("prj_starfall", "starfall", "starfall"),
            ],
        });
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });
        sibling.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });
        var dependency = TestData.Task("STAR-0001", "Sibling dependency", track: "GAME");
        sibling.WriteTask(dependency);
        sibling.UpdateTaskState(dependency, "done");
        var task = TestData.Task(
            "GAME-0001",
            "Linked task",
            "Linked task body",
            track: "GAME",
            dependsOn: ["pm://project/prj_starfall/task/STAR-0001"]);
        child.WriteTask(task);
        child.UpdateTaskState(task, "todo");
        Assert.True(new WikiService(child).CreatePage("guide/start", "Linked guide", "Linked wiki body").Success);

        var family = LinkedFamily(active, workspace);
        var (app, client) = await CreateApiClient(active, linkedProjectFamilyService: family);
        await using (app)
        using (client)
        {
            var project = await client.GetFromJsonAsync<LinkedProjectContextResponse>(
                "/api/v1/projects/prj_royale/project");
            Assert.Equal("prj_royale", project!.ProjectId);
            Assert.Equal("Royale", project.Name);
            Assert.Equal("child", project.Relationship);
            Assert.True(project.ReadOnly);

            var board = await client.GetFromJsonAsync<BoardResponse>(
                "/api/v1/projects/prj_royale/board");
            Assert.Equal("Royale", board!.ProjectName);
            var boardTask = Assert.Single(board.MilestoneGroups.SelectMany(group => group.States)
                .SelectMany(state => state.Tasks), candidate => candidate.Id == task.Id);
            Assert.True(boardTask.Dependencies.Ready);
            Assert.Empty(boardTask.Dependencies.Missing);

            var detail = await client.GetFromJsonAsync<TaskResponse>(
                "/api/v1/projects/prj_royale/tasks/GAME-0001");
            Assert.Equal("Linked task body", detail!.Description);
            Assert.True(detail.Dependencies.Ready);
            Assert.Empty(detail.Dependencies.Missing);

            var settings = await client.GetFromJsonAsync<SettingsResponse>(
                "/api/v1/projects/prj_royale/settings");
            Assert.Contains(settings!.Tracks, track => track.Key == "GAME");

            var pages = await client.GetFromJsonAsync<List<WikiPageSummaryResponse>>(
                "/api/v1/projects/prj_royale/wiki/pages");
            Assert.Contains(pages!, page => page.Path == "guide/start");
            var page = await client.GetFromJsonAsync<WikiPageResponse>(
                "/api/v1/projects/prj_royale/wiki/pages/guide/start");
            Assert.Equal("Linked wiki body", page!.Body);

            using var denied = new HttpRequestMessage(HttpMethod.Post,
                "/api/v1/projects/prj_royale/tasks")
            {
                Content = JsonContent.Create(new CreateTaskRequest("No", "GAME")),
            };
            denied.Headers.Add(ApiV1Endpoints.ClientHeader, "api-test");
            var deniedResponse = await client.SendAsync(denied);
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
            Assert.Equal("linked_project_write_untrusted",
                (await deniedResponse.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Fact]
    public async Task CurrentWebReadsResolveReadableUntrustedParentDependencies()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateLinkedProject(
            Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateLinkedProject(
            Path.Combine(parent.RepositoryPath, "starfall"), "prj_starfall", "Starfall");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = [Declaration("prj_starfall", "starfall", "starfall")],
        });
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });
        var dependency = TestData.Task("GAME-0001", "Coordinator contract", track: "GAME");
        parent.WriteTask(dependency);
        parent.UpdateTaskState(dependency, "done");
        var task = TestData.Task(
            "GAME-0002",
            "Consume coordinator contract",
            track: "GAME",
            dependsOn: ["pm://project/prj_games/task/GAME-0001"]);
        child.WriteTask(task);
        child.UpdateTaskState(task, "todo");
        var family = LinkedFamily(child, workspace);

        var resolved = await family.ResolveAsync();
        var parentMember = resolved.Payload!.Members.Single(member => member.ProjectId == "prj_games");
        Assert.True(parentMember.Readable);
        Assert.False(parentMember.WriteTrusted);

        var (app, client) = await CreateApiClient(child, linkedProjectFamilyService: family);
        await using (app)
        using (client)
        {
            var board = await client.GetFromJsonAsync<BoardResponse>("/api/v1/board");
            var boardTask = Assert.Single(board!.MilestoneGroups.SelectMany(group => group.States)
                .SelectMany(state => state.Tasks), candidate => candidate.Id == task.Id);
            var detail = await client.GetFromJsonAsync<TaskResponse>("/api/v1/tasks/GAME-0002");
            var next = await client.GetFromJsonAsync<NextTaskResponse>("/api/v1/tasks/next?readyOnly=true");

            Assert.True(boardTask.Dependencies.Ready);
            Assert.True(detail!.Dependencies.Ready);
            Assert.True(next!.Found);
            Assert.Equal("GAME-0002", next.Task!.Id);

            using var denied = new HttpRequestMessage(HttpMethod.Post,
                "/api/v1/projects/prj_games/tasks")
            {
                Content = JsonContent.Create(new CreateTaskRequest("Denied write", "GAME")),
            };
            denied.Headers.Add(ApiV1Endpoints.ClientHeader, "api-test");
            var deniedResponse = await client.SendAsync(denied);
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
            Assert.Equal("linked_project_write_untrusted",
                (await deniedResponse.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);

            parent.UpdateTaskState(dependency, "todo");
            detail = await client.GetFromJsonAsync<TaskResponse>("/api/v1/tasks/GAME-0002");
            next = await client.GetFromJsonAsync<NextTaskResponse>("/api/v1/tasks/next?readyOnly=true");
            Assert.False(detail!.Dependencies.Ready);
            Assert.Contains("pm://project/prj_games/task/GAME-0001", detail.Dependencies.WaitingOn);
            Assert.False(next!.Found);
        }
    }

    [Fact]
    public async Task LinkedProjectApiGrantsLocalTrustAndReturnsMutationReceipts()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await workspace.CreateProject(TestData.Config(name: "Games"));
        await WriteProjectId(active, "prj_games");
        var child = await CreateLinkedProject(
            Path.Combine(workspace.Path, "royale"), "prj_royale", "Royale");
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = [Declaration("prj_royale", "royale", "royale")],
        });
        var task = TestData.Task("GAME-0001", "Linked task", "Body", track: "GAME");
        child.WriteTask(task);
        child.UpdateTaskState(task, "todo");

        var registry = new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
        {
            RootPath = Path.Combine(workspace.Path, "linked-api-registry"),
        });
        Assert.True(registry.Bind("prj_royale", child.RepositoryPath).Success);
        var family = LinkedFamily(active, workspace, registry);
        var nextIds = new ApiNextIdService();
        var mutations = new LinkedProjectMutationService(
            active, nextIds, family, registry, new TaskServiceFactory(TimeProvider.System));
        var (app, client) = await CreateApiClient(active, nextIdService: nextIds,
            linkedProjectFamilyService: family, linkedProjectMutationService: mutations,
            linkedProjectRegistry: registry);
        await using (app)
        using (client)
        {
            using var trust = new HttpRequestMessage(HttpMethod.Post,
                "/api/v1/project/links/prj_royale/write-trust")
            {
                Content = JsonContent.Create(new { }),
            };
            trust.Headers.Add(ApiV1Endpoints.ClientHeader, "api-test");
            var trustResponse = await client.SendAsync(trust);
            Assert.Equal(HttpStatusCode.OK, trustResponse.StatusCode);
            Assert.Contains((await trustResponse.Content.ReadFromJsonAsync<LinkedProjectFamilyResponse>())!.Members,
                member => member.ProjectId == "prj_royale" && member.WriteTrusted);

            var detail = await client.GetAsync("/api/v1/projects/prj_royale/tasks/GAME-0001");
            var current = (await detail.Content.ReadFromJsonAsync<TaskResponse>())!;
            using var update = new HttpRequestMessage(HttpMethod.Put,
                "/api/v1/projects/prj_royale/tasks/GAME-0001")
            {
                Content = JsonContent.Create(new UpdateTaskRequest(
                    "Updated linked task", current.State, current.Description, "inherit")),
            };
            update.Headers.Add(ApiV1Endpoints.ClientHeader, "api-test");
            update.Headers.TryAddWithoutValidation("If-Match", detail.Headers.ETag!.ToString());
            var updated = await client.SendAsync(update);

            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal("prj_royale", updated.Headers.GetValues("X-PM-Project-Id").Single());
            Assert.Equal([".pm/states/todo/GAME-0001.ref", ".pm/tasks/GAME-0001.md"],
                updated.Headers.GetValues("X-PM-Changed-Path").ToArray());
            Assert.Equal("Updated linked task",
                (await updated.Content.ReadFromJsonAsync<TaskResponse>())!.Title);
            Assert.DoesNotContain(active.GetAllTasks(), candidate => candidate.Id == task.Id);
        }
    }

    [Fact]
    public async Task LinkedProjectReadApiUsesExactIdsAndDistinguishesUnavailableProjects()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await workspace.CreateProject(TestData.Config(name: "Games"));
        await WriteProjectId(active, "prj_games");
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = [Declaration("prj_missing", "missing", "missing")],
        });

        var (app, client) = await CreateApiClient(
            active, linkedProjectFamilyService: LinkedFamily(active, workspace));
        await using (app)
        using (client)
        {
            var unknown = await client.GetAsync("/api/v1/projects/missing/project");
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal("unknown_linked_project",
                (await unknown.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);

            var unavailable = await client.GetAsync("/api/v1/projects/prj_missing/project");
            Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
            Assert.Equal("linked_project_unavailable",
                (await unavailable.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    private static LinkedProjectFamilyService LinkedFamily(
        ProjectRoot active,
        TempWorkingDirectory workspace,
        LinkedProjectRegistryStore? registry = null) =>
        new(
            active,
            new LinkedProjectService(active),
            new LinkedProjectResolver(
                registry ?? new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
                {
                    RootPath = Path.Combine(workspace.Path, "linked-api-registry"),
                }),
                new LinkedApiSubmoduleInspector()));

    private static LinkedProjectDeclaration Declaration(
        string projectId,
        string alias,
        string pathHint) => new()
    {
        ProjectId = projectId,
        Alias = alias,
        RepositoryUrl = $"https://example.test/{projectId}.git",
        PathHint = pathHint,
    };

    private static async Task<ProjectRoot> CreateLinkedProject(
        string path,
        string projectId,
        string name)
    {
        Directory.CreateDirectory(path);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = path;
        try
        {
            var root = new ProjectRoot();
            await root.CreateProject(TestData.Config(
                name: name,
                idPrefix: "GAME",
                tracks: new() { ["GAME"] = "Game" }));
            await WriteProjectId(root, projectId);
            return root;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private static Task WriteProjectId(ProjectRoot root, string projectId) =>
        File.WriteAllTextAsync(Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");

    private sealed class LinkedApiSubmoduleInspector : ILinkedProjectSubmoduleInspector
    {
        public Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
            string repositoryPath,
            string pathHint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<LinkedProjectRepairAction?>.Ok(null));
    }
}
