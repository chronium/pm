using PM.Application;
using PM.Project;
using PM.Tasks;
using PM.Wiki;

namespace PM.Tests;

public sealed class ProjectResourceReferenceTests
{
    [Theory]
    [InlineData("pm://project/prj_royale/task/ROY-0042", "prj_royale", ProjectResourceKind.Task, "ROY-0042")]
    [InlineData("PM://PROJECT/prj_games/wiki/architecture/rendering", "prj_games", ProjectResourceKind.Wiki,
        "architecture/rendering")]
    [InlineData("pm://project/prj_games/wiki/rendering%20notes/%E2%9C%93", "prj_games",
        ProjectResourceKind.Wiki, "rendering notes/✓")]
    public void CanonicalProjectReferencesParseAndFormat(
        string value,
        string projectId,
        ProjectResourceKind kind,
        string resourcePath)
    {
        Assert.True(ProjectResourceReference.TryParse(value, out var reference, out var message), message);
        Assert.Equal(projectId, reference!.ProjectId);
        Assert.Equal(kind, reference.Kind);
        Assert.Equal(resourcePath, reference.ResourcePath);

        Assert.True(ProjectResourceReference.TryParse(reference.ToCanonicalUri(), out var roundTrip, out message),
            message);
        Assert.Equal(reference, roundTrip);
    }

    [Theory]
    [InlineData("http://project/prj/task/TASK-1")]
    [InlineData("pm://other/prj/task/TASK-1")]
    [InlineData("pm://user@project/prj/task/TASK-1")]
    [InlineData("pm://project:42/prj/task/TASK-1")]
    [InlineData("pm://project/prj/task")]
    [InlineData("pm://project/prj/task/TASK-1/extra")]
    [InlineData("pm://project/prj/track/BUILD")]
    [InlineData("pm://project/prj/wiki/architecture//rendering")]
    [InlineData("pm://project/prj/wiki/architecture/../rendering")]
    [InlineData("pm://project/prj/wiki/architecture%2Frendering")]
    [InlineData("pm://project/prj/wiki/rendering.md")]
    [InlineData("pm://project/prj/wiki/%FF")]
    [InlineData("pm://project/prj/wiki/%2")]
    [InlineData("pm://project/prj/wiki/page?view=1")]
    [InlineData("pm://project/prj/wiki/page#heading")]
    public void MalformedProjectReferencesAreRejected(string value)
    {
        Assert.False(ProjectResourceReference.TryParse(value, out _, out var message));
        Assert.NotEmpty(message);
    }

    [Fact]
    public void ProjectReferenceCreationEscapesEachResourceSegment()
    {
        Assert.True(ProjectResourceReference.TryCreate(
            "prj_games", ProjectResourceKind.Wiki, "rendering notes/✓", out var reference, out var message),
            message);

        Assert.Equal("pm://project/prj_games/wiki/rendering%20notes/%E2%9C%93", reference!.ToCanonicalUri());
    }

    [Fact]
    public void SelectorsResolveCurrentParentAliasesAndStableIds()
    {
        var manifest = new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games"),
            Children = [Declaration("prj_second", "second")],
        };

        Assert.Equal("prj_royale",
            LinkedProjectSelector.ResolveProjectId("prj_royale", manifest, "current").Payload);
        Assert.Equal("prj_games",
            LinkedProjectSelector.ResolveProjectId("prj_royale", manifest, "PARENT").Payload);
        Assert.Equal("prj_second",
            LinkedProjectSelector.ResolveProjectId("prj_royale", manifest, "SECOND").Payload);
        Assert.Equal("prj_external",
            LinkedProjectSelector.ResolveProjectId("prj_royale", manifest, "prj_external").Payload);

        var unknown = LinkedProjectSelector.ResolveProjectId("prj_royale", manifest, "not a selector");
        Assert.Equal("unknown_linked_project", unknown.ErrorCode);
        Assert.Contains("second (prj_second)", unknown.Message);
    }

    [Fact]
    public void AmbiguousAliasesAndMissingCurrentIdentityFail()
    {
        var manifest = new LinkedProjectManifest
        {
            Children = [Declaration("prj_one", "game"), Declaration("prj_two", "GAME")],
        };

        Assert.Equal("ambiguous_linked_project",
            LinkedProjectSelector.ResolveProjectId("prj_current", manifest, "game").ErrorCode);
        Assert.Equal("missing_project_id",
            LinkedProjectSelector.ResolveProjectId(null, manifest, "current").ErrorCode);
    }

    [Fact]
    public async Task ModeledTaskWritesCompactLocalReferencesAndPreserveQualifiedReferences()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        File.WriteAllText(Path.Combine(root.RootPath!, GlobalConfig.ProjectIdFile), "prj_current\n");
        var task = TestData.Task("PM-0001", "Task");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var service = new TaskService(root, new UnusedNextIdService());

        var result = service.PatchTaskMetadata("PM-0001", dependsOn:
        [
            "pm://project/prj_current/task/PM-0002",
            "pm://project/prj_other/task/OTHER-0001",
        ]);

        Assert.True(result.Success);
        Assert.Equal(["PM-0002", "pm://project/prj_other/task/OTHER-0001"], result.Payload!.Task.DependencyIds);
        Assert.Equal("invalid_dependency_reference",
            service.PatchTaskMetadata("PM-0001", dependsOn: ["pm://project/prj_other/wiki/page"]).ErrorCode);
        Assert.Equal("invalid_dependency_reference",
            service.PatchTaskMetadata("PM-0001", dependsOn: ["pm:not-a-reference"]).ErrorCode);
        Assert.Equal("invalid_dependency",
            service.PatchTaskMetadata("PM-0001",
                dependsOn: ["pm://project/prj_current/task/PM-0001"]).ErrorCode);

        var malformedMarkdown = (task with { DependsOn = ["pm:not-a-reference"] }).ToMarkdown();
        Assert.Equal("invalid_dependency_reference",
            service.SaveEditedTaskContent("PM-0001", malformedMarkdown).ErrorCode);
    }

    [Fact]
    public async Task ValidationSeparatesLocalAndQualifiedDependenciesWithoutTargetCheckout()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        File.WriteAllText(Path.Combine(root.RootPath!, GlobalConfig.ProjectIdFile), "prj_current\n");
        var dependency = TestData.Task("PM-0002", "Dependency");
        var task = TestData.Task("PM-0001", "Task", dependsOn:
        [
            "pm://project/prj_current/task/PM-0002",
            "pm://project/prj_other/task/OTHER-0001",
        ]);
        root.WriteTask(dependency);
        root.WriteTask(task);
        root.UpdateTaskState(dependency, "done");
        root.UpdateTaskState(task, "todo");

        var validation = await new ProjectValidationService(root).ValidateProjectAsync();
        Assert.DoesNotContain(validation.Payload!.Issues, issue => issue.Code == "missing_dependency");

        var boardTask = new BoardService(root).GetTask("PM-0001").Payload!;
        Assert.False(boardTask.Dependencies.Ready);
        Assert.Equal(["pm://project/prj_other/task/OTHER-0001"], boardTask.Dependencies.Missing);
    }

    [Fact]
    public async Task ManuallyStoredMalformedReferencesRemainReadableAndDoctorReportsThem()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Task", dependsOn: ["pm:not-a-reference"]);
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");

        Assert.NotNull(root.GetAllTasks().Single());
        var validation = await new ProjectValidationService(root).ValidateProjectAsync();
        Assert.Contains(validation.Payload!.Issues, issue => issue.Code == "invalid_dependency_reference");
    }

    [Fact]
    public void TaskAndWikiMarkdownPreserveCanonicalReferences()
    {
        const string link = "pm://project/prj_games/wiki/architecture/rendering";
        var task = TestData.Task("PM-0001", "Task", $"[Rendering]({link})");
        var parsedTask = TaskItem.Parse(task.ToMarkdown());
        Assert.Equal(task.Description, parsedTask!.Description);

        var wiki = new WikiPage
        {
            Path = "architecture/links",
            Title = "Links",
            Body = $"[Rendering]({link})",
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };
        var parsedWiki = WikiPage.Parse(wiki.Path, wiki.ToMarkdown());
        Assert.Equal(wiki.Body, parsedWiki!.Body);
    }

    private static LinkedProjectDeclaration Declaration(string projectId, string alias) => new()
    {
        ProjectId = projectId,
        Alias = alias,
        RepositoryUrl = $"https://example.test/{alias}.git",
    };

    private sealed class UnusedNextIdService : INextIdService
    {
        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> Healthy(ProjectConfig config,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
