using PM.Application;
using PM.Files;
using PM.Project;

namespace PM.Tests;

public sealed class TaskLifecycleMutationServiceTests
{
    [Fact]
    public async Task FailureAfterSourceDeletionRestoresExactTaskStateAndOrder()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Keep state");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", null), [task.Id]);
        root.SetTaskOrder(new TaskOrderScope("PM", "review", null), []);
        var sourcePath = Path.Combine(root.StatesPath, "todo", $"{task.Id}.ref");
        var destinationPath = Path.Combine(root.StatesPath, "review", $"{task.Id}.ref");
        var originalSource = File.ReadAllText(sourcePath);
        var originalOrder = File.ReadAllText(root.TaskOrderPath);
        var service = CreateService(root);

        var result = service.Execute(
            task,
            task,
            "todo",
            "review",
            () =>
            {
                root.UpdateTaskState(task, "review");
                throw new InvalidOperationException("Injected mutation tracking failure.");
            },
            "task_state_write_failed",
            "Task PM-0001 could not be moved to review.");

        Assert.False(result.Success);
        Assert.Equal("task_state_write_failed", result.ErrorCode);
        Assert.Equal(originalSource, File.ReadAllText(sourcePath));
        Assert.False(File.Exists(destinationPath));
        Assert.Equal(originalOrder, File.ReadAllText(root.TaskOrderPath));
    }

    [Fact]
    public async Task FailedRestorationReturnsBoundedRollbackFailure()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Report rollback failure");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var sourcePath = Path.Combine(root.StatesPath, "todo", $"{task.Id}.ref");
        var service = CreateService(root);

        var result = service.Execute(
            task,
            task,
            "todo",
            "review",
            () =>
            {
                root.UpdateTaskState(task, "review");
                Directory.CreateDirectory(sourcePath);
                throw new IOException("Injected failure after source deletion.");
            },
            "task_state_write_failed",
            "Task PM-0001 could not be moved to review.");

        Assert.False(result.Success);
        Assert.Equal("task_mutation_rollback_failed", result.ErrorCode);
        Assert.Contains("could not be fully restored", result.Message);
    }

    private static TaskLifecycleMutationService CreateService(ProjectRoot root)
    {
        var resolver = new MilestoneActivationResolver(root);
        return new TaskLifecycleMutationService(
            root,
            resolver,
            new AutomaticActivationService(resolver, TimeProvider.System),
            new ProjectConfigPersistence(root));
    }
}
