using System.Net;
using System.Net.Http.Json;
using PM.Api;
using PM.Application;
using PM.Project;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task LinkedProjectReadApiServesProjectBoardTasksWikiAndSettingsWithoutWrites()
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
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });
        var task = TestData.Task("GAME-0001", "Linked task", "Linked task body", track: "GAME");
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
            Assert.Contains(board.MilestoneGroups.SelectMany(group => group.States)
                .SelectMany(state => state.Tasks), candidate => candidate.Id == task.Id);

            var detail = await client.GetFromJsonAsync<TaskResponse>(
                "/api/v1/projects/prj_royale/tasks/GAME-0001");
            Assert.Equal("Linked task body", detail!.Description);

            var settings = await client.GetFromJsonAsync<SettingsResponse>(
                "/api/v1/projects/prj_royale/settings");
            Assert.Contains(settings!.Tracks, track => track.Key == "GAME");

            var pages = await client.GetFromJsonAsync<List<WikiPageSummaryResponse>>(
                "/api/v1/projects/prj_royale/wiki/pages");
            Assert.Contains(pages!, page => page.Path == "guide/start");
            var page = await client.GetFromJsonAsync<WikiPageResponse>(
                "/api/v1/projects/prj_royale/wiki/pages/guide/start");
            Assert.Equal("Linked wiki body", page!.Body);

            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync("/api/v1/projects/prj_royale/tasks", new { title = "No" }))
                .StatusCode);
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
        TempWorkingDirectory workspace) =>
        new(
            active,
            new LinkedProjectService(active),
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
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
