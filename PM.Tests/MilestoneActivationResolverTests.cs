using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class MilestoneActivationResolverTests
{
    [Fact]
    public async Task ResolvesTaskAndMilestoneRequirementsWithoutVacuousSatisfaction()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["source"] = "Source",
            ["consumer"] = "Consumer",
        });
        config.Milestones["source"].Delivery = OrdinaryDelivery();
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry"];
        config.ActivationTriggers["manual"] = new ActivationTriggerDefinition { Title = "Manual" };
        config.ActivationTriggers["entry"] = new ActivationTriggerDefinition
        {
            Title = "Entry",
            Requirements =
            [
                TaskRequirement("PM-0001"),
                TaskRequirement("PM-0002"),
                TaskRequirement("PM-9999"),
                MilestoneRequirement("source"),
            ],
        };
        config.ActivationTriggers["satisfied-pending"] = new ActivationTriggerDefinition
        {
            Title = "Satisfied but not latched",
            Requirements = [TaskRequirement("PM-0001")],
        };
        var tasks = Tasks(
            TestData.Task("PM-0001", "Done requirement"),
            TestData.Task("PM-0002", "Open requirement"),
            TestData.Task("PM-0003", "Delivered work", milestone: "source"),
            TestData.Task("PM-0004", "Consumer work", milestone: "consumer"));
        var states = States(
            ("PM-0001", "done"),
            ("PM-0002", "todo"),
            ("PM-0003", "done"),
            ("PM-0004", "todo"));

        var snapshot = new MilestoneActivationResolver(root).Resolve(config, tasks, states);

        var manual = Trigger(snapshot, "manual");
        Assert.False(manual.RequirementsSatisfied);
        Assert.Equal(0, manual.RequirementCount);
        var entry = Trigger(snapshot, "entry");
        Assert.Equal(2, entry.SatisfiedRequirementCount);
        Assert.Equal(4, entry.RequirementCount);
        Assert.False(entry.RequirementsSatisfied);
        Assert.True(Requirement(entry, ActivationRequirementKind.Task, "PM-0001").IsSatisfied);
        Assert.False(Requirement(entry, ActivationRequirementKind.Task, "PM-0002").IsSatisfied);
        Assert.False(Requirement(entry, ActivationRequirementKind.Task, "PM-9999").IsSatisfied);
        Assert.True(Requirement(entry, ActivationRequirementKind.Milestone, "source").IsSatisfied);
        var satisfiedPending = Trigger(snapshot, "satisfied-pending");
        Assert.True(satisfiedPending.RequirementsSatisfied);
        Assert.False(satisfiedPending.IsActive);
        Assert.False(satisfiedPending.IsLatchedDespiteUnmetRequirements);
    }

    [Fact]
    public async Task AutomaticActivationRemainsLatchedAfterRequirementBecomesUnmet()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config();
        config.ActivationTriggers["entry"] = new ActivationTriggerDefinition
        {
            Title = "Entry",
            Requirements = [TaskRequirement("PM-0001")],
            Activation = new ActivationRecord
            {
                At = Timestamp,
                Mode = ActivationMode.Automatic,
            },
        };

        var snapshot = new MilestoneActivationResolver(root).Resolve(
            config,
            Tasks(TestData.Task("PM-0001", "Reopened work")),
            States(("PM-0001", "todo")));

        var trigger = Trigger(snapshot, "entry");
        Assert.True(trigger.IsActive);
        Assert.Equal(ActivationMode.Automatic, trigger.Activation!.Mode);
        Assert.False(trigger.RequirementsSatisfied);
        Assert.True(trigger.IsLatchedDespiteUnmetRequirements);
        Assert.Equal(0, trigger.SatisfiedRequirementCount);
    }

    [Fact]
    public async Task OverrideProvenanceRemainsSeparateWhenRequirementsLaterBecomeSatisfied()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config();
        config.ActivationTriggers["entry"] = new ActivationTriggerDefinition
        {
            Title = "Entry",
            Requirements = [TaskRequirement("PM-0001"), TaskRequirement("PM-0002")],
            Activation = new ActivationRecord
            {
                At = Timestamp,
                Mode = ActivationMode.Override,
                Reason = "The second task was accepted at entry.",
                WaivedRequirements = [TaskRequirement("PM-0002")],
            },
        };
        var tasks = Tasks(
            TestData.Task("PM-0001", "Originally complete"),
            TestData.Task("PM-0002", "Completed later"));

        var snapshot = new MilestoneActivationResolver(root).Resolve(
            config,
            tasks,
            States(("PM-0001", "done"), ("PM-0002", "done")));

        var trigger = Trigger(snapshot, "entry");
        Assert.True(trigger.IsActive);
        Assert.True(trigger.RequirementsSatisfied);
        Assert.False(trigger.IsLatchedDespiteUnmetRequirements);
        Assert.Equal(2, trigger.SatisfiedRequirementCount);
        Assert.Equal(ActivationMode.Override, trigger.Activation!.Mode);
        Assert.Equal("The second task was accepted at entry.", trigger.Activation.Reason);
        var waived = Assert.Single(trigger.Activation.WaivedRequirements);
        Assert.Equal("PM-0002", waived.Source);
        Assert.True(Requirement(trigger, ActivationRequirementKind.Task, "PM-0002").WasWaivedAtActivation);
        Assert.True(Requirement(trigger, ActivationRequirementKind.Task, "PM-0002").IsSatisfied);
    }

    [Fact]
    public async Task ReusableTriggerReportsEveryConsumingMilestone()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["first"] = "First",
            ["second"] = "Second",
        });
        config.ActivationTriggers["shared"] = new ActivationTriggerDefinition { Title = "Shared" };
        config.Milestones["first"].RequiredActivationTriggers = ["shared"];
        config.Milestones["second"].RequiredActivationTriggers = ["shared"];

        var snapshot = new MilestoneActivationResolver(root).Resolve(config, Tasks(), States());

        Assert.Equal(["first", "second"], Trigger(snapshot, "shared").ConsumingMilestones);
    }

    [Fact]
    public async Task ResolvesMilestoneLifecycleUsingStrictPrecedence()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["delivered"] = "Delivered",
            ["inactive"] = "Inactive",
            ["ready"] = "Ready",
            ["active"] = "Active",
            ["empty"] = "Empty",
        });
        config.ActivationTriggers["pending"] = new ActivationTriggerDefinition
        {
            Title = "Pending",
            Requirements = [TaskRequirement("PM-1000")],
        };
        config.ActivationTriggers["on"] = new ActivationTriggerDefinition
        {
            Title = "On",
            Activation = new ActivationRecord
            {
                At = Timestamp,
                Mode = ActivationMode.Manual,
            },
        };
        config.Milestones["delivered"].RequiredActivationTriggers = ["pending"];
        config.Milestones["delivered"].Delivery = OrdinaryDelivery();
        config.Milestones["inactive"].RequiredActivationTriggers = ["pending", "missing"];
        config.Milestones["ready"].RequiredActivationTriggers = ["on"];
        config.Milestones["active"].RequiredActivationTriggers = ["on"];
        var tasks = Tasks(
            TestData.Task("PM-0001", "Delivered work", milestone: "delivered"),
            TestData.Task("PM-0002", "Blocked completed work", milestone: "inactive"),
            TestData.Task("PM-0003", "Ready work", milestone: "ready"),
            TestData.Task("PM-0004", "Active work", milestone: "active"));
        var states = States(
            ("PM-0001", "done"),
            ("PM-0002", "done"),
            ("PM-0003", "done"),
            ("PM-0004", "todo"));

        var snapshot = new MilestoneActivationResolver(root).Resolve(config, tasks, states);

        Assert.Equal(MilestoneLifecycle.Delivered, Milestone(snapshot, "delivered").Lifecycle);
        Assert.Equal(MilestoneLifecycle.Inactive, Milestone(snapshot, "inactive").Lifecycle);
        Assert.Equal(MilestoneLifecycle.ReadyToDeliver, Milestone(snapshot, "ready").Lifecycle);
        Assert.Equal(MilestoneLifecycle.Active, Milestone(snapshot, "active").Lifecycle);
        Assert.Equal(MilestoneLifecycle.Active, Milestone(snapshot, "empty").Lifecycle);
        Assert.Equal(["pending", "missing"], Milestone(snapshot, "inactive").UnmetActivationTriggers);
        Assert.Empty(Milestone(snapshot, "ready").UnmetActivationTriggers);
    }

    [Fact]
    public async Task InvalidDeliveryNeitherDeliversMilestoneNorSatisfiesRequirement()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["source"] = "Source",
            ["consumer"] = "Consumer",
        });
        config.Milestones["source"].Delivery = OrdinaryDelivery();
        config.ActivationTriggers["entry"] = new ActivationTriggerDefinition
        {
            Title = "Entry",
            Requirements = [MilestoneRequirement("source")],
        };
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry"];
        var tasks = Tasks(
            TestData.Task("PM-0001", "Still open", milestone: "source"),
            TestData.Task("PM-0002", "Consumer", milestone: "consumer"));
        var states = States(("PM-0001", "todo"), ("PM-0002", "todo"));

        var snapshot = new MilestoneActivationResolver(root).Resolve(config, tasks, states);

        var source = Milestone(snapshot, "source");
        Assert.Equal(MilestoneLifecycle.Active, source.Lifecycle);
        Assert.False(source.Delivery!.IsValid);
        Assert.False(Assert.Single(Trigger(snapshot, "entry").Requirements).IsSatisfied);
        Assert.Equal(MilestoneLifecycle.Inactive, Milestone(snapshot, "consumer").Lifecycle);
    }

    [Fact]
    public async Task ResolvesTheCurrentProjectThroughTheRepositoryBackedEntryPoint()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["release"] = "Release",
        });
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Release work", milestone: "release");
        root.WriteTask(task);
        root.UpdateTaskState(task, "done");

        var result = new MilestoneActivationResolver(root).ResolveCurrentProject();

        Assert.True(result.Success);
        var milestone = Assert.Single(result.Payload!.Milestones);
        Assert.Equal(MilestoneLifecycle.ReadyToDeliver, milestone.Lifecycle);
        Assert.Equal(1, milestone.AssignedTaskCount);
        Assert.Equal(1, milestone.DoneTaskCount);
    }

    private static readonly DateTimeOffset Timestamp = new(2026, 8, 6, 8, 15, 0, TimeSpan.Zero);

    private static MilestoneDelivery OrdinaryDelivery() => new()
    {
        At = Timestamp,
        Mode = MilestoneDeliveryMode.Ordinary,
    };

    private static ActivationRequirement TaskRequirement(string source) => new()
    {
        Kind = ActivationRequirementKind.Task,
        Source = source,
    };

    private static ActivationRequirement MilestoneRequirement(string source) => new()
    {
        Kind = ActivationRequirementKind.Milestone,
        Source = source,
    };

    private static Dictionary<string, TaskItem> Tasks(params TaskItem[] tasks) =>
        tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);

    private static Dictionary<string, string> States(params (string TaskId, string State)[] states) =>
        states.ToDictionary(state => state.TaskId, state => state.State, StringComparer.Ordinal);

    private static ResolvedActivationTrigger Trigger(MilestoneActivationSnapshot snapshot, string key) =>
        snapshot.ActivationTriggers.Single(trigger => trigger.Key == key);

    private static ResolvedActivationRequirement Requirement(
        ResolvedActivationTrigger trigger,
        ActivationRequirementKind kind,
        string source) =>
        trigger.Requirements.Single(requirement => requirement.Kind == kind && requirement.Source == source);

    private static ResolvedMilestone Milestone(MilestoneActivationSnapshot snapshot, string key) =>
        snapshot.Milestones.Single(milestone => milestone.Key == key);
}
