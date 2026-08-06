using PM.Api;
using PM.Application;
using PM.Project;

namespace PM.Tests;

public class ResourceRevisionTests
{
    [Fact]
    public async Task TaskRevisionUsesExactMarkdownAndResolvedState()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "First");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var revisions = CreateRevisions(root);

        var initial = Revision(revisions.GetTaskRevision(task.Id));
        Assert.Equal(initial, Revision(revisions.GetTaskRevision(task.Id)));
        Assert.Matches("^[0-9a-f]{64}$", initial);

        root.WriteTaskFile(task.Id, File.ReadAllText(root.GetTaskFilePath(task.Id)) + "\n");
        var contentChanged = Revision(revisions.GetTaskRevision(task.Id));
        Assert.NotEqual(initial, contentChanged);

        root.UpdateTaskState(task, "review");
        Assert.NotEqual(contentChanged, Revision(revisions.GetTaskRevision(task.Id)));
    }

    [Fact]
    public async Task WikiAndConfigurationRevisionsUseExactPersistedContent()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.WriteWikiFile("guide", "---\ntitle: Guide\ncreatedAt: 2026-01-01\nmodifiedAt: 2026-01-01\n---\n\nBody");
        var revisions = CreateRevisions(root);

        var wiki = Revision(revisions.GetWikiPageRevision("guide"));
        var config = Revision(revisions.GetProjectConfigRevision());
        Assert.Equal(wiki, Revision(revisions.GetWikiPageRevision("guide.md")));
        Assert.Equal(config, Revision(revisions.GetProjectConfigRevision()));

        root.WriteWikiFile("guide", File.ReadAllText(Path.Combine(root.WikiPath, "guide.md")) + "\n");
        File.AppendAllText(root.ConfigPath, "\n");
        Assert.NotEqual(wiki, Revision(revisions.GetWikiPageRevision("guide")));
        Assert.NotEqual(config, Revision(revisions.GetProjectConfigRevision()));
    }

    [Fact]
    public async Task BoardRevisionTracksFiltersVisibleTasksAndOrderingButNotWikiOrFilteredTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "PM", ["BUILD"] = "Build" }));
        var first = TestData.Task("PM-0001", "First", new string('a', 120));
        var second = TestData.Task("PM-0002", "Second");
        var filtered = TestData.Task("BUILD-0001", "Filtered", track: "BUILD");
        foreach (var task in new[] { first, second, filtered }) root.WriteTask(task);
        root.UpdateTaskState(first, "todo");
        root.UpdateTaskState(second, "todo");
        root.UpdateTaskState(filtered, "todo");
        var revisions = CreateRevisions(root);
        var query = new BoardQuery(Track: "PM");

        var initial = Revision(revisions.GetBoardRevision(query));
        Assert.Equal(initial, Revision(revisions.GetBoardRevision(query)));
        Assert.NotEqual(initial, Revision(revisions.GetBoardRevision(new BoardQuery())));

        root.WriteWikiFile("notes", "wiki change");
        root.WriteTask(filtered with { Title = "Still filtered" });
        root.WriteTask(first with { Description = first.Description + " hidden beyond preview" });
        Assert.Equal(initial, Revision(revisions.GetBoardRevision(query)));

        root.SetTaskOrder(new TaskOrderScope("PM", "todo", null), [second.Id, first.Id]);
        var reordered = Revision(revisions.GetBoardRevision(query));
        Assert.NotEqual(initial, reordered);

        root.WriteTask(first with { Title = "Visible change" });
        Assert.NotEqual(reordered, Revision(revisions.GetBoardRevision(query)));
    }

    [Fact]
    public async Task BoardRevisionTracksConfigurationAndDependencyReadiness()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "PM", ["BUILD"] = "Build" });
        var root = await workspace.CreateProject(config);
        var dependency = TestData.Task("BUILD-0001", "Dependency", track: "BUILD");
        var visible = TestData.Task("PM-0001", "Visible", dependsOn: [dependency.Id]);
        root.WriteTask(dependency);
        root.WriteTask(visible);
        root.UpdateTaskState(dependency, "todo");
        root.UpdateTaskState(visible, "todo");
        var revisions = CreateRevisions(root);
        var query = new BoardQuery(Track: "PM");

        var waiting = Revision(revisions.GetBoardRevision(query));
        root.UpdateTaskState(dependency, "done");
        var ready = Revision(revisions.GetBoardRevision(query));
        Assert.NotEqual(waiting, ready);

        config.Name = "Renamed";
        config.WriteConfig(root);
        Assert.True(root.TryReloadConfig());
        Assert.NotEqual(ready, Revision(revisions.GetBoardRevision(query)));
    }

    [Fact]
    public async Task BoardAndTaskRevisionsTrackActivationAndDeliveryState()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new Dictionary<string, string> { ["beta"] = "Beta" },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["entry"] = new()
                {
                    Title = "Entry",
                },
                ["progress"] = new()
                {
                    Title = "Progress",
                    Requirements =
                    [
                        new ActivationRequirement
                        {
                            Kind = ActivationRequirementKind.Task,
                            Source = "PM-0002",
                        },
                    ],
                },
            });
        config.Milestones["beta"].RequiredActivationTriggers = ["entry"];
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Beta work", milestone: "beta");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var requirement = TestData.Task("PM-0002", "Requirement progress");
        root.WriteTask(requirement);
        root.UpdateTaskState(requirement, "todo");
        var revisions = CreateRevisions(root);

        var inactiveBoard = Revision(revisions.GetBoardRevision(new BoardQuery()));
        var inactiveTask = Revision(revisions.GetTaskRevision(task.Id));
        root.UpdateTaskState(requirement, "done");
        var requirementSatisfiedBoard = Revision(revisions.GetBoardRevision(new BoardQuery()));

        root.Config!.ActivationTriggers["entry"].Activation = new ActivationRecord
        {
            At = new DateTimeOffset(2026, 8, 6, 8, 15, 0, TimeSpan.Zero),
            Mode = ActivationMode.Manual,
        };
        root.Config.WriteConfig(root);
        Assert.True(root.TryReloadConfig());
        var activeBoard = Revision(revisions.GetBoardRevision(new BoardQuery()));
        var activeTask = Revision(revisions.GetTaskRevision(task.Id));

        root.Config!.Milestones["beta"].Delivery = new MilestoneDelivery
        {
            At = new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero),
            Mode = MilestoneDeliveryMode.Exceptional,
            Reason = "Accepted with one open task.",
            AcceptedTaskIds = [task.Id],
        };
        root.Config.WriteConfig(root);
        Assert.True(root.TryReloadConfig());
        var deliveredBoard = Revision(revisions.GetBoardRevision(new BoardQuery()));
        var deliveredTask = Revision(revisions.GetTaskRevision(task.Id));

        Assert.NotEqual(inactiveBoard, requirementSatisfiedBoard);
        Assert.NotEqual(requirementSatisfiedBoard, activeBoard);
        Assert.NotEqual(inactiveTask, activeTask);
        Assert.NotEqual(activeBoard, deliveredBoard);
        Assert.NotEqual(activeTask, deliveredTask);
    }

    private static ResourceRevisionService CreateRevisions(ProjectRoot root) =>
        new(root, TestBoardServices.Create(root));

    private static string Revision(AppResult<string> result)
    {
        Assert.True(result.Success, result.Message);
        return result.Payload!;
    }
}
