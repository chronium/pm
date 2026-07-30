using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class LinkedProjectServiceTests
{
    [Fact]
    public async Task MissingManifestKeepsStandaloneProjectValidWithoutStableId()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();

        var result = new LinkedProjectService(root).GetManifest();

        Assert.True(result.Success);
        Assert.False(result.Payload!.Exists);
        Assert.Equal(LinkedProjectManifest.CurrentVersion, result.Payload.Manifest.Version);
        Assert.Null(result.Payload.Manifest.Parent);
        Assert.Empty(result.Payload.Manifest.Children);
        Assert.False(File.Exists(root.LinkedProjectsPath));
    }

    [Fact]
    public async Task ManifestRoundTripsParentAndOrderedChildrenDeterministically()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateLinkedProject(workspace);
        var service = new LinkedProjectService(root);

        Assert.True(service.SetParent(Declaration(
            "prj_games", "games", "https://example.test/games.git", "..",
            "https://docs.example.test/games/")).Success);
        Assert.True(service.AddChild(Declaration(
            "prj_royale", "royale", "git@example.test:games/royale.git", "royale")).Success);
        Assert.True(service.AddChild(Declaration(
            "prj_starfall", "starfall", "ssh://git@example.test/games/starfall.git", "starfall",
            "https://docs.example.test/starfall/")).Success);

        var yaml = File.ReadAllText(root.LinkedProjectsPath);
        var result = service.GetManifest();

        Assert.True(result.Success);
        Assert.True(result.Payload!.Exists);
        Assert.Equal("prj_games", result.Payload.Manifest.Parent!.ProjectId);
        Assert.Equal(["prj_royale", "prj_starfall"],
            result.Payload.Manifest.Children.Select(child => child.ProjectId));
        Assert.Contains("version: 1", yaml);
        Assert.Contains("parent:", yaml);
        Assert.True(yaml.IndexOf("projectId: prj_royale", StringComparison.Ordinal) <
                    yaml.IndexOf("projectId: prj_starfall", StringComparison.Ordinal));
        Assert.DoesNotContain("publicSiteUrl: null", yaml);

        root.WriteLinkedProjectsManifest(root.ReadLinkedProjectsManifest()!);
        Assert.Equal(yaml, File.ReadAllText(root.LinkedProjectsPath));
    }

    [Fact]
    public async Task ExplicitMutationsPreserveIdentityOrderAndDeleteFinalManifest()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateLinkedProject(workspace);
        var service = new LinkedProjectService(root);

        Assert.True(service.SetParent(Declaration(
            "prj_games", "games", "https://example.test/games.git", "..")).Success);
        Assert.True(service.AddChild(Declaration(
            "prj_royale", "royale", "https://example.test/royale.git", "royale")).Success);
        Assert.True(service.AddChild(Declaration(
            "prj_starfall", "starfall", "https://example.test/starfall.git", "starfall")).Success);

        Assert.True(service.UpdateChild("prj_royale", Declaration(
            "prj_royale", "royale-game", "https://example.test/royale.git", "royale")).Success);
        Assert.True(service.ReorderChildren(["prj_starfall", "prj_royale"]).Success);
        var reordered = service.GetManifest().Payload!.Manifest;
        Assert.Equal(["prj_starfall", "prj_royale"],
            reordered.Children.Select(child => child.ProjectId));
        Assert.Equal("royale-game", reordered.Children[1].Alias);

        Assert.True(service.RemoveParent().Success);
        Assert.True(File.Exists(root.LinkedProjectsPath));
        Assert.True(service.RemoveChild("prj_starfall").Success);
        var finalRemoval = service.RemoveChild("prj_royale");

        Assert.True(finalRemoval.Success);
        Assert.False(finalRemoval.Payload!.Exists);
        Assert.False(File.Exists(root.LinkedProjectsPath));
    }

    [Fact]
    public async Task MutationsRequireStableProjectId()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();

        var result = new LinkedProjectService(root).AddChild(Declaration(
            "prj_child", "child", "https://example.test/child.git", "child"));

        Assert.Equal("missing_project_id", result.ErrorCode);
        Assert.False(File.Exists(root.LinkedProjectsPath));
    }

    [Fact]
    public async Task DuplicateAndSelfDeclarationsAreRejectedWithoutChangingManifest()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateLinkedProject(workspace);
        var service = new LinkedProjectService(root);
        Assert.True(service.SetParent(Declaration(
            "prj_games", "Games", "https://example.test/games.git", "..")).Success);
        var original = File.ReadAllText(root.LinkedProjectsPath);

        var duplicateAlias = service.AddChild(Declaration(
            "prj_other", "games", "https://example.test/other.git", "other"));
        var duplicateId = service.AddChild(Declaration(
            "prj_games", "other", "https://example.test/games.git", "other"));
        var self = service.AddChild(Declaration(
            "prj_current", "current", "https://example.test/current.git", "current"));

        Assert.Equal("duplicate_linked_project_alias", duplicateAlias.ErrorCode);
        Assert.Equal("duplicate_linked_project_id", duplicateId.ErrorCode);
        Assert.Equal("linked_project_self_reference", self.ErrorCode);
        Assert.Equal(original, File.ReadAllText(root.LinkedProjectsPath));
    }

    [Theory]
    [InlineData("../games/../other")]
    [InlineData("games//other")]
    [InlineData("./games")]
    [InlineData("/games")]
    [InlineData("C:/games")]
    [InlineData("~/games")]
    [InlineData("$HOME/games")]
    [InlineData("games\\other")]
    [InlineData("https://example.test/games")]
    public async Task UnsafeOrNonNormalizedPathHintsAreRejected(string pathHint)
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateLinkedProject(workspace);

        var result = new LinkedProjectService(root).AddChild(Declaration(
            "prj_child", "child", "https://example.test/child.git", pathHint));

        Assert.Equal("invalid_linked_project_path", result.ErrorCode);
        Assert.False(File.Exists(root.LinkedProjectsPath));
    }

    [Fact]
    public async Task InvalidDeclarationMetadataIsRejected()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateLinkedProject(workspace);
        var service = new LinkedProjectService(root);

        Assert.Equal("invalid_linked_project_alias", service.AddChild(Declaration(
            "prj_child", "bad alias", "https://example.test/child.git", "child")).ErrorCode);
        Assert.Equal("invalid_linked_project_repository", service.AddChild(Declaration(
            "prj_child", "child", "https://user:secret@example.test/child.git", "child")).ErrorCode);
        Assert.Equal("invalid_linked_project_public_site", service.AddChild(Declaration(
            "prj_child", "child", "https://example.test/child.git", "child", "file:///tmp/site")).ErrorCode);
        Assert.False(File.Exists(root.LinkedProjectsPath));
    }

    [Fact]
    public async Task IdentityChangingUpdateAndIncompleteOrderAreRejectedWithoutWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateLinkedProject(workspace);
        var service = new LinkedProjectService(root);
        Assert.True(service.AddChild(Declaration(
            "prj_one", "one", "https://example.test/one.git", "one")).Success);
        Assert.True(service.AddChild(Declaration(
            "prj_two", "two", "https://example.test/two.git", "two")).Success);
        var original = File.ReadAllText(root.LinkedProjectsPath);

        var identity = service.UpdateChild("prj_one", Declaration(
            "prj_replacement", "one", "https://example.test/one.git", "one"));
        var missing = service.ReorderChildren(["prj_one"]);
        var duplicate = service.ReorderChildren(["prj_one", "prj_one"]);

        Assert.Equal("linked_project_identity_change", identity.ErrorCode);
        Assert.Equal("invalid_linked_project_order", missing.ErrorCode);
        Assert.Equal("invalid_linked_project_order", duplicate.ErrorCode);
        Assert.Equal(original, File.ReadAllText(root.LinkedProjectsPath));
    }

    [Fact]
    public async Task ProjectValidationReportsBoundedManifestErrors()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateLinkedProject(workspace);
        File.WriteAllText(root.LinkedProjectsPath, "version: [unterminated");

        var malformed = new ProjectValidationService(root).ValidateProject();

        var malformedIssue = Assert.Single(malformed.Payload!.Issues);
        Assert.Equal("invalid_linked_projects_manifest", malformedIssue.Code);
        Assert.Equal(root.LinkedProjectsPath, malformedIssue.Path);

        File.WriteAllText(root.LinkedProjectsPath, "version: 2\nchildren: []\n");
        var unsupported = new ProjectValidationService(root).ValidateProject();

        var versionIssue = Assert.Single(unsupported.Payload!.Issues);
        Assert.Equal("unsupported_linked_projects_version", versionIssue.Code);
        Assert.False(unsupported.Payload.Valid);
    }

    private static async Task<ProjectRoot> CreateLinkedProject(TempWorkingDirectory workspace)
    {
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), "prj_current\n");
        return root;
    }

    private static LinkedProjectDeclaration Declaration(
        string projectId,
        string alias,
        string repositoryUrl,
        string? pathHint = null,
        string? publicSiteUrl = null) => new()
    {
        ProjectId = projectId,
        Alias = alias,
        RepositoryUrl = repositoryUrl,
        PathHint = pathHint,
        PublicSiteUrl = publicSiteUrl,
    };
}
