using PM.Application;
using PM.Tasks;

namespace PM.Tests;

public class BoardServiceTests
{
    [Fact]
    public async Task BoardDataGroupsByMilestoneAndState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var assigned = TestData.Task("BUILD-0001", "Assigned task", track: "BUILD", milestone: "m1");
        var unassigned = TestData.Task("PM-0001", "Unassigned task");
        projectRoot.WriteTask(assigned);
        projectRoot.WriteTask(unassigned);
        projectRoot.UpdateTaskState(assigned, "review");
        projectRoot.UpdateTaskState(unassigned, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;

        var milestone = Assert.Single(board.MilestoneGroups, group => group.Key == "m1");
        Assert.Equal("Milestone 1", milestone.Name);
        Assert.Contains(milestone.States.Single(state => state.Key == "review").Tasks,
            task => task.Task.Id == "BUILD-0001");
        var defaultMilestone = Assert.Single(board.MilestoneGroups, group => group.Key == null);
        Assert.Contains(defaultMilestone.States.Single(state => state.Key == "todo").Tasks,
            task => task.Task.Id == "PM-0001");
    }

    [Fact]
    public async Task BoardDataFiltersTrackMilestoneAndStateTogether()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var match = TestData.Task("BUILD-0001", "Matching task", track: "BUILD", milestone: "m1");
        var wrongTrack = TestData.Task("PM-0001", "Wrong track", track: "PM", milestone: "m1");
        var wrongMilestone = TestData.Task("BUILD-0002", "Wrong milestone", track: "BUILD", milestone: "m2");
        var wrongState = TestData.Task("BUILD-0003", "Wrong state", track: "BUILD", milestone: "m1");
        foreach (var task in new[] { match, wrongTrack, wrongMilestone, wrongState }) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(match, "review");
        projectRoot.UpdateTaskState(wrongTrack, "review");
        projectRoot.UpdateTaskState(wrongMilestone, "review");
        projectRoot.UpdateTaskState(wrongState, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery("BUILD", "m1", "review")).Payload!;

        Assert.Equal("Matching task", Assert.Single(board.Tasks).Task.Title);
    }

    [Fact]
    public async Task BoardNavigationCountsRemainingTasksAndPreservesConfiguredOptions()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Product", ["BUILD"] = "Build", ["EMPTY"] = "Empty" },
            milestones: new Dictionary<string, string> { ["m2"] = "Second", ["m1"] = "First", ["empty"] = "Empty" }));
        var build = TestData.Task("BUILD-0001", "Build", track: "BUILD", milestone: "m1");
        var done = TestData.Task("PM-0001", "Done", track: "PM", milestone: "m2");
        var unassigned = TestData.Task("PM-0002", "Unassigned", track: "PM");
        foreach (var task in new[] { build, done, unassigned }) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(build, "todo");
        projectRoot.UpdateTaskState(done, "done");
        projectRoot.UpdateTaskState(unassigned, "review");

        var navigation = new BoardService(projectRoot).GetNavigation().Payload!;

        Assert.Equal(2, navigation.RemainingCount);
        Assert.Equal(new[] { "PM", "BUILD", "EMPTY" }, navigation.Tracks.Select(option => option.Key));
        Assert.Equal(new[] { 1, 1, 0 }, navigation.Tracks.Select(option => option.RemainingCount));
        Assert.Equal(new[] { "m2", "m1", "empty" }, navigation.Milestones.Select(option => option.Key));
        Assert.Equal(new[] { 0, 1, 0 }, navigation.Milestones.Select(option => option.RemainingCount));
    }

    [Fact]
    public async Task TaskWithoutTrackUsesDefaultTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM"));
        var task = TestData.Task("PM-0001", "Task without track", track: null);
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var boardTask = Assert.Single(new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!.Tasks);

        Assert.Equal("PM", boardTask.Track);
    }

    [Fact]
    public void LocalDependencyStatusDistinguishesInvalidAndUnavailableReferences()
    {
        var invalid = TestData.Task("PM-0001", "Invalid", dependsOn: ["pm:not-a-reference"]);
        var unavailable = TestData.Task("PM-0002", "Unavailable",
            dependsOn: ["pm://project/prj_other/task/PM-0001"]);

        var invalidStatus = BoardService.BuildDependencyStatus(
            invalid, new Dictionary<string, TaskItem>(), new Dictionary<string, string>(), "prj_current");
        var unavailableStatus = BoardService.BuildDependencyStatus(
            unavailable, new Dictionary<string, TaskItem>(), new Dictionary<string, string>(), "prj_current");

        Assert.Equal(["pm:not-a-reference"], invalidStatus.Invalid);
        Assert.Empty(invalidStatus.Missing);
        Assert.Equal(["pm://project/prj_other/task/PM-0001"], unavailableStatus.Unavailable);
        Assert.Empty(unavailableStatus.Missing);
    }
}
