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
            new ProjectConfigService(projectRoot),
            new BoardService(projectRoot));
    }

    private sealed class RecordingNextIdService : INextIdService
    {
        public List<string> GetNextIdTracks { get; } = [];

        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            GetNextIdTracks.Add(track);
            return Task.FromResult(1);
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
            return Task.FromResult(true);
        }
    }
}
