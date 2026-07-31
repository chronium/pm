using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class LinkedProjectFamilyTests
{
    [Fact]
    public async Task StandaloneProjectReturnsOnlyTheActiveProject()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active", "Active");

        var result = await Family(active, workspace).ResolveAsync();

        Assert.True(result.Success);
        var member = Assert.Single(result.Payload!.Members);
        Assert.Equal("prj_active", member.ProjectId);
        Assert.Equal(LinkedProjectRelationship.Current, member.Relationship);
        Assert.Empty(result.Payload.Warnings);
    }

    [Fact]
    public async Task ParentViewUsesDeclaredChildOrderAndKeepsUntrustedProjectsReadable()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var royale = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        var starfall = await CreateProject(Path.Combine(workspace.Path, "games", "starfall"), "prj_starfall", "Starfall");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_royale", "royale", "royale"),
                Declaration("prj_starfall", "starfall", "starfall"),
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

        var result = await Family(parent, workspace).ResolveAsync();

        Assert.True(result.Success);
        Assert.Equal(["prj_games", "prj_royale", "prj_starfall"],
            result.Payload!.Members.Select(member => member.ProjectId));
        Assert.All(result.Payload.Members.Skip(1), member =>
        {
            Assert.Equal(LinkedProjectRelationship.Child, member.Relationship);
            Assert.Equal(LinkedProjectResolutionStatus.UntrustedForWrite, member.Status);
            Assert.True(member.Readable);
            Assert.False(member.WriteTrusted);
        });
        Assert.Empty(result.Payload.Warnings);
    }

    [Fact]
    public async Task ChildViewResolvesParentThenSiblingsRelativeToParent()
    {
        using var workspace = new TempWorkingDirectory();
        var gamesPath = Path.Combine(workspace.Path, "games");
        var parent = await CreateProject(gamesPath, "prj_games", "Games");
        var royale = await CreateProject(Path.Combine(gamesPath, "royale"), "prj_royale", "Royale");
        var starfall = await CreateProject(Path.Combine(gamesPath, "starfall"), "prj_starfall", "Starfall");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_royale", "royale", "royale"),
                Declaration("prj_starfall", "starfall", "starfall"),
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

        var result = await Family(royale, workspace).ResolveAsync();

        Assert.True(result.Success);
        Assert.Equal(["prj_royale", "prj_games", "prj_starfall"],
            result.Payload!.Members.Select(member => member.ProjectId));
        Assert.Equal(LinkedProjectRelationship.Parent, result.Payload.Members[1].Relationship);
        Assert.Equal(LinkedProjectRelationship.Sibling, result.Payload.Members[2].Relationship);
        Assert.Equal(starfall.RepositoryPath, result.Payload.Members[2].RepositoryPath);
        Assert.Empty(result.Payload.Warnings);
    }

    [Fact]
    public async Task MissingAndNonReciprocalLinksReturnPartialFamilyWarnings()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        _ = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_royale", "royale", "royale"),
                Declaration("prj_missing", "missing", "missing"),
            ],
        });

        var result = await Family(parent, workspace).ResolveAsync();

        Assert.True(result.Success);
        Assert.Equal(3, result.Payload!.Members.Count);
        Assert.Contains(result.Payload.Warnings, warning =>
            warning.Code == "non_reciprocal_linked_project" && warning.TargetProjectId == "prj_games");
        Assert.Contains(result.Payload.Warnings, warning =>
            warning.Code == "linked_project_missing" && warning.TargetProjectId == "prj_missing");
        Assert.False(result.Payload.Members.Single(member => member.ProjectId == "prj_missing").Readable);
    }

    [Fact]
    public async Task InvalidLinkedManifestWarnsWithoutRemovingReadableProject()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "child"), "prj_child", "Child");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = [Declaration("prj_child", "child", "child")],
        });
        await File.WriteAllTextAsync(child.LinkedProjectsPath, "version: [invalid");

        var result = await Family(parent, workspace).ResolveAsync();

        Assert.True(result.Success);
        var member = result.Payload!.Members.Single(item => item.ProjectId == "prj_child");
        Assert.True(member.Readable);
        Assert.Equal(LinkedProjectResolutionStatus.Invalid, member.Status);
        Assert.Contains(result.Payload.Warnings, warning => warning.Code == "invalid_linked_projects_manifest");
    }

    [Fact]
    public async Task ParentOfParentProducesDepthWarningWithoutTraversal()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "child"), "prj_child", "Child");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_outer", "outer", ".."),
            Children = [Declaration("prj_child", "child", "child")],
        });
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });

        var result = await Family(child, workspace).ResolveAsync();

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Payload!.Members, member => member.ProjectId == "prj_outer");
        Assert.Contains(result.Payload.Warnings, warning => warning.Code == "linked_project_depth_exceeded");
    }

    [Fact]
    public async Task ParentPointingBackToActiveProjectProducesCycleWarning()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "child"), "prj_child", "Child");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_child", "child", "child"),
        });
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });

        var result = await Family(child, workspace).ResolveAsync();

        Assert.True(result.Success);
        Assert.Contains(result.Payload!.Warnings, warning =>
            warning.Code == "linked_project_cycle" && warning.TargetProjectId == "prj_child");
    }

    [Fact]
    public async Task AliasesDuplicatedAcrossManifestsProduceFamilyWarning()
    {
        using var workspace = new TempWorkingDirectory();
        var gamesPath = Path.Combine(workspace.Path, "games");
        var parent = await CreateProject(gamesPath, "prj_games", "Games");
        var active = await CreateProject(Path.Combine(gamesPath, "active"), "prj_active", "Active");
        var sibling = await CreateProject(Path.Combine(gamesPath, "sibling"), "prj_sibling", "Sibling");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_active", "active", "active"),
                Declaration("prj_sibling", "games", "sibling"),
            ],
        });
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });
        sibling.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });

        var result = await Family(active, workspace).ResolveAsync();

        Assert.True(result.Success);
        Assert.Contains(result.Payload!.Warnings, warning =>
            warning.Code == "duplicate_linked_project_alias" && warning.TargetProjectId == "prj_sibling");
    }

    [Fact]
    public async Task FamilyTraversalIsBoundedToThirtyTwoProjectsAndWarningsAreBounded()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = Enumerable.Range(1, 40)
                .Select(index => Declaration($"prj_child_{index}", $"child-{index}", $"child-{index}"))
                .ToList(),
        });

        var result = await Family(parent, workspace).ResolveAsync();

        Assert.True(result.Success);
        Assert.Equal(LinkedProjectFamilyService.MaximumProjectCount, result.Payload!.Members.Count);
        Assert.Contains(result.Payload.Warnings, warning => warning.Code == "linked_project_count_exceeded");
        Assert.True(result.Payload.Warnings.Count <= LinkedProjectFamilyService.MaximumWarningCount);
    }

    [Fact]
    public async Task ProjectValidationRemainsValidWithLinkedWarnings()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active", "Active");
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = [Declaration("prj_missing", "missing", "missing")],
        });
        var task = TestData.Task("PM-0001", "Blocked by unavailable project",
            dependsOn: ["pm://project/prj_missing/task/PM-0002"]);
        active.WriteTask(task);
        active.UpdateTaskState(task, "todo");
        var linkedProjects = new LinkedProjectService(active);
        var family = Family(active, workspace, linkedProjects);

        var result = await new ProjectValidationService(active, linkedProjects, family).ValidateProjectAsync();

        Assert.True(result.Success);
        Assert.True(result.Payload!.Valid);
        var warning = Assert.Single(result.Payload.Issues, issue => issue.Code == "linked_project_missing");
        Assert.Equal("warning", warning.Severity);
        Assert.Equal("prj_missing", warning.ProjectId);
        Assert.Null(warning.Path);
        Assert.Contains(result.Payload.Issues, issue =>
            issue.Code == "dependency_graph_incomplete" && issue.Severity == "warning");
    }

    private static LinkedProjectFamilyService Family(
        ProjectRoot active,
        TempWorkingDirectory workspace,
        LinkedProjectService? linkedProjects = null) =>
        new(active,
            linkedProjects ?? new LinkedProjectService(active),
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
                {
                    RootPath = Path.Combine(workspace.Path, "registry"),
                }),
                new NullSubmoduleInspector()));

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

    private static async Task<ProjectRoot> CreateProject(
        string repositoryPath,
        string projectId,
        string name)
    {
        Directory.CreateDirectory(repositoryPath);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repositoryPath;
        try
        {
            var root = new ProjectRoot();
            var config = TestData.Config();
            config.Name = name;
            await root.CreateProject(config);
            await File.WriteAllTextAsync(Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");
            return root;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private sealed class NullSubmoduleInspector : ILinkedProjectSubmoduleInspector
    {
        public Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
            string repositoryPath,
            string pathHint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<LinkedProjectRepairAction?>.Ok(null));
    }
}
