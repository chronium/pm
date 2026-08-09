using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class OverviewServiceTests
{
    [Fact]
    public async Task DisabledDocumentDoesNotExposeOrReviseForDormantConfiguration()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var idPath = Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile);
        if (File.Exists(idPath)) File.Delete(idPath);
        root.Config!.Site = new OverviewSiteDefinition
        {
            Enabled = false,
            Title = "Dormant title",
            Home = new OverviewHomeDefinition
            {
                Sections = [new OverviewSectionDefinition { Type = OverviewSectionKinds.Tasks }],
            },
        };
        var service = Service(root, workspace.Path);

        var first = await service.ResolveAsync();
        root.Config.Site.Title = "Changed dormant title";
        var second = await service.ResolveAsync();

        Assert.True(first.Success);
        Assert.Equal(OverviewDocumentStatus.Disabled, first.Payload!.Status);
        Assert.Null(first.Payload.ProjectId);
        Assert.Equal(root.Config.Name, first.Payload.DocumentTitle);
        Assert.Null(first.Payload.Composition);
        Assert.Empty(first.Payload.Issues);
        Assert.Equal(first.Payload.Revision, second.Payload!.Revision);
    }

    [Fact]
    public async Task EnabledInvalidDocumentIsAtomicAndUsesSemanticIssuePaths()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.Config!.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Title = " ",
            Home = new OverviewHomeDefinition
            {
                Sections = [new OverviewSectionDefinition { Type = OverviewSectionKinds.Tasks }],
            },
        };

        var service = Service(root, workspace.Path);
        var result = await service.ResolveAsync();

        Assert.True(result.Success);
        var document = result.Payload!;
        Assert.Equal(OverviewDocumentStatus.Invalid, document.Status);
        Assert.Equal(root.Config.Name, document.DocumentTitle);
        Assert.Null(document.Composition);
        Assert.Contains(document.Issues, issue =>
            issue.Code == "invalid_overview_site_title" && issue.Path == "site.title");
        Assert.Contains(document.Issues, issue =>
            issue.Code == "missing_overview_hero" && issue.Path == "site.home.sections[0]");
        Assert.Matches("^[0-9a-f]{64}$", document.Revision);
    }

    [Fact]
    public async Task ImplicitSingleCompositionUsesDefaultSectionsAndMilestonePriorityOrder()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            name: "Project Atlas",
            milestones: new Dictionary<string, string>
            {
                ["first-urgent"] = "First urgent",
                ["second-urgent"] = "Second urgent",
                ["high"] = "High",
            },
            milestonePriorities: new Dictionary<string, string>
            {
                ["first-urgent"] = PriorityLevel.Urgent,
                ["second-urgent"] = PriorityLevel.Urgent,
                ["high"] = PriorityLevel.High,
            });
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Title = "Atlas",
            Description = "Project introduction.",
        };
        var root = await workspace.CreateProject(config);
        Assert.True(new WikiService(root).CreatePage("guide", "Guide", "Guide body.").Success);

        var service = Service(root, workspace.Path);
        var result = await service.ResolveAsync();

        Assert.True(result.Success);
        var document = result.Payload!;
        Assert.Equal(OverviewDocumentStatus.Ready, document.Status);
        Assert.Equal("Atlas", document.DocumentTitle);
        var single = Assert.IsType<SingleOverviewComposition>(document.Composition);
        Assert.Equal(["hero", "milestone", "tasks", "wiki"], single.Sections.Select(section => section.Type));
        var hero = Assert.IsType<HeroOverviewSection>(single.Sections[0]);
        Assert.Equal("Project introduction.", hero.Description);
        var milestone = Assert.IsType<MilestoneOverviewSection>(single.Sections[1]);
        Assert.Equal("Current milestone", milestone.Title);
        Assert.Equal("first-urgent", milestone.Milestone!.Key);
        Assert.Empty(Assert.IsType<TasksOverviewSection>(single.Sections[2]).Tasks);
        Assert.Equal("guide", Assert.Single(Assert.IsType<WikiOverviewSection>(single.Sections[3]).Pages).Path);
    }

    [Fact]
    public async Task SplitCompositionPreservesRegionsRepeatedContentMarkdownAndCopyright()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Home = new OverviewHomeDefinition
            {
                Layout = OverviewLayouts.Split,
                Primary =
                [
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Markdown, source: "wiki:introduction"),
                    Section(OverviewSectionKinds.Markdown, title: "Again", source: "wiki:introduction"),
                ],
                Secondary = [Section(OverviewSectionKinds.Tasks)],
                After =
                [
                    Section(OverviewSectionKinds.Wiki, pages: ["guide"]),
                    Section(OverviewSectionKinds.Copyright, notice: "Copyright 2026 Example."),
                ],
            },
        };
        var root = await workspace.CreateProject(config);
        var wiki = new WikiService(root);
        Assert.True(wiki.CreatePage("introduction", "Introduction", "Welcome to PM.").Success);
        Assert.True(wiki.CreatePage("guide", "Guide", "Read the guide.").Success);

        var service = Service(root, workspace.Path);
        var result = await service.ResolveAsync();

        var split = Assert.IsType<SplitOverviewComposition>(result.Payload!.Composition);
        Assert.Equal(["hero", "markdown", "markdown"], split.Primary.Select(section => section.Type));
        Assert.Equal(["tasks"], split.Secondary.Select(section => section.Type));
        Assert.Equal(["wiki", "copyright"], split.After.Select(section => section.Type));
        var introduction = Assert.IsType<MarkdownOverviewSection>(split.Primary[1]);
        Assert.Equal("Introduction", introduction.Title);
        Assert.Equal("introduction", introduction.SourcePath);
        Assert.Equal("Welcome to PM.", introduction.Body);
        Assert.Equal("Again", Assert.IsType<MarkdownOverviewSection>(split.Primary[2]).Title);
        Assert.Equal("guide", Assert.Single(Assert.IsType<WikiOverviewSection>(split.After[0]).Pages).Path);
        Assert.Equal("Copyright 2026 Example.",
            Assert.IsType<CopyrightOverviewSection>(split.After[1]).Notice);
        Assert.True(wiki.UpdatePageBody("introduction", "Updated introduction.").Success);
        var changed = await service.ResolveAsync();
        Assert.NotEqual(result.Payload.Revision, changed.Payload!.Revision);
    }

    [Fact]
    public async Task ExplicitMilestoneMayBeReadyAndImplicitWikiSelectionIsBoundedAndOrdered()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new Dictionary<string, string> { ["ready"] = "Ready milestone" });
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Milestone, milestone: "ready"),
                    Section(OverviewSectionKinds.Wiki),
                ],
            },
        };
        var root = await workspace.CreateProject(config);
        var deliveredWork = TestData.Task("PM-0001", "Completed work", milestone: "ready");
        root.WriteTask(deliveredWork);
        root.UpdateTaskState(deliveredWork, "done");
        var wiki = new WikiService(root);
        foreach (var path in new[] { "zeta", "beta", "eta", "alpha", "theta", "delta", "gamma" })
            Assert.True(wiki.CreatePage(path, path, string.Empty).Success);
        Assert.True(wiki.CreatePage("nested/page", "Nested", string.Empty).Success);

        var result = await Service(root, workspace.Path).ResolveAsync();

        var sections = Assert.IsType<SingleOverviewComposition>(result.Payload!.Composition).Sections;
        var milestone = Assert.IsType<MilestoneOverviewSection>(sections[1]).Milestone!;
        Assert.Equal("ready", milestone.Key);
        Assert.Equal(MilestoneLifecycle.ReadyToDeliver, milestone.Lifecycle);
        var pages = Assert.IsType<WikiOverviewSection>(sections[2]).Pages;
        Assert.Equal(["alpha", "beta", "delta", "eta", "gamma", "theta"],
            pages.Select(page => page.Path));
    }

    [Fact]
    public async Task TasksUseSharedPredicatesThenBoardOrderAndLimit()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Tasks, filter: "needle state:todo", limit: 2),
                ],
            },
        };
        var root = await workspace.CreateProject(config);
        AddTask(root, "PM-0001", "First needle", "todo");
        AddTask(root, "PM-0002", "Second needle", "todo");
        AddTask(root, "PM-0003", "Third needle", "todo");
        AddTask(root, "PM-0004", "Done needle", "done");
        root.WriteTaskOrder(new TaskOrderFile
        {
            Orders =
            [
                new TaskOrderEntry
                {
                    Track = "PM",
                    State = "todo",
                    TaskIds = ["PM-0003", "PM-0001", "PM-0002"],
                },
            ],
        });

        var result = await Service(root, workspace.Path).ResolveAsync();

        var tasks = Assert.IsType<TasksOverviewSection>(
            Assert.IsType<SingleOverviewComposition>(result.Payload!.Composition).Sections[1]);
        Assert.Equal(["PM-0003", "PM-0001"], tasks.Tasks.Select(task => task.Id));
        Assert.All(tasks.Tasks, task => Assert.Equal("todo", task.State));
    }

    [Fact]
    public async Task RevisionTracksOnlyEffectiveResolvedInputs()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Tasks, filter: "state:todo", limit: 1),
                ],
            },
        };
        var root = await workspace.CreateProject(config);
        AddTask(root, "PM-0001", "Selected", "todo");
        AddTask(root, "PM-0002", "Unselected done", "done");
        Assert.True(new WikiService(root).CreatePage("unselected/note", "Note", "One").Success);
        var service = Service(root, workspace.Path);

        var initial = (await service.ResolveAsync()).Payload!;
        var stable = (await service.ResolveAsync()).Payload!;
        var done = TestData.Task("PM-0002", "Changed but still unselected");
        root.WriteTask(done);
        root.UpdateTaskState(done, "done");
        Assert.True(new WikiService(root).UpdatePageBody("unselected/note", "Two").Success);
        var unrelated = (await service.ResolveAsync()).Payload!;
        var selected = TestData.Task("PM-0001", "Selected title changed");
        root.WriteTask(selected);
        root.UpdateTaskState(selected, "todo");
        var changed = (await service.ResolveAsync()).Payload!;

        Assert.Equal(initial.Revision, stable.Revision);
        Assert.Equal(initial.Revision, unrelated.Revision);
        Assert.NotEqual(initial.Revision, changed.Revision);
    }

    [Fact]
    public async Task LinkedResolutionUsesOwningProjectConfigurationAndFamilyDependencies()
    {
        using var workspace = new TempWorkingDirectory();
        var parentConfig = SiteConfig("Games", "Parent Overview", "PM");
        var childConfig = SiteConfig("Royale", "Child Overview", "GAME");
        var parent = await CreateProject(
            Path.Combine(workspace.Path, "games"), "prj_games", parentConfig);
        var child = await CreateProject(
            Path.Combine(workspace.Path, "games", "royale"), "prj_royale", childConfig);
        Link(parent, child, "royale");
        AddTask(parent, "PM-0001", "Parent contract", "done");
        var childTask = TestData.Task(
            "GAME-0001",
            "Child work",
            track: "GAME",
            dependsOn: ["pm://project/prj_games/task/PM-0001"]);
        child.WriteTask(childTask);
        child.UpdateTaskState(childTask, "todo");
        var service = Service(parent, workspace.Path);

        var current = await service.ResolveAsync();
        var linked = await service.ResolveAsync("royale");

        Assert.Equal("Parent Overview", current.Payload!.DocumentTitle);
        Assert.Equal("prj_royale", linked.Payload!.ProjectId);
        Assert.Equal("Royale", linked.Payload.ProjectName);
        Assert.Equal("Child Overview", linked.Payload.DocumentTitle);
        var task = Assert.Single(Assert.IsType<TasksOverviewSection>(
            Assert.IsType<SingleOverviewComposition>(linked.Payload.Composition).Sections[1]).Tasks);
        Assert.True(task.Dependencies.Ready);
        Assert.Equal(["pm://project/prj_games/task/PM-0001"], task.Dependencies.Completed);
    }

    private static ProjectConfig SiteConfig(string name, string title, string prefix)
    {
        var config = TestData.Config(
            name: name,
            idPrefix: prefix,
            tracks: new Dictionary<string, string> { [prefix] = prefix });
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Title = title,
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Tasks),
                ],
            },
        };
        return config;
    }

    private static OverviewSectionDefinition Section(
        string type,
        string? title = null,
        string? filter = null,
        int? limit = null,
        IReadOnlyList<string>? pages = null,
        string? milestone = null,
        string? source = null,
        string? notice = null) =>
        new()
        {
            Type = type,
            Title = title,
            Filter = filter,
            Limit = limit,
            Pages = pages?.ToList(),
            Milestone = milestone,
            Source = source,
            Notice = notice,
        };

    private static void AddTask(ProjectRoot root, string id, string title, string state)
    {
        var task = TestData.Task(id, title);
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private static OverviewService Service(ProjectRoot active, string workspacePath)
    {
        var family = new LinkedProjectFamilyService(
            active,
            new LinkedProjectService(active),
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
                {
                    RootPath = Path.Combine(workspacePath, "registry"),
                }),
                new NullSubmoduleInspector()));
        var reads = new LinkedProjectReadService(
            active,
            family,
            new UnusedNextIdService(),
            new FixedGitInspector(),
            new TaskServiceFactory(TimeProvider.System));
        return new OverviewService(reads);
    }

    private static async Task<ProjectRoot> CreateProject(
        string repositoryPath,
        string projectId,
        ProjectConfig config)
    {
        Directory.CreateDirectory(repositoryPath);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repositoryPath;
        try
        {
            var root = new ProjectRoot();
            await root.CreateProject(config);
            await File.WriteAllTextAsync(
                Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");
            return root;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private static void Link(ProjectRoot parent, ProjectRoot child, string alias)
    {
        parent.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration(
                    "prj_royale",
                    alias,
                    Path.GetRelativePath(parent.RepositoryPath, child.RepositoryPath)),
            ],
        });
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration(
                "prj_games",
                "games",
                Path.GetRelativePath(child.RepositoryPath, parent.RepositoryPath)),
        });
    }

    private static LinkedProjectDeclaration Declaration(string projectId, string alias, string pathHint) =>
        new()
        {
            ProjectId = projectId,
            Alias = alias,
            RepositoryUrl = $"https://example.test/{projectId}.git",
            PathHint = pathHint,
        };

    private sealed class NullSubmoduleInspector : ILinkedProjectSubmoduleInspector
    {
        public Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
            string repositoryPath,
            string pathHint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<LinkedProjectRepairAction?>.Ok(null));
    }

    private sealed class FixedGitInspector : ILinkedProjectGitInspector
    {
        public Task<LinkedProjectGitMetadata> InspectAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LinkedProjectGitMetadata(null, null));
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
