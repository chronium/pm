using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PM.Application;
using PM.Mcp;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class LinkedProjectReadServiceTests
{
    [Fact]
    public async Task CurrentReadsBypassInvalidLinkedManifestAndIncludeOwnership()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active", "Active");
        AddTask(active, "PM-0001", "Current task", "search needle");
        Assert.True(new WikiService(active).CreatePage("guide", "Guide", "search needle").Success);
        await File.WriteAllTextAsync(active.LinkedProjectsPath, "version: [invalid");
        var service = Service(active, workspace, new FixedGitInspector("abc123", true));

        var tasks = await service.ListTasksAsync(new LinkedProjectReadRequest());
        var explicitCurrent = await service.ListTasksAsync(
            new LinkedProjectReadRequest(LinkedProjectReadScope.Project, "current"));
        var pages = await service.ListWikiPagesAsync(new LinkedProjectReadRequest());
        var taskSearch = await service.SearchTasksAsync("needle");
        var wikiSearch = await service.SearchWikiPagesAsync("needle");

        Assert.True(tasks.Success);
        var task = Assert.Single(tasks.Payload!.Items);
        Assert.Equal("PM-0001", task.Resource.Task.Id);
        AssertOwner(task.Owner, "prj_active", "Active", "current", "abc123", true);
        Assert.Empty(tasks.Payload.Warnings);
        Assert.Equal("PM-0001", Assert.Single(explicitCurrent.Payload!.Items).Resource.Task.Id);
        Assert.Equal("guide", Assert.Single(pages.Payload!.Items).Resource.Path);
        Assert.Equal("PM-0001", Assert.Single(taskSearch.Payload!.Items).Resource.Task.Id);
        Assert.Equal("guide", Assert.Single(wikiSearch.Payload!.Items).Resource.Path);
    }

    [Fact]
    public async Task FamilyReadsUseFamilyOrderAndSearchUsesGlobalRelevance()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Parent task", "needle");
        AddTask(child, "PM-0002", "Child task", "needle needle");
        Assert.True(new WikiService(parent).CreatePage("parent", "Parent", "needle").Success);
        Assert.True(new WikiService(child).CreatePage("child", "Child", "needle needle").Success);
        var service = Service(parent, workspace);
        var family = new LinkedProjectReadRequest(LinkedProjectReadScope.Family);

        var tasks = await service.ListTasksAsync(family);
        var pages = await service.ListWikiPagesAsync(family);
        var taskSearch = await service.SearchTasksAsync("needle", limit: 1, request: family);
        var wikiSearch = await service.SearchWikiPagesAsync("needle", limit: 1, request: family);

        Assert.Equal(["prj_games", "prj_royale"],
            tasks.Payload!.Items.Select(item => item.Owner.ProjectId));
        Assert.Equal(["prj_games", "prj_royale"],
            pages.Payload!.Items.Select(item => item.Owner.ProjectId));
        Assert.Equal("prj_royale", Assert.Single(taskSearch.Payload!.Items).Owner.ProjectId);
        Assert.Equal("prj_royale", Assert.Single(wikiSearch.Payload!.Items).Owner.ProjectId);
        Assert.All(tasks.Payload.Items, item => Assert.Null(item.Owner.Revision));
        Assert.Empty(tasks.Payload.Warnings);
    }

    [Fact]
    public async Task ExplicitTaskAndWikiReadsResolveParentAliasAndStableId()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Parent task");
        await File.AppendAllTextAsync(parent.GetTaskFilePath("PM-0001"), "\n<!-- exact task markdown -->\n");
        Assert.True(new WikiService(parent).CreatePage("guide", "Guide", "Body").Success);
        var service = Service(child, workspace);

        var byParent = await service.GetTaskAsync("PM-0001", "parent");
        var byId = await service.GetTaskAsync("PM-0001", "prj_games");
        var wiki = await service.GetWikiPageAsync("guide", "games");
        var outline = await service.OutlineWikiPageAsync("guide", "parent");
        var unknown = await service.GetTaskAsync("PM-0001", "unknown");

        var parentTask = Assert.Single(byParent.Payload!.Items);
        Assert.Equal("prj_games", parentTask.Owner.ProjectId);
        Assert.Contains("<!-- exact task markdown -->", parentTask.Resource.Markdown);
        Assert.Equal("prj_games", Assert.Single(byId.Payload!.Items).Owner.ProjectId);
        Assert.Equal("prj_games", Assert.Single(wiki.Payload!.Items).Owner.ProjectId);
        Assert.Equal("prj_games", Assert.Single(outline.Payload!.Items).Owner.ProjectId);
        Assert.Equal("guide", Assert.Single(outline.Payload.Items).Resource.Path);
        Assert.False(unknown.Success);
        Assert.Equal("unknown_linked_project", unknown.ErrorCode);
    }

    [Fact]
    public async Task UnavailableAndAmbiguousSelectorsFailWithoutGuessing()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var active = await CreateProject(Path.Combine(workspace.Path, "games", "active"), "prj_active", "Active");
        var sibling = await CreateProject(Path.Combine(workspace.Path, "games", "sibling"), "prj_sibling", "Sibling");
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_active", "active", "active"),
                Declaration("prj_sibling", "shared", "sibling"),
                Declaration("prj_missing", "missing", "missing"),
            ],
        });
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "shared", ".."),
        });
        sibling.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });
        var service = Service(active, workspace);

        var ambiguous = await service.GetTaskAsync("PM-0001", "shared");
        var unavailable = await service.GetTaskAsync("PM-0001", "missing");

        Assert.False(ambiguous.Success);
        Assert.Equal("ambiguous_linked_project", ambiguous.ErrorCode);
        Assert.False(unavailable.Success);
        Assert.Equal("linked_project_unavailable", unavailable.ErrorCode);
    }

    [Fact]
    public async Task FamilyReadsKeepPartialResultsWhenLinkedContentIsMalformed()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Parent task", "needle");
        await File.WriteAllTextAsync(Path.Combine(child.TasksPath, "PM-0002.md"), "not task markdown");
        Assert.True(new WikiService(parent).CreatePage("parent", "Parent", "Body").Success);
        await File.WriteAllTextAsync(Path.Combine(child.WikiPath, "broken.md"), "not wiki markdown");
        var service = Service(parent, workspace);
        var family = new LinkedProjectReadRequest(LinkedProjectReadScope.Family);

        var tasks = await service.SearchTasksAsync("needle", request: family);
        var pages = await service.ListWikiPagesAsync(family);

        Assert.True(tasks.Success);
        Assert.Equal("PM-0001", Assert.Single(tasks.Payload!.Items).Resource.Task.Id);
        Assert.Contains(tasks.Payload.Warnings, warning =>
            warning.Code == "invalid_task_markdown" && warning.TargetProjectId == "prj_royale");
        Assert.True(pages.Success);
        Assert.Equal("parent", Assert.Single(pages.Payload!.Items).Resource.Path);
        Assert.Contains(pages.Payload.Warnings, warning =>
            warning.Code == "invalid_wiki_markdown" && warning.TargetProjectId == "prj_royale");
    }

    [Fact]
    public async Task ActiveAndExplicitProjectReadFailuresRemainFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        await File.WriteAllTextAsync(Path.Combine(parent.TasksPath, "PM-0001.md"), "not task markdown");
        await File.WriteAllTextAsync(Path.Combine(child.WikiPath, "broken.md"), "not wiki markdown");
        var service = Service(parent, workspace);

        var active = await service.SearchTasksAsync(
            "needle", request: new LinkedProjectReadRequest(LinkedProjectReadScope.Family));
        var explicitProject = await service.ListWikiPagesAsync(
            new LinkedProjectReadRequest(LinkedProjectReadScope.Project, "royale"));

        Assert.False(active.Success);
        Assert.Equal("invalid_task_markdown", active.ErrorCode);
        Assert.False(explicitProject.Success);
        Assert.Equal("invalid_wiki_markdown", explicitProject.ErrorCode);
    }

    [Fact]
    public async Task FamilyFiltersSkipProjectsThatDoNotDefineLocalKeys()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(
            Path.Combine(workspace.Path, "games", "royale"),
            "prj_royale",
            "Royale",
            TestData.Config(name: "Royale", idPrefix: "ROY"));
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Parent task");
        AddTask(child, "ROY-0001", "Child task", track: "ROY");
        var service = Service(parent, workspace);

        var result = await service.ListTasksAsync(
            new LinkedProjectReadRequest(LinkedProjectReadScope.Family),
            new BoardQuery(Track: "PM"));

        Assert.True(result.Success);
        Assert.Equal("PM-0001", Assert.Single(result.Payload!.Items).Resource.Task.Id);
    }

    [Fact]
    public async Task ListLimitsAreGlobalAndReturnATruncationWarning()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active", "Active");
        for (var index = 1; index <= 3; index++)
        {
            AddTask(active, $"PM-{index:0000}", $"Task {index}");
            Assert.True(new WikiService(active).CreatePage($"page-{index}", $"Page {index}", "Body").Success);
        }
        var service = Service(active, workspace, maximumListResultCount: 2);

        var tasks = await service.ListTasksAsync(new LinkedProjectReadRequest());
        var pages = await service.ListWikiPagesAsync(new LinkedProjectReadRequest());

        Assert.Equal(2, tasks.Payload!.Items.Count);
        Assert.True(tasks.Payload.Truncated);
        Assert.Contains(tasks.Payload.Warnings, warning => warning.Code == "linked_project_results_truncated");
        Assert.Equal(2, pages.Payload!.Items.Count);
        Assert.True(pages.Payload.Truncated);
    }

    [Fact]
    public async Task CancellationStopsFamilyTraversal()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Parent task");
        AddTask(child, "PM-0002", "Child task");
        using var cancellation = new CancellationTokenSource();
        var inspector = new CancellingGitInspector(cancellation);
        var service = Service(parent, workspace, inspector);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ListTasksAsync(
            new LinkedProjectReadRequest(LinkedProjectReadScope.Family),
            cancellationToken: cancellation.Token));
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task LinkedTaskReadsClassifyCompletedWaitingMissingAndUnavailableDependencies()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(child, "GAME-0001", "Shared work", state: "done");
        AddTask(parent, "PM-0001", "Completed dependency",
            dependsOn: ["pm://project/prj_royale/task/GAME-0001"]);
        AddTask(parent, "PM-0002", "Missing dependency",
            dependsOn: ["pm://project/prj_royale/task/GAME-9999"]);
        AddTask(parent, "PM-0003", "Unavailable dependency",
            dependsOn: ["pm://project/prj_absent/task/GAME-0001"]);
        var service = Service(parent, workspace);

        var first = await service.ListTasksAsync(new LinkedProjectReadRequest());

        Assert.True(first.Success);
        var completed = first.Payload!.Items.Single(item => item.Resource.Task.Id == "PM-0001").Resource.Dependencies;
        var missing = first.Payload.Items.Single(item => item.Resource.Task.Id == "PM-0002").Resource.Dependencies;
        var unavailable = first.Payload.Items.Single(item => item.Resource.Task.Id == "PM-0003").Resource.Dependencies;
        Assert.True(completed.Ready);
        Assert.Equal(["pm://project/prj_royale/task/GAME-0001"], completed.Completed);
        Assert.Equal(["pm://project/prj_royale/task/GAME-9999"], missing.Missing);
        Assert.Equal(["pm://project/prj_absent/task/GAME-0001"], unavailable.Unavailable);
        Assert.Contains(first.Payload.Warnings, warning => warning.Code == "dependency_graph_incomplete");
        var search = await service.SearchTasksAsync("Completed dependency");
        Assert.True(search.Payload!.Items.Single().Resource.Dependencies.Ready);
        Assert.Equal(["pm://project/prj_royale/task/GAME-0001"],
            search.Payload.Items.Single().Resource.Dependencies.Completed);

        Assert.True(child.TryGetById("GAME-0001", out var childTask));
        child.UpdateTaskState(childTask, "todo");
        var second = await service.GetTaskAsync("PM-0001");

        Assert.False(second.Payload!.Items.Single().Resource.Dependencies.Ready);
        Assert.Equal(["pm://project/prj_royale/task/GAME-0001"],
            second.Payload.Items.Single().Resource.Dependencies.WaitingOn);
    }

    [Fact]
    public async Task CurrentChildReadsResolveCanonicalDependenciesThroughReadableUntrustedParent()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "starfall"), "prj_starfall", "Starfall");
        LinkParentAndChildren(parent, [(child, "starfall")]);
        AddTask(parent, "PM-0001", "Coordinator contract", state: "done");
        AddTask(child, "GAME-0001", "Completed parent dependency",
            dependsOn: ["pm://project/prj_games/task/PM-0001"]);
        AddTask(child, "GAME-0002", "Missing parent task",
            dependsOn: ["pm://project/prj_games/task/PM-9999"]);
        AddTask(child, "GAME-0003", "Unavailable project",
            dependsOn: ["pm://project/prj_absent/task/PM-0001"]);
        var family = Family(child, workspace);
        var service = new LinkedProjectReadService(
            child, family, new UnusedNextIdService(), new FixedGitInspector(null, null),
            new TaskServiceFactory(TimeProvider.System));

        var resolvedFamily = await family.ResolveAsync();
        var parentMember = resolvedFamily.Payload!.Members.Single(member => member.ProjectId == "prj_games");
        var localBoard = new BoardService(child).GetBoard(new BoardQuery()).Payload!;
        var board = await service.EnrichCurrentBoardAsync(localBoard);
        var detail = await service.EnrichCurrentTaskAsync(new BoardService(child).GetTask("GAME-0001").Payload!);
        var next = await service.GetNextTaskAsync(
            new LinkedProjectReadRequest(), new NextTaskQuery(ReadyOnly: true));

        Assert.True(parentMember.Readable);
        Assert.False(parentMember.WriteTrusted);
        var completed = board.Payload!.Tasks.Single(task => task.Task.Id == "GAME-0001").Dependencies;
        var missing = board.Payload.Tasks.Single(task => task.Task.Id == "GAME-0002").Dependencies;
        var unavailable = board.Payload.Tasks.Single(task => task.Task.Id == "GAME-0003").Dependencies;
        Assert.True(completed.Ready);
        Assert.Equal(["pm://project/prj_games/task/PM-0001"], completed.Completed);
        Assert.Equal(["pm://project/prj_games/task/PM-9999"], missing.Missing);
        Assert.Equal(["pm://project/prj_absent/task/PM-0001"], unavailable.Unavailable);
        Assert.True(detail.Payload!.Dependencies.Ready);
        Assert.Equal("GAME-0001", next.Payload!.Task!.Task.Id);

        Assert.True(parent.TryGetById("PM-0001", out var parentTask));
        parent.UpdateTaskState(parentTask, "todo");
        var waiting = await service.EnrichCurrentTaskAsync(new BoardService(child).GetTask("GAME-0001").Payload!);
        Assert.False(waiting.Payload!.Dependencies.Ready);
        Assert.Equal(["pm://project/prj_games/task/PM-0001"], waiting.Payload.Dependencies.WaitingOn);
    }

    [Fact]
    public async Task CrossProjectCyclesWarnWithoutInvalidatingReads()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Parent task",
            dependsOn: ["pm://project/prj_royale/task/GAME-0001"]);
        AddTask(child, "GAME-0001", "Child task",
            dependsOn: ["pm://project/prj_games/task/PM-0001"]);

        var result = await Service(parent, workspace).ListTasksAsync(new LinkedProjectReadRequest());

        Assert.True(result.Success);
        Assert.False(result.Payload!.Items.Single().Resource.Dependencies.Ready);
        Assert.Contains(result.Payload.Warnings, warning => warning.Code == "cross_project_dependency_cycle");
    }

    [Fact]
    public async Task RecommendationsKeepActiveCandidatesByDefaultAndRankFamilyCandidatesDeterministically()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale");
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Active low", priority: "low");
        AddTask(child, "GAME-0001", "Child urgent", priority: "urgent");
        var service = Service(parent, workspace);

        var current = await service.GetNextTaskAsync(
            new LinkedProjectReadRequest(), new NextTaskQuery(ReadyOnly: true));
        var family = await service.GetNextTaskAsync(
            new LinkedProjectReadRequest(LinkedProjectReadScope.Family),
            new NextTaskQuery(ReadyOnly: true));
        var selected = await service.GetNextTaskAsync(
            new LinkedProjectReadRequest(LinkedProjectReadScope.Project, "royale"),
            new NextTaskQuery(ReadyOnly: true));

        parent.WriteTask(TestData.Task("PM-0001", "Active urgent", priority: "urgent"));
        var tiedFamily = await service.GetNextTaskAsync(
            new LinkedProjectReadRequest(LinkedProjectReadScope.Family),
            new NextTaskQuery(ReadyOnly: true));

        Assert.Equal("PM-0001", current.Payload!.Task!.Task.Id);
        Assert.Equal("prj_games", current.Payload.Owner!.ProjectId);
        Assert.Equal("GAME-0001", family.Payload!.Task!.Task.Id);
        Assert.Equal("prj_royale", family.Payload.Owner!.ProjectId);
        Assert.Equal("GAME-0001", selected.Payload!.Task!.Task.Id);
        Assert.Equal("PM-0001", tiedFamily.Payload!.Task!.Task.Id);
    }

    [Fact]
    public async Task FamilyRecommendationFiltersMatchOnlyProjectsDefiningTheKey()
    {
        using var workspace = new TempWorkingDirectory();
        var parent = await CreateProject(Path.Combine(workspace.Path, "games"), "prj_games", "Games");
        var child = await CreateProject(
            Path.Combine(workspace.Path, "games", "royale"), "prj_royale", "Royale",
            TestData.Config(name: "Royale", idPrefix: "GAME",
                tracks: new Dictionary<string, string> { ["GAME"] = "Game", ["BUILD"] = "Build" }));
        LinkParentAndChildren(parent, [(child, "royale")]);
        AddTask(parent, "PM-0001", "Parent", track: "PM");
        AddTask(child, "BUILD-0001", "Build", track: "BUILD");
        var service = Service(parent, workspace);
        var request = new LinkedProjectReadRequest(LinkedProjectReadScope.Family);

        var filtered = await service.GetNextTaskAsync(request, new NextTaskQuery(Track: "BUILD"));
        var invalid = await service.GetNextTaskAsync(request, new NextTaskQuery(Track: "NOPE"));

        Assert.True(filtered.Success);
        Assert.Equal("BUILD-0001", filtered.Payload!.Task!.Task.Id);
        Assert.False(invalid.Success);
        Assert.Equal("invalid_track", invalid.ErrorCode);
    }

    [Fact]
    public async Task GitInspectorReportsRevisionAndDirtyStateWithoutRequiringGit()
    {
        using var workspace = new TempWorkingDirectory();
        var repository = Path.Combine(workspace.Path, "repository");
        Directory.CreateDirectory(repository);
        Git(repository, "init");
        Git(repository, "config", "user.email", "pm-tests@example.test");
        Git(repository, "config", "user.name", "PM Tests");
        await File.WriteAllTextAsync(Path.Combine(repository, "tracked.txt"), "tracked");
        Git(repository, "add", "tracked.txt");
        Git(repository, "commit", "-m", "Initial");
        var expectedRevision = Git(repository, "rev-parse", "HEAD").Trim();
        var inspector = new LinkedProjectGitInspector();

        var clean = await inspector.InspectAsync(repository);
        await File.WriteAllTextAsync(Path.Combine(repository, "dirty.txt"), "dirty");
        var dirty = await inspector.InspectAsync(repository);
        var nonGit = await inspector.InspectAsync(workspace.Path);

        Assert.Equal(expectedRevision, clean.Revision);
        Assert.False(clean.Dirty);
        Assert.True(dirty.Dirty);
        Assert.Null(nonGit.Revision);
        Assert.Null(nonGit.Dirty);
    }

    [Fact]
    public async Task McpHostRegistersTheReadFederationService()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), "prj_active\n");

        using var host = McpServerHost.CreateBuilder([]).Build();

        Assert.NotNull(host.Services.GetRequiredService<LinkedProjectReadService>());
    }

    private static LinkedProjectReadService Service(
        ProjectRoot active,
        TempWorkingDirectory workspace,
        ILinkedProjectGitInspector? gitInspector = null,
        int maximumListResultCount = LinkedProjectReadService.MaximumListResultCount) =>
        new(active,
            Family(active, workspace),
            new UnusedNextIdService(),
            gitInspector ?? new FixedGitInspector(null, null),
            new TaskServiceFactory(TimeProvider.System),
            maximumListResultCount);

    private static LinkedProjectFamilyService Family(ProjectRoot active, TempWorkingDirectory workspace) =>
        new(active,
            new LinkedProjectService(active),
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
                {
                    RootPath = Path.Combine(workspace.Path, "registry"),
                }),
                new NullSubmoduleInspector()));

    private static async Task<ProjectRoot> CreateProject(
        string repositoryPath,
        string projectId,
        string name,
        ProjectConfig? config = null)
    {
        Directory.CreateDirectory(repositoryPath);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repositoryPath;
        try
        {
            var root = new ProjectRoot();
            var projectConfig = config ?? TestData.Config(name: name);
            projectConfig.Name = name;
            await root.CreateProject(projectConfig);
            await File.WriteAllTextAsync(Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");
            return root;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private static void LinkParentAndChildren(
        ProjectRoot parent,
        IReadOnlyList<(ProjectRoot Project, string Alias)> children)
    {
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = children.Select(child => Declaration(
                ReadProjectId(child.Project),
                child.Alias,
                Path.GetRelativePath(parent.RepositoryPath, child.Project.RepositoryPath))).ToList(),
        });
        foreach (var child in children)
            child.Project.WriteLinkedProjectsManifest(new LinkedProjectManifest
            {
                Parent = Declaration(
                    ReadProjectId(parent),
                    "games",
                    Path.GetRelativePath(child.Project.RepositoryPath, parent.RepositoryPath)),
            });
    }

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

    private static string ReadProjectId(ProjectRoot root)
    {
        Assert.True(root.TryReadProjectId(out var projectId));
        return projectId;
    }

    private static void AddTask(
        ProjectRoot root,
        string id,
        string title,
        string description = "",
        string? track = "PM",
        string state = "todo",
        string? priority = null,
        IReadOnlyList<string>? dependsOn = null)
    {
        var task = TestData.Task(id, title, description, track, priority: priority, dependsOn: dependsOn);
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private static void AssertOwner(
        LinkedProjectResourceOwner owner,
        string projectId,
        string name,
        string alias,
        string? revision,
        bool? dirty)
    {
        Assert.Equal(projectId, owner.ProjectId);
        Assert.Equal(name, owner.ProjectName);
        Assert.Equal(alias, owner.Alias);
        Assert.Equal(revision, owner.Revision);
        Assert.Equal(dirty, owner.Dirty);
    }

    private static string Git(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private sealed class FixedGitInspector(string? revision, bool? dirty) : ILinkedProjectGitInspector
    {
        public Task<LinkedProjectGitMetadata> InspectAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LinkedProjectGitMetadata(revision, dirty));
    }

    private sealed class CancellingGitInspector(CancellationTokenSource cancellation) : ILinkedProjectGitInspector
    {
        public int CallCount { get; private set; }

        public Task<LinkedProjectGitMetadata> InspectAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellation.Cancel();
            return Task.FromResult(new LinkedProjectGitMetadata(null, null));
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
