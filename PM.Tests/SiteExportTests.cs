using System.Text;
using PM.Application;
using PM.Project;
using PM.Site;
using PM.Web;

namespace PM.Tests;

public class SiteExportTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 7, 27, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SnapshotContainsCompletePublicProjectDataWithoutLocalMetadata()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            nextIdServiceUrl: "https://secret-next-id.example.test",
            milestones: new Dictionary<string, string> { ["launch"] = "Launch" }));
        var dependency = TestData.Task("PM-0001", "Dependency");
        var task = TestData.Task("PM-0002", "Export", "Private task body", milestone: "launch",
            dependsOn: [dependency.Id]);
        projectRoot.WriteTask(dependency);
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(dependency, "done");
        projectRoot.UpdateTaskState(task, "todo");
        Assert.True(new WikiService(projectRoot).CreatePage("guide/nested", "Nested guide", "Wiki body").Success);

        var snapshot = await CreateSnapshotBuilder(projectRoot).BuildAsync(GeneratedAt);

        Assert.True(snapshot.Success);
        var payload = snapshot.Payload!;
        var json = SiteExportService.SerializeSnapshot(payload);
        Assert.Contains("Private task body", json);
        Assert.Contains("PM-0001", json);
        Assert.Contains("guide/nested", json);
        Assert.Contains("Wiki body", json);
        Assert.Contains("\"generatedAt\": \"2026-07-27T12:30:00+00:00\"", json);
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nextId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-next-id", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(projectRoot.RootPath, json, StringComparison.Ordinal);
        Assert.Equal(4, payload.SchemaVersion);
        Assert.Equal("static-snapshot", payload.Activation.Revision);
        Assert.Null(payload.ProjectId);
        Assert.Empty(payload.LinkedProjects);
        Assert.Equal(json, SiteExportService.SerializeSnapshot(payload));
    }

    [Fact]
    public async Task SnapshotPublishesLinkedSiteMetadataWithoutRequiringTheLinkedCheckout()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile),
            "prj_games\n");
        projectRoot.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                new LinkedProjectDeclaration
                {
                    ProjectId = "prj_royale",
                    Alias = "royale",
                    RepositoryUrl = "https://example.test/royale.git",
                    PathHint = "missing-royale",
                    PublicSiteUrl = "https://example.test/sites/royale/?source=games#old",
                },
            ],
        });

        var snapshot = await CreateSnapshotBuilder(projectRoot).BuildAsync(GeneratedAt);

        Assert.True(snapshot.Success, snapshot.Message);
        Assert.Equal("prj_games", snapshot.Payload!.ProjectId);
        var linked = Assert.Single(snapshot.Payload.LinkedProjects);
        Assert.Equal("prj_royale", linked.ProjectId);
        Assert.Equal("royale", linked.Name);
        Assert.Equal("child", linked.Relationship);
        Assert.Equal("https://example.test/sites/royale/?source=games#old", linked.PublicSiteUrl);
        var json = SiteExportService.SerializeSnapshot(snapshot.Payload);
        Assert.DoesNotContain("repositoryPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missing-royale", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChildSnapshotKeepsItsParentSiteHintWhenTheParentCheckoutIsMissing()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile),
            "prj_royale\n");
        projectRoot.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = new LinkedProjectDeclaration
            {
                ProjectId = "prj_games",
                Alias = "games",
                RepositoryUrl = "https://example.test/games.git",
                PathHint = "missing-games",
                PublicSiteUrl = "https://example.test/sites/games/",
            },
        });

        var snapshot = await CreateSnapshotBuilder(projectRoot).BuildAsync(GeneratedAt);

        Assert.True(snapshot.Success, snapshot.Message);
        var parent = Assert.Single(snapshot.Payload!.LinkedProjects);
        Assert.Equal("parent", parent.Relationship);
        Assert.Equal("https://example.test/sites/games/", parent.PublicSiteUrl);
    }

    [Fact]
    public async Task BuildWritesRelativeStaticAssetsSnapshotAndNoJekyll()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var assets = new MemoryAssetStore(new Dictionary<string, string>
        {
            ["index.html"] = "<html><head><base href=\"/\"></head><body></body></html>",
            ["assets/main.js"] = "console.log('site')",
        });

        var result = await CreateExportService(projectRoot).BuildAsync("public", false, assets, GeneratedAt);

        Assert.True(result.Success, result.Message);
        var index = File.ReadAllText(Path.Combine(result.Payload!, "index.html"));
        Assert.Contains("<base href=\"./\">", index);
        Assert.Contains("name=\"pm-site-mode\" content=\"static\"", index);
        Assert.Contains("name=\"pm-site-snapshot\" content=\"./pm-snapshot.json\"", index);
        Assert.True(File.Exists(Path.Combine(result.Payload!, "pm-snapshot.json")));
        Assert.True(File.Exists(Path.Combine(result.Payload!, ".nojekyll")));
        Assert.Equal("console.log('site')", File.ReadAllText(Path.Combine(result.Payload!, "assets", "main.js")));
    }

    [Fact]
    public async Task BuildRequiresForceForNonEmptyDestinationAndReplacesIt()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var output = Path.Combine(workspace.Path, "existing");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.txt"), "old");
        var service = CreateExportService(projectRoot);

        var rejected = await service.BuildAsync(output, false, ValidAssets(), GeneratedAt);
        var replaced = await service.BuildAsync(output, true, ValidAssets(), GeneratedAt);

        Assert.False(rejected.Success);
        Assert.Equal("site_output_exists", rejected.ErrorCode);
        Assert.True(replaced.Success, replaced.Message);
        Assert.False(File.Exists(Path.Combine(output, "old.txt")));
        Assert.True(File.Exists(Path.Combine(output, "index.html")));
    }

    [Fact]
    public async Task FailedStagingLeavesExistingDestinationUntouched()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var output = Path.Combine(workspace.Path, "existing");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "keep.txt"), "keep");
        var invalidAssets = new MemoryAssetStore(new Dictionary<string, string>
        {
            ["index.html"] = "<html><head></head></html>",
            ["../escape.js"] = "bad",
        });

        var result = await CreateExportService(projectRoot).BuildAsync(output, true, invalidAssets, GeneratedAt);

        Assert.False(result.Success);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(workspace.Path, "escape.js")));
    }

    [Fact]
    public async Task BuildRejectsMissingAssetsAndDangerousDestinations()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = CreateExportService(projectRoot);
        var projectDirectory = Directory.GetParent(projectRoot.RootPath)!.FullName;

        Assert.Equal("missing_angular_assets",
            (await service.BuildAsync("site", false, new MemoryAssetStore(new Dictionary<string, string>()), GeneratedAt)).ErrorCode);
        Assert.Equal("unsafe_site_output",
            (await service.BuildAsync(projectDirectory, false, ValidAssets(), GeneratedAt)).ErrorCode);
        Assert.Equal("unsafe_site_output",
            (await service.BuildAsync(projectRoot.RootPath, false, ValidAssets(), GeneratedAt)).ErrorCode);
        Assert.Equal("unsafe_site_output",
            (await service.BuildAsync(Path.Combine(projectRoot.RootPath, "site"), false, ValidAssets(), GeneratedAt)).ErrorCode);
        Assert.Equal("unsafe_site_output",
            (await service.BuildAsync(Directory.GetParent(projectDirectory)!.FullName, false, ValidAssets(), GeneratedAt)).ErrorCode);
    }

    [Fact]
    public async Task BuildRejectsExistingSymlink()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var target = Path.Combine(workspace.Path, "target");
        var link = Path.Combine(workspace.Path, "linked-output");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);

        var result = await CreateExportService(projectRoot).BuildAsync(link, true, ValidAssets(), GeneratedAt);

        Assert.False(result.Success);
        Assert.Equal("unsafe_site_output", result.ErrorCode);
    }

    [Fact]
    public async Task BuildOutsideProjectFailsClearly()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();

        var result = await CreateExportService(projectRoot).BuildAsync("site", false, ValidAssets(), GeneratedAt);

        Assert.False(result.Success);
        Assert.Equal("missing_project", result.ErrorCode);
    }

    private static SiteExportService CreateExportService(ProjectRoot projectRoot) =>
        new(projectRoot, CreateSnapshotBuilder(projectRoot));

    private static SiteSnapshotBuilder CreateSnapshotBuilder(ProjectRoot projectRoot)
    {
        var activation = TestMilestoneActivationServices.Create(projectRoot);
        return new(
            projectRoot,
            new ProjectConfigService(projectRoot),
            TestBoardServices.Create(projectRoot),
            new WikiService(projectRoot),
            activation.Resolver,
            activation.Validator,
            new LinkedProjectService(projectRoot),
            LinkedProjectFamilyService.CreateDefault(projectRoot));
    }

    private static MemoryAssetStore ValidAssets() => new(new Dictionary<string, string>
    {
        ["index.html"] = "<html><head><base href=\"/\"></head><body></body></html>",
    });

    private sealed class MemoryAssetStore(IReadOnlyDictionary<string, string> values) : IAngularAssetStore
    {
        public bool HasAssets => values.ContainsKey("index.html");
        public IReadOnlyCollection<string> Paths => values.Keys.ToArray();

        public bool TryGet(string path, out AngularAsset asset)
        {
            if (values.TryGetValue(path, out var value))
            {
                asset = new AngularAsset(Encoding.UTF8.GetBytes(value));
                return true;
            }

            asset = null!;
            return false;
        }
    }
}
