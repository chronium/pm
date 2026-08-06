using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class MilestoneActivationGraphTests
{
    [Fact]
    public void DetectsDirectCycleThroughMilestoneRequirement()
    {
        var config = TestData.Config(milestones: Milestones("consumer"));
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry"];
        config.ActivationTriggers["entry"] = Trigger(MilestoneRequirement("consumer"));

        var graph = new MilestoneActivationGraphService().Build(config, Tasks());

        Assert.Equal(
            ["milestone:consumer", "trigger:entry", "milestone:consumer"],
            CyclePath(Assert.Single(graph.Cycles)));
    }

    [Fact]
    public void DetectsDirectCycleThroughAssignedTaskRequirement()
    {
        var config = TestData.Config(milestones: Milestones("consumer"));
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry"];
        config.ActivationTriggers["entry"] = Trigger(TaskRequirement("PM-0001"));
        var tasks = Tasks(TestData.Task("PM-0001", "Entry work", milestone: "consumer"));

        var graph = new MilestoneActivationGraphService().Build(config, tasks);

        Assert.Equal(
            ["milestone:consumer", "trigger:entry", "milestone:consumer"],
            CyclePath(Assert.Single(graph.Cycles)));
    }

    [Fact]
    public void DetectsIndirectCycleAcrossMilestonesAndTriggers()
    {
        var config = TestData.Config(milestones: Milestones("public-beta", "foundation"));
        config.Milestones["public-beta"].RequiredActivationTriggers = ["beta-entry"];
        config.Milestones["foundation"].RequiredActivationTriggers = ["architecture-ready"];
        config.ActivationTriggers["beta-entry"] = Trigger(MilestoneRequirement("foundation"));
        config.ActivationTriggers["architecture-ready"] = Trigger(MilestoneRequirement("public-beta"));

        var graph = new MilestoneActivationGraphService().Build(config, Tasks());

        Assert.Equal(
            [
                "milestone:foundation",
                "trigger:architecture-ready",
                "milestone:public-beta",
                "trigger:beta-entry",
                "milestone:foundation",
            ],
            CyclePath(Assert.Single(graph.Cycles)));
    }

    [Fact]
    public async Task UnassignedTaskAddsNoEdgeButProspectivePlacementCreatesCycle()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: Milestones("consumer"));
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry"];
        config.ActivationTriggers["entry"] = Trigger(TaskRequirement("PM-0001"));
        var root = await workspace.CreateProject(config);
        var unassigned = TestData.Task("PM-0001", "Entry work");
        root.WriteTask(unassigned);
        root.UpdateTaskState(unassigned, "todo");
        var graphService = new MilestoneActivationGraphService();

        var current = graphService.Build(config, Tasks(unassigned));
        var prospectiveTask = unassigned with { Milestone = "consumer" };
        var prospectiveTasks = Tasks(prospectiveTask);
        var prospective = graphService.Build(config, prospectiveTasks);
        var prospectiveIssues = new MilestoneActivationValidationService(
                root, graphService, new MilestoneActivationResolver(root))
            .Validate(config, prospectiveTasks);

        Assert.Empty(current.Cycles);
        Assert.Empty(current.Edges[Node(MilestoneActivationGraphNodeKind.Trigger, "entry")]);
        Assert.Single(prospective.Cycles);
        Assert.Contains(prospectiveIssues, issue => issue.Code == "activation_cycle");
        Assert.Null(root.GetAllTasks().Single().Milestone);
    }

    [Fact]
    public void OrdinaryTaskDependenciesDoNotEnterActivationGraph()
    {
        var config = TestData.Config(milestones: Milestones("first", "second"));
        var tasks = Tasks(
            TestData.Task("PM-0001", "First", milestone: "first", dependsOn: ["PM-0002"]),
            TestData.Task("PM-0002", "Second", milestone: "second", dependsOn: ["PM-0001"]));

        var graph = new MilestoneActivationGraphService().Build(config, tasks);

        Assert.Empty(graph.Cycles);
        Assert.All(graph.Edges.Values, Assert.Empty);
    }

    [Fact]
    public void CollapsesDuplicateEdgesAndProducesDeterministicCycles()
    {
        var first = BuildOrderedCycle(reverse: false);
        var reversed = BuildOrderedCycle(reverse: true);
        var duplicateConfig = TestData.Config(milestones: Milestones("source"));
        duplicateConfig.ActivationTriggers["entry"] = Trigger(
            TaskRequirement("PM-0001"),
            TaskRequirement("PM-0001"),
            TaskRequirement("PM-0002"));
        var duplicateTasks = Tasks(
            TestData.Task("PM-0001", "First", milestone: "source"),
            TestData.Task("PM-0002", "Second", milestone: "source"));
        var service = new MilestoneActivationGraphService();

        var firstGraph = service.Build(first, Tasks());
        var reversedGraph = service.Build(reversed, Tasks());
        var duplicateGraph = service.Build(duplicateConfig, duplicateTasks);

        Assert.Equal(
            CyclePath(Assert.Single(firstGraph.Cycles)),
            CyclePath(Assert.Single(reversedGraph.Cycles)));
        Assert.Single(duplicateGraph.Edges[Node(MilestoneActivationGraphNodeKind.Trigger, "entry")]);
    }

    [Fact]
    public async Task ProjectValidationReportsDeterministicActivationCycleIssue()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: Milestones("consumer"));
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry"];
        config.ActivationTriggers["entry"] = Trigger(MilestoneRequirement("consumer"));
        var root = await workspace.CreateProject(config);

        var result = new ProjectValidationService(root).ValidateProject();

        Assert.True(result.Success);
        Assert.False(result.Payload!.Valid);
        var issue = Assert.Single(result.Payload.Issues, issue => issue.Code == "activation_cycle");
        Assert.Equal("error", issue.Severity);
        Assert.Equal(root.ConfigPath, issue.Path);
        Assert.Equal(
            "Milestone activation cycle detected: milestone:consumer -> trigger:entry -> milestone:consumer.",
            issue.Message);
    }

    private static ProjectConfig BuildOrderedCycle(bool reverse)
    {
        var config = TestData.Config();
        config.Milestones = reverse
            ? new Dictionary<string, MilestoneDefinition>
            {
                ["second"] = Milestone("Second", "ready"),
                ["first"] = Milestone("First", "entry"),
            }
            : new Dictionary<string, MilestoneDefinition>
            {
                ["first"] = Milestone("First", "entry"),
                ["second"] = Milestone("Second", "ready"),
            };
        config.ActivationTriggers = reverse
            ? new Dictionary<string, ActivationTriggerDefinition>
            {
                ["ready"] = Trigger(MilestoneRequirement("first")),
                ["entry"] = Trigger(MilestoneRequirement("second")),
            }
            : new Dictionary<string, ActivationTriggerDefinition>
            {
                ["entry"] = Trigger(MilestoneRequirement("second")),
                ["ready"] = Trigger(MilestoneRequirement("first")),
            };
        return config;
    }

    private static MilestoneDefinition Milestone(string title, string requiredTrigger) => new()
    {
        Title = title,
        RequiredActivationTriggers = [requiredTrigger],
    };

    private static Dictionary<string, string> Milestones(params string[] keys) =>
        keys.ToDictionary(key => key, key => key, StringComparer.Ordinal);

    private static ActivationTriggerDefinition Trigger(params ActivationRequirement[] requirements) => new()
    {
        Title = "Trigger",
        Requirements = requirements.ToList(),
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

    private static MilestoneActivationGraphNode Node(MilestoneActivationGraphNodeKind kind, string key) =>
        new(kind, key);

    private static IReadOnlyList<string> CyclePath(MilestoneActivationCycle cycle) =>
        cycle.Path.Select(node => node.ToString()).ToList();
}
