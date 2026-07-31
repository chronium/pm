using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

internal sealed class LinkedProjectIntegrationFixture : IDisposable
{
    private LinkedProjectIntegrationFixture(
        TempWorkingDirectory workspace,
        ProjectRoot games,
        ProjectRoot royale,
        ProjectRoot starfall,
        ProjectRoot standalone)
    {
        Workspace = workspace;
        Games = games;
        Royale = royale;
        Starfall = starfall;
        Standalone = standalone;
        RegistryPath = Path.Combine(workspace.Path, "registry");
    }

    public TempWorkingDirectory Workspace { get; }
    public ProjectRoot Games { get; }
    public ProjectRoot Royale { get; }
    public ProjectRoot Starfall { get; }
    public ProjectRoot Standalone { get; }
    public string RegistryPath { get; }

    public static async Task<LinkedProjectIntegrationFixture> CreateAsync()
    {
        var workspace = new TempWorkingDirectory();
        try
        {
            var games = await CreateProject(
                Path.Combine(workspace.Path, "games"),
                "prj_games",
                "Games",
                "SHARED",
                "Shared platform");
            var royale = await CreateProject(
                Path.Combine(games.RepositoryPath, "royale"),
                "prj_royale",
                "Royale",
                "ROYALE",
                "Royale game");
            var starfall = await CreateProject(
                Path.Combine(games.RepositoryPath, "starfall"),
                "prj_starfall",
                "Starfall",
                "STAR",
                "Starfall game");
            var standalone = await CreateProject(
                Path.Combine(workspace.Path, "standalone"),
                "prj_standalone",
                "Standalone",
                "SOLO",
                "Standalone project");

            games.WriteLinkedProjectsManifest(new LinkedProjectManifest
            {
                Children =
                [
                    Declaration("prj_royale", "royale", "royale"),
                    Declaration("prj_starfall", "starfall", "starfall"),
                    Declaration("prj_missing", "missing", "missing-game"),
                ],
            });
            royale.WriteLinkedProjectsManifest(new LinkedProjectManifest
            {
                Parent = Declaration("prj_games", "games", ".."),
            });
            starfall.WriteLinkedProjectsManifest(new LinkedProjectManifest
            {
                Parent = Declaration("prj_games", "games", ".."),
            });
            await File.WriteAllTextAsync(Path.Combine(games.RepositoryPath, ".gitmodules"), """
                [submodule "missing-game"]
                    path = missing-game
                    url = https://github.com/chronium/pm-link-fixture-missing.git
                """);

            AddTask(games, "SHARED-0001", "Define the family contract", "family-e2e shared contract", "SHARED", "done");
            AddTask(royale, "ROYALE-0001", "Implement Royale contract", "family-e2e royale implementation", "ROYALE", "done");
            AddTask(royale, "ROYALE-0002", "Consume Starfall contract", "family-e2e cross-project dependency", "ROYALE", "todo",
                ["pm://project/prj_starfall/task/STAR-0001"]);
            AddTask(starfall, "STAR-0001", "Publish Starfall contract", "family-e2e starfall implementation", "STAR", "todo",
                ["pm://project/prj_games/task/SHARED-0001"]);
            AddTask(standalone, "SOLO-0001", "Remain independent", "family-e2e standalone project", "SOLO", "todo");

            CreateWiki(games, "architecture/family", "Games family", "family-e2e shared architecture");
            CreateWiki(royale, "architecture/royale", "Royale architecture", "family-e2e royale architecture");
            CreateWiki(starfall, "architecture/starfall", "Starfall architecture", "family-e2e starfall architecture");
            CreateWiki(standalone, "architecture/standalone", "Standalone architecture", "family-e2e standalone architecture");

            return new LinkedProjectIntegrationFixture(workspace, games, royale, starfall, standalone);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    public LinkedProjectRegistryStore Registry() =>
        new(new LinkedProjectRegistryStoreOptions { RootPath = RegistryPath });

    public void Dispose() => Workspace.Dispose();

    private static async Task<ProjectRoot> CreateProject(
        string repositoryPath,
        string projectId,
        string name,
        string track,
        string trackName)
    {
        Directory.CreateDirectory(repositoryPath);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repositoryPath;
        try
        {
            var root = new ProjectRoot();
            await root.CreateProject(TestData.Config(
                name: name,
                idPrefix: track,
                tracks: new Dictionary<string, string> { [track] = trackName },
                milestones: new Dictionary<string, string> { ["m1"] = "First playable" }));
            await File.WriteAllTextAsync(
                Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");
            return root;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private static LinkedProjectDeclaration Declaration(string projectId, string alias, string pathHint) => new()
    {
        ProjectId = projectId,
        Alias = alias,
        RepositoryUrl = $"https://github.com/chronium/pm-link-fixture-{alias}.git",
        PathHint = pathHint,
        PublicSiteUrl = $"https://chronium.github.io/pm-link-fixture-{alias}/",
    };

    private static void AddTask(
        ProjectRoot root,
        string id,
        string title,
        string description,
        string track,
        string state,
        IReadOnlyList<string>? dependencies = null)
    {
        var task = TestData.Task(id, title, description, track, "m1", dependsOn: dependencies);
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private static void CreateWiki(ProjectRoot root, string path, string title, string body)
    {
        var result = new WikiService(root).CreatePage(path, title, body);
        if (!result.Success)
            throw new InvalidOperationException(result.Message ?? $"Could not create wiki page {path}.");
    }
}
