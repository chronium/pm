using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public class ApplicationServiceTests
{
    [Fact]
    public async Task CreateTaskValidatesTrackMilestoneAndUsesTrackScopedId()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var nextIds = new RecordingNextIdService();
        var service = new TaskService(projectRoot, nextIds);

        var invalidTrack = await service.CreateTask("Bad", "NOPE", null, "", false);
        var invalidMilestone = await service.CreateTask("Bad", "BUILD", "missing", "", false);
        var created = await service.CreateTask("Build task", "BUILD", "m1", "Body", false);

        Assert.Equal("invalid_track", invalidTrack.ErrorCode);
        Assert.Equal("invalid_milestone", invalidMilestone.ErrorCode);
        Assert.True(created.Success);
        Assert.Equal("BUILD-0001", created.Payload!.Id);
        Assert.Equal(["BUILD"], nextIds.GetNextIdTracks);
        Assert.True(File.Exists(Path.Combine(projectRoot.TasksPath, "BUILD-0001.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "BUILD-0001.ref")));
    }

    [Fact]
    public async Task DryRunCreateReturnsPlaceholderWithoutWritingOrAllocating()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var nextIds = new RecordingNextIdService();
        var service = new TaskService(projectRoot, nextIds);

        var result = await service.CreateTask("Preview", "PM", null, "Body", true);

        Assert.True(result.Success);
        Assert.Equal("PM-????", result.Payload!.Id);
        Assert.Equal(0, nextIds.GetNextIdCalls);
        Assert.Equal(0, nextIds.HealthyCalls);
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-????.md")));
    }

    [Fact]
    public async Task MoveTaskValidatesMissingTaskCurrentStateAndTargetState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Move me");
        projectRoot.WriteTask(task);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        Assert.Equal("invalid_state", service.MoveTask("PM-0001", "missing").ErrorCode);
        Assert.Equal("missing_task", service.MoveTask("PM-9999", "done").ErrorCode);
        Assert.Equal("missing_current_state", service.MoveTask("PM-0001", "done").ErrorCode);

        projectRoot.UpdateTaskState(task, "todo");
        var moved = service.MoveTask("PM-0001", "done");

        Assert.True(moved.Success);
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "done", "PM-0001.ref")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task RemoveTaskDeletesMarkdownAndStateRefs()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Remove me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var missing = service.RemoveTask("PM-9999");
        var removed = service.RemoveTask("PM-0001");

        Assert.Equal("missing_task", missing.ErrorCode);
        Assert.True(removed.Success);
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task EditValidationRejectsInvalidMarkdownAndChangedId()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        Assert.Equal("invalid_edited_markdown", service.SaveEditedTaskContent("PM-0001", "not markdown").ErrorCode);
        Assert.Equal("changed_task_id",
            service.SaveEditedTaskContent("PM-0001", TestData.Task("PM-0002", "Changed").ToMarkdown()).ErrorCode);

        var updated = task with { Title = "Updated" };
        var result = service.SaveEditedTaskContent("PM-0001", updated.ToMarkdown());

        Assert.True(result.Success);
        Assert.Contains("title: Updated", File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task TrackAndMilestoneAddRejectDuplicatesAndEmptyValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new ProjectConfigService(projectRoot);

        Assert.True(service.AddTrack("BUILD", "Build").Success);
        Assert.Equal("duplicate_track", service.AddTrack("BUILD", "Duplicate").ErrorCode);
        Assert.Equal("invalid_track", service.AddTrack(" ", "Missing").ErrorCode);

        Assert.True(service.AddMilestone("m1", "Milestone 1").Success);
        Assert.Equal("duplicate_milestone", service.AddMilestone("m1", "Duplicate").ErrorCode);
        Assert.Equal("invalid_milestone", service.AddMilestone("m2", " ").ErrorCode);
    }

    [Fact]
    public async Task TrackAndMilestoneRemoveRejectReferencedItemsAndPersistUnusedRemovals()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build", ["UI"] = "UI" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var task = TestData.Task("BUILD-0001", "Build task", track: "BUILD", milestone: "m1");
        projectRoot.WriteTask(task);
        var service = new ProjectConfigService(projectRoot);

        Assert.Equal("track_in_use", service.RemoveTrack("BUILD").ErrorCode);
        Assert.Equal("milestone_in_use", service.RemoveMilestone("m1").ErrorCode);
        Assert.Equal("missing_track", service.RemoveTrack("NOPE").ErrorCode);
        Assert.Equal("missing_milestone", service.RemoveMilestone("missing").ErrorCode);

        Assert.True(service.RemoveTrack("UI").Success);
        Assert.True(service.RemoveMilestone("m2").Success);

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.False(config.Tracks.ContainsKey("UI"));
        Assert.False(config.Milestones.ContainsKey("m2"));
    }

    [Fact]
    public async Task RemoveTrackRejectsLastTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new ProjectConfigService(projectRoot);

        var result = service.RemoveTrack("PM");

        Assert.Equal("last_track", result.ErrorCode);
    }

    [Fact]
    public async Task BoardServiceGroupsAndFiltersForListAndWeb()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var match = TestData.Task("BUILD-0001", "Matching task", "- Preview line", "BUILD", "m1");
        var wrong = TestData.Task("PM-0001", "Other task", track: "PM", milestone: "m2");
        projectRoot.WriteTask(match);
        projectRoot.WriteTask(wrong);
        projectRoot.UpdateTaskState(match, "review");
        projectRoot.UpdateTaskState(wrong, "todo");
        var service = new BoardService(projectRoot);

        var board = service.GetBoard(new BoardQuery("BUILD", "m1", "review"),
            BoardService.CliDescriptionPreviewLength).Payload!;
        var task = Assert.Single(board.MilestoneGroups.SelectMany(group => group.States).SelectMany(state => state.Tasks));

        Assert.Equal("Matching task", task.Task.Title);
        Assert.Equal("Preview line", task.DescriptionPreview);
        Assert.Equal("Milestone 1", Assert.Single(board.MilestoneGroups).Name);
        Assert.Equal("review", Assert.Single(board.MilestoneGroups.Single().States).Key);
    }

    private sealed class RecordingNextIdService : INextIdService
    {
        public int GetNextIdCalls { get; private set; }
        public int HealthyCalls { get; private set; }
        public List<string> GetNextIdTracks { get; } = [];

        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            GetNextIdCalls++;
            GetNextIdTracks.Add(track);
            return Task.FromResult(1);
        }

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default)
        {
            HealthyCalls++;
            return Task.FromResult(true);
        }
    }
}
