using PM.Application;
using PM.Files;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class AutomaticActivationLifecycleTests
{
    [Fact]
    public async Task CompletingFinalAffectedRequirementLatchesTriggerAndReportsLifecycleChange()
    {
        using var workspace = new TempWorkingDirectory();
        var config = ActivationConfig();
        config.ActivationTriggers["already-satisfied"] = new ActivationTriggerDefinition
        {
            Title = "Unrelated recovery candidate",
            Requirements = [TaskRequirement("PM-0001")],
        };
        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Foundation one", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Foundation two", milestone: "foundation"), "todo");
        WriteTask(root, TestData.Task("PM-0003", "Beta work", milestone: "beta"), "todo");
        var activatedAt = DateTimeOffset.Parse("2026-08-06T18:15:00Z");
        var service = TestTaskServices.Create(
            root, new UnusedNextIdService(), new FixedTimeProvider(activatedAt));

        var result = service.MoveTask("PM-0002", "done");

        Assert.True(result.Success);
        var impact = result.Payload!.ActivationImpact;
        var activated = Assert.Single(impact.ActivatedTriggers);
        Assert.Equal("beta-entry", activated.Key);
        Assert.Equal(ActivationMode.Automatic, activated.Activation!.Mode);
        Assert.Equal(activatedAt, activated.Activation.At);
        var milestone = impact.MilestoneChanges.Single(change => change.MilestoneKey == "beta");
        Assert.Equal("beta", milestone.MilestoneKey);
        Assert.Equal(MilestoneLifecycle.Inactive, milestone.Before);
        Assert.Equal(MilestoneLifecycle.Active, milestone.After);

        var stored = ProjectConfig.ReadConfig(root);
        Assert.Equal(ActivationMode.Automatic,
            stored.ActivationTriggers["beta-entry"].Activation!.Mode);
        Assert.Equal(activatedAt, stored.ActivationTriggers["beta-entry"].Activation!.At);
        Assert.Null(stored.ActivationTriggers["already-satisfied"].Activation);
    }

    [Fact]
    public async Task CombinedTaskUpdateUsesTheSameAutomaticActivationWorkflow()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(ActivationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Foundation one", milestone: "foundation"), "done");
        var second = TestData.Task("PM-0002", "Foundation two", milestone: "foundation");
        WriteTask(root, second, "todo");
        WriteTask(root, TestData.Task("PM-0003", "Beta work", milestone: "beta"), "todo");
        var service = TestTaskServices.Create(root, new UnusedNextIdService());

        var result = service.UpdateTaskDetails(
            second.Id, "Foundation two complete", "done", second.Description);

        Assert.True(result.Success);
        Assert.Equal("Foundation two complete", result.Payload!.Value.Title);
        Assert.Equal("beta-entry", Assert.Single(result.Payload.ActivationImpact.ActivatedTriggers).Key);
        Assert.NotNull(ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation);
    }

    [Fact]
    public async Task ReopeningCompletedTaskDoesNotRemoveActivationRecord()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(ActivationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Foundation one", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Foundation two", milestone: "foundation"), "todo");
        var service = TestTaskServices.Create(root, new UnusedNextIdService());
        Assert.True(service.MoveTask("PM-0002", "done").Success);
        var activation = ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation!;

        var reopened = service.MoveTask("PM-0002", "todo");

        Assert.True(reopened.Success);
        Assert.Empty(reopened.Payload!.ActivationImpact.ActivatedTriggers);
        var persisted = ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation!;
        Assert.Equal(activation.At, persisted.At);
        Assert.Equal(activation.Mode, persisted.Mode);
        Assert.Equal(activation.Reason, persisted.Reason);
        Assert.Equal(activation.WaivedRequirements, persisted.WaivedRequirements);
    }

    [Fact]
    public async Task CompletionWithoutAffectedTriggersDoesNotRewriteConfiguration()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["release"] = "Release" }));
        var task = TestData.Task("PM-0001", "Release work", milestone: "release");
        WriteTask(root, task, "todo");
        var before = File.ReadAllText(root.ConfigPath);
        var service = TestTaskServices.Create(root, new UnusedNextIdService());

        var result = service.MoveTask(task.Id, "done");

        Assert.True(result.Success);
        Assert.Empty(result.Payload!.ActivationImpact.ActivatedTriggers);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
    }

    [Fact]
    public async Task ActivationPersistenceFailureRestoresTaskStateAndProvenance()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(ActivationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Foundation one", milestone: "foundation"), "done");
        var second = TestData.Task("PM-0002", "Foundation two", milestone: "foundation");
        WriteTask(root, second, "todo");
        var before = File.ReadAllText(root.ConfigPath);
        var persistence = new FaultingProjectConfigPersistence(root) { FailWrite = true };
        var service = TestTaskServices.Create(
            root, new UnusedNextIdService(), persistence: persistence);

        var result = service.MoveTask(second.Id, "done");

        Assert.False(result.Success);
        Assert.Equal("task_lifecycle_transition_failed", result.ErrorCode);
        Assert.True(File.Exists(Path.Combine(root.StatesPath, "todo", $"{second.Id}.ref")));
        Assert.False(File.Exists(Path.Combine(root.StatesPath, "done", $"{second.Id}.ref")));
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation);
    }

    [Fact]
    public async Task ReloadFailureAfterActivationWriteRestoresExactTaskAndConfigurationState()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(ActivationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Foundation one", milestone: "foundation"), "done");
        var second = TestData.Task("PM-0002", "Foundation two", milestone: "foundation");
        WriteTask(root, second, "todo");
        root.SetTaskOrder(new TaskOrderScope("PM", "todo", "foundation"), [second.Id]);
        var beforeConfig = File.ReadAllText(root.ConfigPath);
        var beforeOrder = File.ReadAllText(root.TaskOrderPath);
        var persistence = new FaultingProjectConfigPersistence(root) { ReloadFailuresAfterWrite = 1 };
        var service = TestTaskServices.Create(
            root, new UnusedNextIdService(), persistence: persistence);

        var result = service.MoveTask(second.Id, "done");

        Assert.False(result.Success);
        Assert.Equal("task_lifecycle_transition_failed", result.ErrorCode);
        Assert.True(File.Exists(Path.Combine(root.StatesPath, "todo", $"{second.Id}.ref")));
        Assert.False(File.Exists(Path.Combine(root.StatesPath, "done", $"{second.Id}.ref")));
        Assert.Equal(beforeConfig, File.ReadAllText(root.ConfigPath));
        Assert.Equal(beforeOrder, File.ReadAllText(root.TaskOrderPath));
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation);
    }

    [Fact]
    public async Task PrimaryTaskWriteFailureDoesNotPersistPlannedActivation()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(ActivationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Foundation one", milestone: "foundation"), "done");
        var second = TestData.Task("PM-0002", "Foundation two", milestone: "foundation");
        WriteTask(root, second, "todo");
        Directory.CreateDirectory(Path.Combine(root.StatesPath, "done", $"{second.Id}.ref"));
        var before = File.ReadAllText(root.ConfigPath);
        var service = TestTaskServices.Create(root, new UnusedNextIdService());

        var result = service.MoveTask(second.Id, "done");

        Assert.False(result.Success);
        Assert.Equal("task_state_write_failed", result.ErrorCode);
        Assert.True(File.Exists(Path.Combine(root.StatesPath, "todo", $"{second.Id}.ref")));
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation);
    }

    [Fact]
    public async Task RootScopedFactoryRecordsLinkedStyleConfigurationAndStateMutations()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(ActivationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Foundation one", milestone: "foundation"), "done");
        var second = TestData.Task("PM-0002", "Foundation two", milestone: "foundation");
        WriteTask(root, second, "todo");
        var service = new TaskServiceFactory(TimeProvider.System).Create(root, new UnusedNextIdService());
        using var mutations = FileSystem.TrackMutations(root.RepositoryPath);

        var result = service.MoveTask(second.Id, "done");

        Assert.True(result.Success);
        Assert.Contains(Path.GetRelativePath(root.RepositoryPath, root.ConfigPath)
            .Replace(Path.DirectorySeparatorChar, '/'), mutations.ChangedPaths);
        Assert.Contains($".pm/states/done/{second.Id}.ref", mutations.ChangedPaths);
        Assert.NotNull(ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation);
    }

    private static ProjectConfig ActivationConfig()
    {
        var config = TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["foundation"] = "Foundation",
                ["beta"] = "Beta",
            },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["beta-entry"] = new()
                {
                    Title = "Beta entry",
                    Requirements = [TaskRequirement("PM-0001"), TaskRequirement("PM-0002")],
                },
            });
        config.Milestones["beta"].RequiredActivationTriggers.Add("beta-entry");
        return config;
    }

    private static ActivationRequirement TaskRequirement(string taskId) => new()
    {
        Kind = ActivationRequirementKind.Task,
        Source = taskId,
    };

    private static void WriteTask(ProjectRoot root, TaskItem task, string state)
    {
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FaultingProjectConfigPersistence(ProjectRoot root) : IProjectConfigPersistence
    {
        private readonly ProjectConfigPersistence inner = new(root);
        private bool wrote;

        public bool FailWrite { get; init; }
        public int ReloadFailuresAfterWrite { get; set; }

        public string ReadText() => inner.ReadText();

        public void WriteTextAtomic(string yaml)
        {
            if (FailWrite) throw new IOException("Injected activation write failure.");
            inner.WriteTextAtomic(yaml);
            wrote = true;
        }

        public bool Reload()
        {
            if (wrote && ReloadFailuresAfterWrite > 0)
            {
                ReloadFailuresAfterWrite--;
                return false;
            }

            return inner.Reload();
        }
    }

    private sealed class UnusedNextIdService : INextIdService
    {
        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
