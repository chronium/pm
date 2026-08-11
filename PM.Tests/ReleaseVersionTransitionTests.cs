using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class ReleaseVersionTransitionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-11T09:00:00Z");

    [Fact]
    public async Task TaskCompletionAdvancesPatchExactlyOnceAndPersistsEvidence()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(root.ReleaseVersionPath, "1.4.6\n");
        var task = TestData.Task("PM-0001", "Ship it");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var service = CreateLifecycle(root);

        var result = service.Execute(
            task, task, "todo", "done", () => root.UpdateTaskState(task, "done"),
            "failed", "failed");

        Assert.True(result.Success, result.Message);
        Assert.Equal("1.4.7", File.ReadAllText(root.ReleaseVersionPath).Trim());
        Assert.Equal("task", result.Payload!.ReleaseTransition!.Kind);
        Assert.Equal("PM-0001", result.Payload.ReleaseTransition.Source);
        Assert.True(File.Exists(Path.Combine(root.ReleaseTransitionsPath, "1.4.7.yaml")));
        Assert.False(File.Exists(root.PendingReleaseTransitionPath));

        var repeat = service.Execute(task, task, "done", "done", () => { }, "failed", "failed");
        Assert.True(repeat.Success);
        Assert.Null(repeat.Payload!.ReleaseTransition);
        Assert.Equal("1.4.7", File.ReadAllText(root.ReleaseVersionPath).Trim());
    }

    [Fact]
    public async Task MilestoneAndMajorPlansUseTheirDefinedVersionDeltas()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(root.ReleaseVersionPath, "1.4.6\n");
        var releases = new ReleaseVersionService(root, new FixedTimeProvider(Now));

        var milestone = releases.PrepareMilestoneDelivery("public-beta");
        Assert.True(milestone.Success);
        Assert.Equal("1.5.0", milestone.Payload!.Transition.ToVersion);
        Assert.Equal("public-beta", milestone.Payload.Transition.Source);

        var major = releases.PreviewMajor("New compatibility boundary");
        Assert.True(major.Success);
        Assert.Equal("2.0.0", major.Payload!.Transition.ToVersion);
        Assert.Null(major.Payload.Transition.Source);
        Assert.Equal("New compatibility boundary", major.Payload.Transition.Reason);
        Assert.NotEmpty(major.Payload.Revision);
    }

    [Fact]
    public async Task ReconciliationCompletesAppliedTaskForwardAndIsIdempotent()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(root.ReleaseVersionPath, "1.0.0\n");
        var task = TestData.Task("PM-0001", "Recover completion");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var releases = new ReleaseVersionService(root, new FixedTimeProvider(Now));
        var plan = releases.PrepareTaskCompletion(task.Id).Payload!;
        Assert.True(releases.Begin(plan).Success);
        root.UpdateTaskState(task, "done");

        var preview = releases.Reconcile(dryRun: true);
        Assert.True(preview.Success);
        Assert.True(preview.Payload!.Changed);
        Assert.Equal("complete-forward", preview.Payload.Action);
        Assert.Equal("1.0.0", File.ReadAllText(root.ReleaseVersionPath).Trim());

        var repaired = releases.Reconcile();
        Assert.True(repaired.Success, repaired.Message);
        Assert.Equal("1.0.1", File.ReadAllText(root.ReleaseVersionPath).Trim());
        Assert.False(File.Exists(root.PendingReleaseTransitionPath));
        Assert.True(File.Exists(Path.Combine(root.ReleaseTransitionsPath, "1.0.1.yaml")));

        var again = releases.Reconcile();
        Assert.True(again.Success);
        Assert.False(again.Payload!.Changed);
    }

    [Fact]
    public async Task ReconciliationClearsUnappliedTaskIntentWithoutAdvancing()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(root.ReleaseVersionPath, "1.0.0\n");
        var task = TestData.Task("PM-0001", "Remain open");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var releases = new ReleaseVersionService(root, new FixedTimeProvider(Now));
        var plan = releases.PrepareTaskCompletion(task.Id).Payload!;
        Assert.True(releases.Begin(plan).Success);

        var repaired = releases.Reconcile();

        Assert.True(repaired.Success, repaired.Message);
        Assert.Equal("cleared-unapplied", repaired.Payload!.Action);
        Assert.Equal("1.0.0", File.ReadAllText(root.ReleaseVersionPath).Trim());
        Assert.False(File.Exists(root.PendingReleaseTransitionPath));
    }

    [Fact]
    public async Task UnversionedProjectsRemainUnchanged()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "No release policy");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");

        var result = CreateLifecycle(root).Execute(
            task, task, "todo", "done", () => root.UpdateTaskState(task, "done"),
            "failed", "failed");

        Assert.True(result.Success);
        Assert.Null(result.Payload!.ReleaseTransition);
        Assert.False(File.Exists(root.PendingReleaseTransitionPath));
        Assert.False(Directory.Exists(root.ReleaseTransitionsPath));
    }

    [Fact]
    public async Task DoctorRequiresPendingTransitionReconciliation()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(root.ReleaseVersionPath, "1.0.0\n");
        var task = TestData.Task("PM-0001", "Pending");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var releases = new ReleaseVersionService(root, new FixedTimeProvider(Now));
        Assert.True(releases.Begin(releases.PrepareTaskCompletion(task.Id).Payload!).Success);

        var validation = new ProjectValidationService(root).ValidateProject();

        Assert.True(validation.Success);
        Assert.False(validation.Payload!.Valid);
        Assert.Contains(validation.Payload.Issues, issue => issue.Code == "release_reconciliation_required");
    }

    private static TaskLifecycleMutationService CreateLifecycle(ProjectRoot root)
    {
        var resolver = new MilestoneActivationResolver(root);
        var time = new FixedTimeProvider(Now);
        return new TaskLifecycleMutationService(
            root,
            resolver,
            new AutomaticActivationService(resolver, time),
            new ProjectConfigPersistence(root),
            new ReleaseVersionService(root, time));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
