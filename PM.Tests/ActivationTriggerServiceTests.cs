using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public class ActivationTriggerServiceTests
{
    [Fact]
    public async Task ManagesDefinitionsAndReportsAffectedMilestones()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["alpha"] = "Alpha",
                ["beta"] = "Beta",
            }));
        var task = TestData.Task("PM-0001", "Foundation");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var service = CreateService(root);

        var added = service.AddTrigger("entry", "Entry",
            [new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = task.Id }]);
        Assert.True(added.Success);
        Assert.Empty(added.Payload!.AffectedMilestones);

        Assert.True(service.AttachTrigger("entry", "beta").Success);
        var renamed = service.RenameTrigger("entry", "Beta entry");
        Assert.True(renamed.Success);
        Assert.Equal(["beta"], renamed.Payload!.AffectedMilestones);

        var replaced = service.SetRequirements("entry",
            [new ActivationRequirement { Kind = ActivationRequirementKind.Milestone, Source = "alpha" }]);
        Assert.True(replaced.Success);
        Assert.Equal(["beta"], replaced.Payload!.AffectedMilestones);

        Assert.True(service.SetRequirements("entry", []).Success);
        var listed = service.ListTriggers();
        Assert.True(listed.Success);
        var trigger = Assert.Single(listed.Payload!);
        Assert.Equal("Beta entry", trigger.Title);
        Assert.Empty(trigger.Requirements);
        Assert.Equal(["beta"], trigger.ConsumingMilestones);

        Assert.True(service.DetachTrigger("entry", "beta").Success);
        Assert.True(service.RemoveTrigger("entry").Success);
        Assert.Empty(ProjectConfig.ReadConfig(root).ActivationTriggers);
    }

    [Fact]
    public async Task ProspectiveValidationRejectsMissingDuplicateAndCyclicRequirementsBeforeWrite()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["foundation"] = "Foundation" }));
        var task = TestData.Task("PM-0001", "Foundation", milestone: "foundation");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var service = CreateService(root);

        var missing = service.AddTrigger("missing", "Missing",
            [new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-9999" }]);
        Assert.Equal("unknown_activation_task", missing.ErrorCode);

        var duplicate = service.AddTrigger("duplicate", "Duplicate",
        [
            new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = task.Id },
            new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = task.Id },
        ]);
        Assert.Equal("duplicate_activation_requirement", duplicate.ErrorCode);

        Assert.True(service.AddTrigger("entry", "Entry",
            [new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = task.Id }]).Success);
        var before = File.ReadAllText(root.ConfigPath);

        var cycle = service.AttachTrigger("entry", "foundation");

        Assert.Equal("activation_cycle", cycle.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Empty(root.Config!.Milestones["foundation"].RequiredActivationTriggers);
    }

    [Fact]
    public async Task ActiveRequirementsCannotBeEditedButUnusedActiveTriggerCanBeRemoved()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["manual"] = new()
                {
                    Title = "Manual",
                    Activation = new ActivationRecord
                    {
                        At = DateTimeOffset.Parse("2026-08-06T08:00:00Z"),
                        Mode = ActivationMode.Manual,
                    },
                },
            }));
        var service = CreateService(root);

        var update = service.SetRequirements("manual", []);

        Assert.Equal("activation_trigger_active", update.ErrorCode);
        Assert.True(service.RemoveTrigger("manual").Success);
        Assert.Empty(ProjectConfig.ReadConfig(root).ActivationTriggers);
    }

    [Fact]
    public async Task AttachAndDetachUseStrictRelationshipsAndActiveGateIsImmediatelySatisfied()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["beta"] = "Beta" },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["active"] = new()
                {
                    Title = "Active",
                    Activation = new ActivationRecord
                    {
                        At = DateTimeOffset.Parse("2026-08-06T08:00:00Z"),
                        Mode = ActivationMode.Manual,
                    },
                },
                ["pending"] = new()
                {
                    Title = "Pending",
                    Requirements =
                    [
                        new ActivationRequirement
                            { Kind = ActivationRequirementKind.Task, Source = "PM-0001" },
                    ],
                },
            }));
        var task = TestData.Task("PM-0001", "Pending source");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        var service = CreateService(root);

        var attached = service.AttachTrigger("active", "beta");
        Assert.True(attached.Success);
        Assert.Equal(["beta"], attached.Payload!.AffectedMilestones);
        Assert.Equal("activation_trigger_already_attached", service.AttachTrigger("active", "beta").ErrorCode);

        var beta = Assert.Single(new MilestoneActivationResolver(root).ResolveCurrentProject().Payload!.Milestones);
        Assert.Empty(beta.UnmetActivationTriggers);
        Assert.NotEqual(MilestoneLifecycle.Inactive, beta.Lifecycle);

        Assert.True(service.DetachTrigger("active", "beta").Success);
        Assert.Equal("activation_trigger_not_attached", service.DetachTrigger("active", "beta").ErrorCode);

        Assert.True(service.AttachTrigger("pending", "beta").Success);
        beta = Assert.Single(new MilestoneActivationResolver(root).ResolveCurrentProject().Payload!.Milestones);
        Assert.Equal(MilestoneLifecycle.Inactive, beta.Lifecycle);
        Assert.Equal(["pending"], beta.UnmetActivationTriggers);
    }

    [Fact]
    public async Task RemovingConsumedTriggerOrRequirementSourcesIsRejected()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["source"] = "Source",
                ["consumer"] = "Consumer",
            },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["entry"] = new()
                {
                    Title = "Entry",
                    Requirements =
                    [
                        new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0001" },
                        new ActivationRequirement { Kind = ActivationRequirementKind.Milestone, Source = "source" },
                    ],
                },
            }));
        root.Config!.Milestones["consumer"].RequiredActivationTriggers.Add("entry");
        root.Config.WriteConfig(root);
        var task = TestData.Task("PM-0001", "Required task");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");

        Assert.Equal("activation_trigger_in_use", CreateService(root).RemoveTrigger("entry").ErrorCode);
        Assert.Equal("activation_requirement_in_use",
            new TaskService(root, new RecordingNextIdService()).RemoveTask(task.Id).ErrorCode);
        Assert.Equal("activation_requirement_in_use",
            new ProjectConfigService(root).RemoveMilestone("source").ErrorCode);
        Assert.True(File.Exists(Path.Combine(root.TasksPath, $"{task.Id}.md")));
        Assert.True(ProjectConfig.ReadConfig(root).Milestones.ContainsKey("source"));
    }

    [Fact]
    public async Task RedefinitionPreviewReportsEligibilityLossAndPendingMutationRequiresApproval()
    {
        using var workspace = new TempWorkingDirectory();
        var (root, newRequirement, _) = await CreateRedefinitionProject(workspace, newRequirementDone: false);
        var before = File.ReadAllText(root.ConfigPath);
        var service = CreateService(root, new FixedTimeProvider(DateTimeOffset.Parse("2026-08-06T12:00:00Z")));
        ActivationRequirement[] requirements =
            [new() { Kind = ActivationRequirementKind.Task, Source = newRequirement.Id }];

        var preview = service.PreviewRedefinition("entry", requirements);

        Assert.True(preview.Success);
        Assert.False(preview.Payload!.WillReactivateAutomatically);
        Assert.True(preview.Payload.RequiresConfirmation);
        Assert.Equal(["PM-0003"], preview.Payload.CurrentlyEligibleTaskIds);
        Assert.Equal(["PM-0003"], preview.Payload.TaskIdsLosingEligibility);
        var impact = Assert.Single(preview.Payload.Milestones);
        Assert.Equal(MilestoneLifecycle.Active, impact.Before);
        Assert.Equal(MilestoneLifecycle.Inactive, impact.After);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));

        var unconfirmed = service.RedefineTrigger(
            "entry", requirements, preview.Payload.Revision, allowDeactivation: false);
        Assert.Equal("activation_trigger_redefine_confirmation_required", unconfirmed.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));

        var changed = service.RedefineTrigger(
            "entry", requirements, preview.Payload.Revision, allowDeactivation: true);
        Assert.True(changed.Success);
        Assert.False(changed.Payload!.IsActive);
        var stored = ProjectConfig.ReadConfig(root).ActivationTriggers["entry"];
        Assert.Null(stored.Activation);
        Assert.Equal(newRequirement.Id, Assert.Single(stored.Requirements).Source);
    }

    [Fact]
    public async Task SatisfiedRedefinitionCreatesFreshAutomaticActivationWithoutConfirmation()
    {
        using var workspace = new TempWorkingDirectory();
        var (root, newRequirement, _) = await CreateRedefinitionProject(workspace, newRequirementDone: true);
        var now = DateTimeOffset.Parse("2026-08-06T12:34:56Z");
        var service = CreateService(root, new FixedTimeProvider(now));
        ActivationRequirement[] requirements =
            [new() { Kind = ActivationRequirementKind.Task, Source = newRequirement.Id }];

        var preview = service.PreviewRedefinition("entry", requirements);
        Assert.True(preview.Success);
        Assert.True(preview.Payload!.WillReactivateAutomatically);
        Assert.False(preview.Payload.RequiresConfirmation);
        Assert.All(preview.Payload.Milestones, impact => Assert.Equal(impact.Before, impact.After));

        var changed = service.RedefineTrigger(
            "entry", requirements, preview.Payload.Revision, allowDeactivation: false);

        Assert.True(changed.Success);
        Assert.True(changed.Payload!.IsActive);
        Assert.Equal(ActivationMode.Automatic, changed.Payload.ActivationMode);
        Assert.Equal(now, changed.Payload.ActivatedAt);
        var activation = ProjectConfig.ReadConfig(root).ActivationTriggers["entry"].Activation;
        Assert.NotNull(activation);
        Assert.Equal(ActivationMode.Automatic, activation.Mode);
        Assert.Equal(now, activation.At);
    }

    [Fact]
    public async Task RedefinitionRejectsInactiveTriggersAndStalePreviewsWithoutWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var (root, newRequirement, _) = await CreateRedefinitionProject(workspace, newRequirementDone: false);
        var service = CreateService(root);
        ActivationRequirement[] requirements =
            [new() { Kind = ActivationRequirementKind.Task, Source = newRequirement.Id }];
        var preview = service.PreviewRedefinition("entry", requirements);
        Assert.True(preview.Success);
        var before = File.ReadAllText(root.ConfigPath);

        var changedProposal = service.RedefineTrigger(
            "entry", [], preview.Payload!.Revision, allowDeactivation: true);
        Assert.Equal("activation_trigger_redefine_stale", changedProposal.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));

        root.UpdateTaskState(newRequirement, "done");
        var stale = service.RedefineTrigger(
            "entry", requirements, preview.Payload!.Revision, allowDeactivation: true);

        Assert.Equal("activation_trigger_redefine_stale", stale.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));

        root.Config!.ActivationTriggers["entry"].Activation = null;
        root.Config.WriteConfig(root);
        Assert.Equal("activation_trigger_inactive",
            service.PreviewRedefinition("entry", requirements).ErrorCode);
    }

    [Fact]
    public async Task RedefinitionValidatesReferencesAndActivationCyclesBeforePreview()
    {
        using var workspace = new TempWorkingDirectory();
        var (root, _, eligibleTask) = await CreateRedefinitionProject(workspace, newRequirementDone: false);
        var service = CreateService(root);
        var before = File.ReadAllText(root.ConfigPath);

        var missing = service.PreviewRedefinition("entry",
            [new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-9999" }]);
        var cycle = service.PreviewRedefinition("entry",
            [new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = eligibleTask.Id }]);

        Assert.Equal("unknown_activation_task", missing.ErrorCode);
        Assert.Equal("activation_cycle", cycle.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
    }

    [Fact]
    public async Task RedefinitionRestoresExactConfigurationWhenReloadFails()
    {
        using var workspace = new TempWorkingDirectory();
        var (root, newRequirement, _) = await CreateRedefinitionProject(workspace, newRequirementDone: false);
        var persistence = new FaultingProjectConfigPersistence(root) { ReloadFailuresAfterWrite = 1 };
        var service = CreateService(root, persistence: persistence);
        ActivationRequirement[] requirements =
            [new() { Kind = ActivationRequirementKind.Task, Source = newRequirement.Id }];
        var preview = service.PreviewRedefinition("entry", requirements);
        var before = File.ReadAllText(root.ConfigPath);
        var oldActivation = root.Config!.ActivationTriggers["entry"].Activation;

        var result = service.RedefineTrigger(
            "entry", requirements, preview.Payload!.Revision, allowDeactivation: true);

        Assert.Equal("activation_trigger_redefine_failed", result.ErrorCode);
        Assert.Equal(2, persistence.WriteCount);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        var restored = root.Config.ActivationTriggers["entry"];
        Assert.NotNull(restored.Activation);
        Assert.Equal(oldActivation!.At, restored.Activation.At);
        Assert.Equal(oldActivation.Mode, restored.Activation.Mode);
        Assert.Equal("PM-0001", Assert.Single(restored.Requirements).Source);
    }

    [Fact]
    public async Task RedefinitionReportsRollbackFailureSeparately()
    {
        using var workspace = new TempWorkingDirectory();
        var (root, newRequirement, _) = await CreateRedefinitionProject(workspace, newRequirementDone: false);
        var persistence = new FaultingProjectConfigPersistence(root)
        {
            ReloadFailuresAfterWrite = 1,
            FailRestoreWrite = true,
        };
        var service = CreateService(root, persistence: persistence);
        ActivationRequirement[] requirements =
            [new() { Kind = ActivationRequirementKind.Task, Source = newRequirement.Id }];
        var preview = service.PreviewRedefinition("entry", requirements);

        var result = service.RedefineTrigger(
            "entry", requirements, preview.Payload!.Revision, allowDeactivation: true);

        Assert.Equal("activation_trigger_redefine_rollback_failed", result.ErrorCode);
    }

    private static ActivationTriggerService CreateService(
        ProjectRoot root,
        TimeProvider? timeProvider = null,
        IProjectConfigPersistence? persistence = null) =>
        new(
            root,
            new MilestoneActivationResolver(root),
            new MilestoneActivationValidationService(root),
            timeProvider ?? TimeProvider.System,
            persistence ?? new ProjectConfigPersistence(root));

    private static async Task<(ProjectRoot Root, TaskItem NewRequirement, TaskItem EligibleTask)>
        CreateRedefinitionProject(TempWorkingDirectory workspace, bool newRequirementDone)
    {
        var config = TestData.Config(
            milestones: new Dictionary<string, string> { ["beta"] = "Beta" },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["entry"] = new()
                {
                    Title = "Entry",
                    Requirements =
                    [
                        new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0001" },
                    ],
                    Activation = new ActivationRecord
                    {
                        At = DateTimeOffset.Parse("2026-08-06T08:00:00Z"),
                        Mode = ActivationMode.Automatic,
                    },
                },
            });
        config.Milestones["beta"].RequiredActivationTriggers.Add("entry");
        var root = await workspace.CreateProject(config);
        var oldRequirement = TestData.Task("PM-0001", "Old requirement");
        var newRequirement = TestData.Task("PM-0002", "New requirement");
        var eligibleTask = TestData.Task("PM-0003", "Eligible beta work", milestone: "beta");
        foreach (var task in new[] { oldRequirement, newRequirement, eligibleTask }) root.WriteTask(task);
        root.UpdateTaskState(oldRequirement, "done");
        root.UpdateTaskState(newRequirement, newRequirementDone ? "done" : "todo");
        root.UpdateTaskState(eligibleTask, "todo");
        return (root, newRequirement, eligibleTask);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FaultingProjectConfigPersistence(ProjectRoot root) : IProjectConfigPersistence
    {
        private readonly ProjectConfigPersistence inner = new(root);

        public int ReloadFailuresAfterWrite { get; init; }
        public bool FailRestoreWrite { get; init; }
        public int WriteCount { get; private set; }
        private int reloadFailures;

        public string ReadText() => inner.ReadText();

        public void WriteTextAtomic(string yaml)
        {
            WriteCount++;
            if (FailRestoreWrite && WriteCount == 2) throw new IOException("Restore failed.");
            inner.WriteTextAtomic(yaml);
        }

        public bool Reload()
        {
            if (WriteCount > 0 && reloadFailures < ReloadFailuresAfterWrite)
            {
                reloadFailures++;
                return false;
            }

            return inner.Reload();
        }
    }

    private sealed class RecordingNextIdService : INextIdService
    {
        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);

        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectRegistration("project-test", "recovery-test"));

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
