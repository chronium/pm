using Microsoft.Extensions.DependencyInjection;
using PM.Application;
using PM.Mcp;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public class McpToolTests
{
    [Fact]
    public void MissingProjectReturnsStructuredFailure()
    {
        using var workspace = new TempWorkingDirectory();
        var tools = CreateTools(new ProjectRoot());

        var result = tools.GetProject();

        Assert.False(result.Success);
        Assert.Equal("missing_project", result.ErrorCode);
        Assert.Equal("Project not found. Run pm init first.", result.Message);
    }

    [Fact]
    public async Task GetProjectReturnsConfigData()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            name: "MCP Test",
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var tools = CreateTools(projectRoot);

        var result = tools.GetProject();

        Assert.True(result.Success);
        Assert.Equal("MCP Test", result.Data!.Name);
        Assert.Equal(projectRoot.RootPath, result.Data.RootPath);
        Assert.Contains(result.Data.Tracks, track => track.Key == "BUILD" && track.Name == "Build");
        Assert.Contains(result.Data.Milestones, milestone => milestone.Key == "m1");
        Assert.Contains(result.Data.States, state => state.Key == "todo" && state.Name == "Queued");
    }

    [Fact]
    public async Task CreateProjectInitializesAndReturnsStructuredPayload()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var tools = CreateTools(projectRoot);

        var result = await tools.CreateProject(
            "MCP Project",
            idWidth: 3,
            idPrefix: "BUG",
            nextIdServiceUrl: "http://ids.local",
            states: new Dictionary<string, string?> { ["todo"] = "Todo" },
            tracks: new Dictionary<string, string?> { ["BUG"] = "Bugs" },
            milestones: new Dictionary<string, string?> { ["v1"] = "Version 1" });

        Assert.True(result.Success);
        Assert.Equal("MCP Project", result.Data!.Name);
        Assert.Equal(projectRoot.RootPath, result.Data.RootPath);
        Assert.Contains(result.Data.Tracks, track => track.Key == "BUG" && track.Name == "Bugs");
        Assert.Contains(result.Data.Milestones, milestone => milestone.Key == "v1");
        Assert.Contains(result.Data.States, state => state.Key == "todo" && state.Name == "Todo");
    }

    [Fact]
    public async Task CreateProjectReturnsValidationFailuresForBlankOptions()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var nextIds = new RecordingNextIdService();
        var tools = CreateTools(projectRoot, nextIds);

        var result = await tools.CreateProject(
            "MCP Project",
            milestones: new Dictionary<string, string?> { ["m1"] = null });

        Assert.False(result.Success);
        Assert.Equal("invalid_milestones", result.ErrorCode);
        Assert.Equal(0, nextIds.HealthyCalls);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, GlobalConfig.PmDirName)));
    }

    [Fact]
    public async Task ListTasksFiltersByTrackMilestoneAndState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var match = TestData.Task("BUILD-0001", "Matching task", "- Preview line", "BUILD", "m1");
        var wrongTrack = TestData.Task("PM-0001", "Wrong track", track: "PM", milestone: "m1");
        var wrongMilestone = TestData.Task("BUILD-0002", "Wrong milestone", track: "BUILD", milestone: "m2");
        projectRoot.WriteTask(match);
        projectRoot.WriteTask(wrongTrack);
        projectRoot.WriteTask(wrongMilestone);
        projectRoot.UpdateTaskState(match, "review");
        projectRoot.UpdateTaskState(wrongTrack, "review");
        projectRoot.UpdateTaskState(wrongMilestone, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.ListTasks("BUILD", "m1", "review");

        Assert.True(result.Success);
        var task = Assert.Single(result.Data!.Tasks);
        Assert.Equal("BUILD-0001", task.Id);
        Assert.Equal("Preview line", task.DescriptionPreview);
        Assert.Equal("review", task.State);
    }

    [Fact]
    public async Task GetTaskReturnsMarkdownAndState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing", "Body text");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetTask("PM-0001");

        Assert.True(result.Success);
        Assert.Equal("PM-0001", result.Data!.Id);
        Assert.Equal("todo", result.Data.State);
        Assert.Equal("Body text", result.Data.Description);
        Assert.Contains("title: Existing", result.Data.Markdown);
        Assert.Equal(projectRoot.GetTaskFilePath("PM-0001"), result.Data.FilePath);
    }

    [Fact]
    public async Task CreateTaskCreatesTrackScopedTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var nextIds = new RecordingNextIdService();
        var tools = CreateTools(projectRoot, nextIds);

        var result = await tools.CreateTask("Build thing", "BUILD", "m1", "Details");

        Assert.True(result.Success);
        Assert.Equal("BUILD-0001", result.Data!.Id);
        Assert.Equal(["BUILD"], nextIds.GetNextIdTracks);
        Assert.True(File.Exists(Path.Combine(projectRoot.TasksPath, "BUILD-0001.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "BUILD-0001.ref")));
    }

    [Fact]
    public async Task BulkCreateTasksForTrackReturnsCreatedTasksAndPartialFailure()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var nextIds = new RecordingNextIdService(ids: [1], failWhenIdsExhausted: true);
        var tools = CreateTools(projectRoot, nextIds);

        var result = await tools.BulkCreateTasksForTrack("PM",
        [
            new BulkTaskInputPayload("First", "Body"),
            new BulkTaskInputPayload("Second"),
        ]);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.RequestedCount);
        Assert.Equal(1, result.Data.CreatedCount);
        var created = Assert.Single(result.Data.Tasks);
        Assert.Equal("PM-0001", created.Id);
        Assert.Equal("Body", TaskItem.Parse(File.ReadAllText(created.FilePath))!.Description);
        Assert.Equal("next_id_unavailable", result.Data.Failure!.ErrorCode);
    }

    [Fact]
    public async Task BulkAssignTasksToMilestoneReturnsUpdatedTaskPayload()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var tools = CreateTools(projectRoot);

        var result = tools.BulkAssignTasksToMilestone("m1", ["PM-0001"]);

        Assert.True(result.Success);
        Assert.Equal("m1", result.Data!.Milestone);
        Assert.Equal(["PM-0001"], result.Data.TaskIds);
        Assert.Equal([projectRoot.GetTaskFilePath("PM-0001")], result.Data.FilePaths);
        Assert.Equal(1, result.Data.UpdatedCount);
        Assert.Equal("m1", TaskItem.Parse(File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")))!.Milestone);
    }

    [Fact]
    public async Task BulkAssignTasksToMilestoneRejectsDuplicateIdsBeforeWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var originalContent = File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001"));
        var tools = CreateTools(projectRoot);

        var result = tools.BulkAssignTasksToMilestone("m1", ["PM-0001", " PM-0001 "]);

        Assert.False(result.Success);
        Assert.Equal("duplicate_task_id", result.ErrorCode);
        Assert.Equal(originalContent, File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));
    }

    [Fact]
    public async Task MoveTaskUpdatesStateRefs()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Move me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.MoveTask("PM-0001", "done");

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "done", "PM-0001.ref")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task RemoveTaskDeletesFilesAndReportsMissingTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Remove me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        var missing = tools.RemoveTask("PM-9999");
        var removed = tools.RemoveTask("PM-0001");

        Assert.False(missing.Success);
        Assert.Equal("missing_task", missing.ErrorCode);
        Assert.True(removed.Success);
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task UpdateTaskMarkdownRejectsInvalidMarkdownAndChangedIds()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var tools = CreateTools(projectRoot);

        var invalid = tools.UpdateTaskMarkdown("PM-0001", "not markdown");
        var changedId = tools.UpdateTaskMarkdown("PM-0001", TestData.Task("PM-0002", "Changed").ToMarkdown());
        var updated = tools.UpdateTaskMarkdown("PM-0001", (task with { Title = "Updated" }).ToMarkdown());

        Assert.False(invalid.Success);
        Assert.Equal("invalid_edited_markdown", invalid.ErrorCode);
        Assert.False(changedId.Success);
        Assert.Equal("changed_task_id", changedId.ErrorCode);
        Assert.True(updated.Success);
        Assert.Contains("title: Updated", File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task WikiToolsCreateReadListAndUpdatePages()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        var created = tools.CreateWikiPage("architecture/rendering", "Rendering", "# Rendering");
        var list = tools.ListWikiPages();
        var read = tools.GetWikiPage("architecture/rendering");
        var updatedMarkdown = read.Data!.Markdown.Replace("title: Rendering", "title: Render Pipeline")
            .Replace("# Rendering", "# Updated");
        var updated = tools.UpdateWikiPageMarkdown("architecture/rendering", updatedMarkdown);

        Assert.True(created.Success);
        Assert.Equal("architecture/rendering", created.Data!.Path);
        Assert.Equal(projectRoot.TryResolveWikiPath("architecture/rendering", out _, out var filePath) ? filePath : "",
            created.Data.FilePath);
        var page = Assert.Single(list.Data!.Pages);
        Assert.Equal("architecture/rendering", page.Path);
        Assert.Equal("Rendering", read.Data.Title);
        Assert.Equal("# Rendering", read.Data.Body);
        Assert.True(updated.Success);
        Assert.Equal("Render Pipeline", updated.Data!.Title);
        Assert.Equal("# Updated", updated.Data.Body);
    }

    [Fact]
    public async Task WikiToolsReturnStableFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var missingTools = CreateTools(new ProjectRoot());

        Assert.Equal("missing_project", missingTools.ListWikiPages().ErrorCode);

        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        Assert.Equal("invalid_wiki_path", tools.CreateWikiPage("../escape", "Escape").ErrorCode);
        Assert.Equal("missing_wiki_page", tools.GetWikiPage("missing").ErrorCode);
        Assert.True(tools.CreateWikiPage("notes", "Notes").Success);
        Assert.Equal("duplicate_wiki_page", tools.CreateWikiPage("notes", "Duplicate").ErrorCode);
        Assert.Equal("invalid_wiki_markdown", tools.UpdateWikiPageMarkdown("notes", "not markdown").ErrorCode);
    }

    [Fact]
    public async Task AddTrackAndMilestoneReturnDuplicateAndInvalidErrors()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        Assert.True(tools.AddTrack("BUILD", "Build").Success);
        Assert.Equal("duplicate_track", tools.AddTrack("BUILD", "Duplicate").ErrorCode);
        Assert.Equal("invalid_track", tools.AddTrack(" ", "Missing").ErrorCode);

        Assert.True(tools.AddMilestone("m1", "Milestone 1").Success);
        Assert.Equal("duplicate_milestone", tools.AddMilestone("m1", "Duplicate").ErrorCode);
        Assert.Equal("invalid_milestone", tools.AddMilestone("m2", " ").ErrorCode);
    }

    [Fact]
    public async Task StatusToolsAddRenameRemoveAndReturnStableFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Todo task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        Assert.True(tools.AddStatus("blocked", "Blocked").Success);
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "blocked")) ||
                    Directory.Exists(Path.Combine(projectRoot.StatesPath, "blocked")));
        Assert.Equal("duplicate_status", tools.AddStatus("blocked", "Duplicate").ErrorCode);
        Assert.True(tools.RenameStatus("todo", "Ready").Success);
        Assert.Equal("status_in_use", tools.RemoveStatus("todo").ErrorCode);
        Assert.True(tools.RemoveStatus("blocked").Success);
        Assert.False(Directory.Exists(Path.Combine(projectRoot.StatesPath, "blocked")));
        Assert.Equal("missing_status", tools.RemoveStatus("missing").ErrorCode);

        var project = tools.GetProject();
        Assert.Contains(project.Data!.States, state => state.Key == "todo" && state.Name == "Ready");
        Assert.DoesNotContain(project.Data.States, state => state.Key == "blocked");
    }

    [Fact]
    public async Task RenameTrackAndMilestoneToolsWorkWhileReferenced()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        projectRoot.WriteTask(TestData.Task("BUILD-0001", "Build task", track: "BUILD", milestone: "m1"));
        var tools = CreateTools(projectRoot);

        Assert.True(tools.RenameTrack("BUILD", "Build Work").Success);
        Assert.True(tools.RenameMilestone("m1", "Launch").Success);
        Assert.Equal("missing_track", tools.RenameTrack("missing", "Missing").ErrorCode);
        Assert.Equal("missing_milestone", tools.RenameMilestone("missing", "Missing").ErrorCode);

        var project = tools.GetProject();
        Assert.Contains(project.Data!.Tracks, track => track.Key == "BUILD" && track.Name == "Build Work");
        Assert.Contains(project.Data.Milestones, milestone => milestone.Key == "m1" && milestone.Name == "Launch");
    }

    [Fact]
    public async Task RemoveTrackAndMilestoneRejectReferencedItemsAndRemoveUnusedItems()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build", ["UI"] = "UI" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        projectRoot.WriteTask(TestData.Task("BUILD-0001", "Build task", track: "BUILD", milestone: "m1"));
        var tools = CreateTools(projectRoot);

        Assert.Equal("track_in_use", tools.RemoveTrack("BUILD").ErrorCode);
        Assert.Equal("milestone_in_use", tools.RemoveMilestone("m1").ErrorCode);
        Assert.True(tools.RemoveTrack("UI").Success);
        Assert.True(tools.RemoveMilestone("m2").Success);

        var project = tools.GetProject();
        Assert.DoesNotContain(project.Data!.Tracks, track => track.Key == "UI");
        Assert.DoesNotContain(project.Data.Milestones, milestone => milestone.Key == "m2");
    }

    [Fact]
    public void McpHostBuildsWithoutWritingToStdout()
    {
        using var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using var host = McpServerHost.CreateBuilder([]).Build();

            Assert.NotNull(host.Services.GetRequiredService<ProjectRoot>());
            Assert.Equal(string.Empty, stdout.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static PmMcpTools CreateTools(ProjectRoot projectRoot, INextIdService? nextIdService = null)
    {
        nextIdService ??= new RecordingNextIdService();
        return new PmMcpTools(
            projectRoot,
            new TaskService(projectRoot, nextIdService),
            new ProjectCreationService(projectRoot, nextIdService),
            new ProjectConfigService(projectRoot),
            new BoardService(projectRoot),
            new WikiService(projectRoot));
    }

    private sealed class RecordingNextIdService(
        bool healthy = true,
        IReadOnlyList<int>? ids = null,
        bool failWhenIdsExhausted = false) : INextIdService
    {
        public List<string> GetNextIdTracks { get; } = [];
        public int HealthyCalls { get; private set; }
        private int _idIndex;

        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            GetNextIdTracks.Add(track);
            if (ids == null)
                return Task.FromResult(1);

            if (_idIndex < ids.Count)
                return Task.FromResult(ids[_idIndex++]);

            if (failWhenIdsExhausted)
                throw new InvalidOperationException("No more IDs.");

            return Task.FromResult(ids[^1] + 1);
        }

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(1);
        }

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default)
        {
            HealthyCalls++;
            return Task.FromResult(healthy);
        }
    }
}
