using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class TaskPlacementActivationPreflightTests
{
    [Fact]
    public async Task EverySingleTaskPlacementSurfaceRejectsDirectCycleWithoutWrites()
    {
        using var workspace = new TempWorkingDirectory();
        var config = DirectCycleConfig("consumer", "entry", "PM-0001");
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Entry work", "Original body");
        WriteTask(root, task, "todo");
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", null), [task.Id]);
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", "consumer"), []);
        var original = CaptureStorage(root, task.Id);

        // This two-argument construction is also used for linked-project mutation targets.
        var service = TestTaskServices.Create(root, new UnusedNextIdService());
        var patch = service.PatchTaskMetadata(task.Id, milestone: "consumer");
        AssertPreflightFailure(patch.Success, patch.ErrorCode, patch.Message);
        AssertStorageUnchanged(root, task, original);

        var update = service.UpdateTaskDetails(
            task.Id,
            "Changed title",
            "done",
            "Changed body",
            placement: new TaskPlacementUpdate("PM", "consumer"));
        AssertPreflightFailure(update.Success, update.ErrorCode, update.Message);
        AssertStorageUnchanged(root, task, original);

        var edited = task with { Milestone = "consumer", Title = "Edited title" };
        var save = service.SaveEditedTaskContent(task.Id, edited.ToMarkdown());
        AssertPreflightFailure(save.Success, save.ErrorCode, save.Message);
        AssertStorageUnchanged(root, task, original);
    }

    [Fact]
    public async Task ReassignmentRejectsIndirectCycleWithoutChangingPlacement()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: Milestones("a", "b", "c"));
        config.Milestones["a"].RequiredActivationTriggers = ["gate-a"];
        config.Milestones["b"].RequiredActivationTriggers = ["gate-b"];
        config.ActivationTriggers["gate-a"] = Trigger(MilestoneRequirement("b"));
        config.ActivationTriggers["gate-b"] = Trigger(TaskRequirement("PM-0001"));
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Reassigned work", milestone: "c");
        WriteTask(root, task, "todo");
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", "c"), [task.Id]);
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", "a"), []);
        var original = CaptureStorage(root, task.Id);
        var service = TestTaskServices.Create(root, new UnusedNextIdService());

        var result = service.PatchTaskMetadata(task.Id, milestone: "a");

        AssertPreflightFailure(result.Success, result.ErrorCode, result.Message);
        Assert.Equal(
            "Task milestone placement would create an activation cycle: " +
            "milestone:a -> trigger:gate-a -> milestone:b -> trigger:gate-b -> milestone:a.",
            result.Message);
        AssertStorageUnchanged(root, task, original);
    }

    [Fact]
    public async Task MilestoneRemovalThatBreaksCycleIsAllowedAndMovesOrderScope()
    {
        using var workspace = new TempWorkingDirectory();
        var config = DirectCycleConfig("consumer", "entry", "PM-0001");
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Entry work", milestone: "consumer");
        WriteTask(root, task, "todo");
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", "consumer"), [task.Id]);
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", null), []);
        var service = TestTaskServices.Create(root, new UnusedNextIdService());

        var result = service.PatchTaskMetadata(task.Id, milestone: " ");

        Assert.True(result.Success);
        Assert.True(result.Payload!.Changed);
        Assert.Null(result.Payload.Task.Milestone);
        Assert.Empty(root.GetTaskOrder(new TaskOrderScope("PM", "todo", "consumer")));
        Assert.Equal([task.Id], root.GetTaskOrder(new TaskOrderScope("PM", "todo", null)));
        Assert.Empty(new MilestoneActivationGraphService()
            .Build(config, Tasks(root.GetAllTasks().ToArray()))
            .Cycles);
    }

    [Fact]
    public async Task BulkAssignmentValidatesCompleteBatchBeforeFirstWrite()
    {
        using var workspace = new TempWorkingDirectory();
        var config = DirectCycleConfig("consumer", "entry", "PM-0002");
        var root = await workspace.CreateProject(config);
        var first = TestData.Task("PM-0001", "Safe first task", "First body");
        var second = TestData.Task("PM-0002", "Cycle-closing second task", "Second body");
        WriteTask(root, first, "todo");
        WriteTask(root, second, "todo");
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", null), [first.Id, second.Id]);
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", "consumer"), []);
        var firstOriginal = CaptureStorage(root, first.Id);
        var secondOriginal = CaptureStorage(root, second.Id);
        var service = TestTaskServices.Create(root, new UnusedNextIdService());

        var result = service.BulkAssignTasksToMilestone("consumer", [first.Id, second.Id]);

        AssertPreflightFailure(result.Success, result.ErrorCode, result.Message);
        AssertStorageUnchanged(root, first, firstOriginal);
        AssertStorageUnchanged(root, second, secondOriginal);
        Assert.Equal(
            [first.Id, second.Id],
            root.GetTaskOrder(new TaskOrderScope("PM", "todo", null)));
        Assert.Empty(root.GetTaskOrder(new TaskOrderScope("PM", "todo", "consumer")));
    }

    [Fact]
    public async Task ValidPlacementPreservesExistingAssignmentBehavior()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: Milestones("consumer"));
        config.ActivationTriggers["entry"] = Trigger(TaskRequirement("PM-0002"));
        var root = await workspace.CreateProject(config);
        var assigned = TestData.Task("PM-0001", "Assigned work", "Body");
        var unassignedRequirement = TestData.Task("PM-0002", "Unassigned prerequisite");
        WriteTask(root, assigned, "review");
        WriteTask(root, unassignedRequirement, "todo");
        root.SetTaskOrder(new TaskOrderScope("PM", "review", null), [assigned.Id]);
        root.SetTaskOrder(new TaskOrderScope("PM", "review", "consumer"), []);
        var service = TestTaskServices.Create(root, new UnusedNextIdService());

        var result = service.PatchTaskMetadata(assigned.Id, milestone: "consumer");

        Assert.True(result.Success);
        Assert.Equal("consumer", result.Payload!.Task.Milestone);
        Assert.Equal("Body", result.Payload.Task.Description);
        Assert.True(root.TryGetState(result.Payload.Task, out var state));
        Assert.Equal("review", state);
        Assert.Empty(root.GetTaskOrder(new TaskOrderScope("PM", "review", null)));
        Assert.Equal([assigned.Id], root.GetTaskOrder(new TaskOrderScope("PM", "review", "consumer")));
    }

    private static ProjectConfig DirectCycleConfig(string milestone, string trigger, string taskId)
    {
        var config = TestData.Config(milestones: Milestones(milestone));
        config.Milestones[milestone].RequiredActivationTriggers = [trigger];
        config.ActivationTriggers[trigger] = Trigger(TaskRequirement(taskId));
        return config;
    }

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

    private static void WriteTask(ProjectRoot root, TaskItem task, string state)
    {
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private static StorageSnapshot CaptureStorage(ProjectRoot root, string taskId)
    {
        var stateRefs = Directory.EnumerateFiles(root.StatesPath, $"{taskId}.ref", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);
        return new StorageSnapshot(
            File.ReadAllText(root.GetTaskFilePath(taskId)),
            File.Exists(root.TaskOrderPath) ? File.ReadAllText(root.TaskOrderPath) : null,
            stateRefs);
    }

    private static void AssertStorageUnchanged(ProjectRoot root, TaskItem task, StorageSnapshot original)
    {
        Assert.Equal(original.TaskMarkdown, File.ReadAllText(root.GetTaskFilePath(task.Id)));
        Assert.Equal(original.TaskOrder, File.Exists(root.TaskOrderPath) ? File.ReadAllText(root.TaskOrderPath) : null);
        var currentRefs = Directory.EnumerateFiles(root.StatesPath, $"{task.Id}.ref", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);
        Assert.Equal(original.StateRefs, currentRefs);
        var stored = TaskItem.Parse(original.TaskMarkdown)!;
        Assert.Equal(task.Milestone, stored.Milestone);
        Assert.Equal(task.ModifiedAt, stored.ModifiedAt);
    }

    private static void AssertPreflightFailure(bool success, string? errorCode, string? message)
    {
        Assert.False(success);
        Assert.Equal("activation_cycle", errorCode);
        Assert.StartsWith("Task milestone placement would create an activation cycle: ", message);
    }

    private sealed record StorageSnapshot(
        string TaskMarkdown,
        string? TaskOrder,
        IReadOnlyDictionary<string, string> StateRefs);

    private sealed class UnusedNextIdService : INextIdService
    {
        public Task<int> GetNextId(
            ProjectRoot projectRoot,
            string track,
            CancellationToken cancellationToken = default) =>
            Task.FromException<int>(new NotSupportedException());

        public Task<int> PeekNextId(
            ProjectRoot projectRoot,
            string track,
            CancellationToken cancellationToken = default) =>
            Task.FromException<int>(new NotSupportedException());

        public Task<int?> PeekExistingNextId(
            ProjectRoot projectRoot,
            string track,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(null);

        public Task<ProjectRegistration> RegisterProject(
            ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProjectRegistration>(new NotSupportedException());

        public Task<bool> Healthy(
            ProjectConfig config,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
