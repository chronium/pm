using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class ActivationEligibilityReadTests
{
    [Fact]
    public async Task BoardNavigationAndTaskReadsExposeActivationWithoutHidingWork()
    {
        using var workspace = new TempWorkingDirectory();
        var config = ActivationConfig();
        config.Milestones["delivered"].Delivery = ExceptionalDelivery("PM-0004");
        var root = await workspace.CreateProject(config);
        var inactive = AddTask(root, "PM-0001", "Inactive", "inactive", "todo");
        AddTask(root, "PM-0002", "Active", "active", "todo");
        AddTask(root, "PM-0003", "Ready", "ready", "done");
        AddTask(root, "PM-0004", "Delivered open work", "delivered", "todo");
        AddTask(root, "PM-0005", "Unassigned", null, "review");
        var service = TestBoardServices.Create(root);

        var board = service.GetBoard(new BoardQuery()).Payload!;
        var navigation = service.GetNavigation().Payload!;
        var detail = service.GetTask(inactive.Id).Payload!;

        Assert.Equal(5, board.Tasks.Count);
        Assert.Equal(MilestoneLifecycle.Inactive, Milestone(board, "inactive").Lifecycle);
        Assert.Equal(MilestoneLifecycle.Active, Milestone(board, "active").Lifecycle);
        Assert.Equal(MilestoneLifecycle.ReadyToDeliver, Milestone(board, "ready").Lifecycle);
        Assert.Equal(MilestoneLifecycle.Delivered, Milestone(board, "delivered").Lifecycle);
        var entry = board.MilestoneActivation.ActivationTriggers.Single(trigger => trigger.Key == "entry");
        Assert.False(entry.IsActive);
        Assert.Equal(0, entry.SatisfiedRequirementCount);
        Assert.Equal(1, entry.RequirementCount);

        Assert.False(Task(board, "PM-0001").Activation.IsEligible);
        Assert.Equal(["entry"], Task(board, "PM-0001").Activation.UnmetActivationTriggers);
        Assert.True(Task(board, "PM-0002").Activation.IsEligible);
        Assert.True(Task(board, "PM-0003").Activation.IsEligible);
        Assert.False(Task(board, "PM-0004").Activation.IsEligible);
        Assert.True(Task(board, "PM-0005").Activation.IsEligible);
        Assert.Null(Task(board, "PM-0005").Activation.MilestoneLifecycle);

        Assert.Equal(4, navigation.RemainingCount);
        var deliveredNavigation = navigation.Milestones.Single(item => item.Key == "delivered");
        Assert.Equal(1, deliveredNavigation.RemainingCount);
        Assert.Equal(MilestoneLifecycle.Delivered, deliveredNavigation.Lifecycle);
        var inactiveNavigation = navigation.Milestones.Single(item => item.Key == "inactive");
        Assert.Equal(["entry"], inactiveNavigation.UnmetActivationTriggers);

        Assert.False(detail.Activation.IsEligible);
        Assert.Contains("unmet activation triggers: entry", detail.Activation.Summary);
    }

    [Fact]
    public async Task NextTaskFiltersActivationBeforeReadinessAndPreservesEligibleRanking()
    {
        using var workspace = new TempWorkingDirectory();
        var config = ActivationConfig();
        config.Milestones["delivered"].Delivery = ExceptionalDelivery("PM-0002");
        var root = await workspace.CreateProject(config);
        AddTask(root, "PM-0001", "Inactive urgent", "inactive", "todo", "urgent");
        AddTask(root, "PM-0002", "Delivered urgent", "delivered", "todo", "urgent");
        var eligible = AddTask(root, "PM-0003", "Eligible ready", null, "todo", "low");
        AddTask(root, "PM-0004", "Eligible blocked", "active", "todo", "urgent", ["PM-9999"]);
        var service = TestBoardServices.Create(root);

        var ready = service.GetNextTask(new NextTaskQuery(ReadyOnly: true)).Payload!;
        root.UpdateTaskState(eligible, "done");
        var includeBlocked = service.GetNextTask(new NextTaskQuery(ReadyOnly: false)).Payload!;

        Assert.Equal("PM-0003", ready.Task!.Task.Id);
        Assert.True(ready.Task.Activation.IsEligible);
        Assert.DoesNotContain("Eligible:", ready.Reason);
        Assert.Equal("PM-0004", includeBlocked.Task!.Task.Id);
        Assert.True(includeBlocked.Task.Activation.IsEligible);
        Assert.False(includeBlocked.Task.Dependencies.Ready);
        Assert.Contains("Eligible: milestone active is active.", includeBlocked.Reason);
    }

    [Fact]
    public async Task ScopedNoResultReasonsExplainInactiveAndDeliveredMilestones()
    {
        using var workspace = new TempWorkingDirectory();
        var config = ActivationConfig();
        config.Milestones["delivered"].Delivery = ExceptionalDelivery("PM-0002");
        var root = await workspace.CreateProject(config);
        AddTask(root, "PM-0001", "Inactive", "inactive", "todo");
        AddTask(root, "PM-0002", "Delivered", "delivered", "todo");
        var service = TestBoardServices.Create(root);

        var inactive = service.GetNextTask(new NextTaskQuery(Milestone: "inactive", ReadyOnly: false)).Payload!;
        var delivered = service.GetNextTask(new NextTaskQuery(Milestone: "delivered", ReadyOnly: true)).Payload!;

        Assert.False(inactive.Found);
        Assert.Equal(
            "No activation-eligible task found for milestone inactive; milestone inactive is inactive; " +
            "unmet activation triggers: entry.",
            inactive.Reason);
        Assert.False(delivered.Found);
        Assert.Equal(
            "No activation-eligible task found for milestone delivered; milestone delivered is delivered.",
            delivered.Reason);
    }

    private static ProjectConfig ActivationConfig()
    {
        var config = TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["inactive"] = "Inactive",
                ["active"] = "Active",
                ["ready"] = "Ready",
                ["delivered"] = "Delivered",
            },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["entry"] = new()
                {
                    Title = "Entry",
                    Requirements =
                    [
                        new ActivationRequirement
                        {
                            Kind = ActivationRequirementKind.Task,
                            Source = "PM-9998",
                        },
                    ],
                },
                ["open"] = new()
                {
                    Title = "Open",
                    Activation = new ActivationRecord
                    {
                        At = Timestamp,
                        Mode = ActivationMode.Manual,
                    },
                },
            });
        config.Milestones["inactive"].RequiredActivationTriggers = ["entry"];
        config.Milestones["active"].RequiredActivationTriggers = ["open"];
        config.Milestones["ready"].RequiredActivationTriggers = ["open"];
        return config;
    }

    private static MilestoneDelivery ExceptionalDelivery(string acceptedTaskId) => new()
    {
        At = Timestamp,
        Mode = MilestoneDeliveryMode.Exceptional,
        Reason = "Accepted for this read-model scenario.",
        AcceptedTaskIds = [acceptedTaskId],
    };

    private static PM.Tasks.TaskItem AddTask(
        ProjectRoot root,
        string id,
        string title,
        string? milestone,
        string state,
        string? priority = null,
        IReadOnlyList<string>? dependsOn = null)
    {
        var task = TestData.Task(id, title, milestone: milestone, priority: priority, dependsOn: dependsOn);
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
        return task;
    }

    private static ResolvedMilestone Milestone(BoardData board, string key) =>
        board.MilestoneActivation.Milestones.Single(milestone => milestone.Key == key);

    private static BoardTask Task(BoardData board, string id) =>
        board.Tasks.Single(task => task.Task.Id == id);

    private static readonly DateTimeOffset Timestamp =
        new(2026, 8, 6, 8, 15, 0, TimeSpan.Zero);
}
