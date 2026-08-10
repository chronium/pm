using PM.Application;
using PM.Project;
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

        var board = TestBoardServices.Create(projectRoot).GetBoard(new BoardQuery()).Payload!;

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

        var board = TestBoardServices.Create(projectRoot).GetBoard(new BoardQuery("BUILD", "m1", "review")).Payload!;

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

        var navigation = TestBoardServices.Create(projectRoot).GetNavigation().Payload!;

        Assert.Equal(2, navigation.RemainingCount);
        Assert.Equal(new[] { "PM", "BUILD", "EMPTY" }, navigation.Tracks.Select(option => option.Key));
        Assert.Equal(new[] { 1, 1, 0 }, navigation.Tracks.Select(option => option.RemainingCount));
        Assert.Equal(new[] { "m2", "m1", "empty" }, navigation.Milestones.Select(option => option.Key));
        Assert.Equal(new[] { 0, 1, 0 }, navigation.Milestones.Select(option => option.RemainingCount));
    }

    [Fact]
    public async Task BoardHidesDeliveredWorkByDefaultAndRestoresItExplicitly()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            tracks: new Dictionary<string, string>
            {
                ["PM"] = "Product",
                ["BUILD"] = "Build",
                ["OPS"] = "Operations",
            },
            milestones: new Dictionary<string, string>
            {
                ["active"] = "Active",
                ["ordinary"] = "Ordinary delivery",
                ["exceptional"] = "Exceptional delivery",
            });
        config.Milestones["ordinary"].Delivery = new MilestoneDelivery
        {
            At = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero),
            Mode = MilestoneDeliveryMode.Ordinary,
        };
        config.Milestones["exceptional"].Delivery = new MilestoneDelivery
        {
            At = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero),
            Mode = MilestoneDeliveryMode.Exceptional,
            Reason = "Accepted with open work.",
            AcceptedTaskIds = ["OPS-0001"],
        };
        var projectRoot = await workspace.CreateProject(config);
        var active = TestData.Task("BUILD-0001", "Active task", track: "BUILD", milestone: "active");
        var ordinary = TestData.Task("PM-0001", "Ordinary delivered task", milestone: "ordinary");
        var exceptional = TestData.Task("OPS-0001", "Exceptional delivered task", track: "OPS",
            milestone: "exceptional");
        foreach (var task in new[] { active, ordinary, exceptional }) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(active, "todo");
        projectRoot.UpdateTaskState(ordinary, "done");
        projectRoot.UpdateTaskState(exceptional, "todo");
        var service = TestBoardServices.Create(projectRoot);

        var defaultBoard = service.GetBoard(new BoardQuery()).Payload!;
        var defaultNavigation = service.GetNavigation().Payload!;
        var includedBoard = service.GetBoard(new BoardQuery(IncludeDelivered: true)).Payload!;
        var includedNavigation = service.GetNavigation(includeDelivered: true).Payload!;
        var deliveredSelection = service.GetBoard(new BoardQuery(Milestone: "exceptional")).Payload!;

        Assert.Equal(["BUILD-0001"], defaultBoard.Tasks.Select(task => task.Task.Id));
        Assert.Equal(["active"], defaultBoard.Milestones.Select(milestone => milestone.Key));
        Assert.Equal(["PM", "BUILD", "OPS"], defaultBoard.Tracks.Select(track => track.Key));
        Assert.Equal([0, 1, 0], defaultNavigation.Tracks.Select(track => track.RemainingCount));
        Assert.Equal(["BUILD-0001", "OPS-0001", "PM-0001"],
            includedBoard.Tasks.Select(task => task.Task.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["active", "ordinary", "exceptional"],
            includedBoard.Milestones.Select(milestone => milestone.Key));
        Assert.Equal(2, includedNavigation.RemainingCount);
        Assert.Empty(deliveredSelection.Tasks);
        Assert.Empty(deliveredSelection.MilestoneGroups);

        projectRoot.Config!.Milestones["exceptional"].Delivery = null;
        projectRoot.Config.WriteConfig(projectRoot);

        var reopened = service.GetBoard(new BoardQuery()).Payload!;
        Assert.Contains(reopened.Tasks, task => task.Task.Id == "OPS-0001");
        Assert.Contains(reopened.Milestones, milestone => milestone.Key == "exceptional");
    }

    [Fact]
    public async Task TaskWithoutTrackUsesDefaultTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM"));
        var task = TestData.Task("PM-0001", "Task without track", track: null);
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var boardTask = Assert.Single(TestBoardServices.Create(projectRoot).GetBoard(new BoardQuery()).Payload!.Tasks);

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
