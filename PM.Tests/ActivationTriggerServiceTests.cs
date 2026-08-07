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
            TestTaskServices.Create(root, new RecordingNextIdService()).RemoveTask(task.Id).ErrorCode);
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
    public async Task RedefinitionRevisionIsBoundToTheOwningProject()
    {
        using var workspace = new TempWorkingDirectory();
        var first = await CreateProjectWithIdentity(
            Path.Combine(workspace.Path, "first"), "prj_first", CreateActiveManualTriggerConfig());
        var second = await CreateProjectWithIdentity(
            Path.Combine(workspace.Path, "second"), "prj_second", CreateActiveManualTriggerConfig());
        var firstService = CreateService(first);
        var secondService = CreateService(second);

        var firstPreview = firstService.PreviewRedefinition("entry", []);
        var secondPreview = secondService.PreviewRedefinition("entry", []);

        Assert.True(firstPreview.Success);
        Assert.True(secondPreview.Success);
        Assert.NotEqual(firstPreview.Payload!.Revision, secondPreview.Payload!.Revision);
        var before = File.ReadAllText(second.ConfigPath);

        var crossProject = secondService.RedefineTrigger(
            "entry", [], firstPreview.Payload.Revision, allowDeactivation: false);

        Assert.Equal("activation_trigger_redefine_stale", crossProject.ErrorCode);
        Assert.Equal(before, File.ReadAllText(second.ConfigPath));
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

    [Fact]
    public async Task ManualOnlyActivationAndResetReturnRefreshedTriggerState()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["launch"] = new() { Title = "Launch authorized" },
            }));
        var now = DateTimeOffset.Parse("2026-08-06T14:15:16Z");
        var service = CreateService(root, new FixedTimeProvider(now));

        var reasonRejected = service.ActivateTrigger("launch", " ");
        Assert.Equal("activation_reason_not_allowed", reasonRejected.ErrorCode);
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["launch"].Activation);

        var activated = service.ActivateTrigger("launch", null);

        Assert.True(activated.Success);
        Assert.True(activated.Payload!.IsActive);
        Assert.Equal(ActivationMode.Manual, activated.Payload.Activation!.Mode);
        Assert.Equal(now, activated.Payload.Activation.At);
        Assert.Equal(0, activated.Payload.RequirementCount);
        var stored = ProjectConfig.ReadConfig(root).ActivationTriggers["launch"].Activation;
        Assert.NotNull(stored);
        Assert.Equal(ActivationMode.Manual, stored.Mode);
        Assert.Equal(now, stored.At);
        Assert.Equal("activation_trigger_active", service.ActivateTrigger("launch", null).ErrorCode);

        var reset = service.ResetTrigger("launch");

        Assert.True(reset.Success);
        Assert.False(reset.Payload!.IsActive);
        Assert.Null(reset.Payload.Activation);
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["launch"].Activation);
        Assert.Equal("activation_trigger_inactive", service.ResetTrigger("launch").ErrorCode);
    }

    [Fact]
    public async Task OverrideRequiresReasonAndSnapshotsOnlyCurrentlyUnsatisfiedRequirements()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateOverrideProject(workspace);
        var now = DateTimeOffset.Parse("2026-08-06T15:00:00Z");
        var service = CreateService(root, new FixedTimeProvider(now));
        var before = File.ReadAllText(root.ConfigPath);

        Assert.Equal("override_reason_required", service.ActivateTrigger("beta-entry", null).ErrorCode);
        Assert.Equal("override_reason_required", service.ActivateTrigger("beta-entry", " ").ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));

        var activated = service.ActivateTrigger("beta-entry", "  Approved for beta hardening.  ");

        Assert.True(activated.Success);
        Assert.Equal(ActivationMode.Override, activated.Payload!.Activation!.Mode);
        Assert.Equal(now, activated.Payload.Activation.At);
        Assert.Equal("Approved for beta hardening.", activated.Payload.Activation.Reason);
        Assert.Equal(2, activated.Payload.SatisfiedRequirementCount);
        Assert.Collection(activated.Payload.Activation.WaivedRequirements,
            requirement =>
            {
                Assert.Equal(ActivationRequirementKind.Task, requirement.Kind);
                Assert.Equal("PM-0002", requirement.Source);
            },
            requirement =>
            {
                Assert.Equal(ActivationRequirementKind.Milestone, requirement.Kind);
                Assert.Equal("pending", requirement.Source);
            });

        var stored = ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation!;
        Assert.Equal(ActivationMode.Override, stored.Mode);
        Assert.Equal("Approved for beta hardening.", stored.Reason);
        Assert.Equal(["PM-0002", "pending"], stored.WaivedRequirements.Select(item => item.Source));
    }

    [Fact]
    public async Task SatisfiedInactiveTriggerRequiresReconciliationWithoutWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["entry"] = new()
                {
                    Title = "Entry",
                    Requirements =
                    [
                        new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0001" },
                    ],
                },
            }));
        var task = TestData.Task("PM-0001", "Complete source");
        root.WriteTask(task);
        root.UpdateTaskState(task, "done");
        var service = CreateService(root);
        var before = File.ReadAllText(root.ConfigPath);

        var result = service.ActivateTrigger("entry", "unnecessary override");

        Assert.Equal("activation_reconciliation_required", result.ErrorCode);
        Assert.Contains("pm trigger reconcile", result.Message);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
    }

    [Fact]
    public async Task ResetAllowsLatchedUnmetTriggerButRejectsCurrentlySatisfiedTrigger()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
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
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Source");
        root.WriteTask(task);
        root.UpdateTaskState(task, "done");
        var service = CreateService(root);
        var before = File.ReadAllText(root.ConfigPath);

        var blocked = service.ResetTrigger("entry");

        Assert.Equal("activation_trigger_reset_blocked", blocked.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));

        root.UpdateTaskState(task, "todo");
        var reset = service.ResetTrigger("entry");

        Assert.True(reset.Success);
        Assert.False(reset.Payload!.IsActive);
        Assert.False(reset.Payload.RequirementsSatisfied);
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["entry"].Activation);
    }

    [Fact]
    public async Task OverrideProvenanceRemainsWhenRequirementsLaterBecomeSatisfied()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateOverrideProject(workspace);
        var now = DateTimeOffset.Parse("2026-08-06T15:00:00Z");
        var service = CreateService(root, new FixedTimeProvider(now));
        Assert.True(service.ActivateTrigger("beta-entry", "Accepted risk").Success);

        var task = root.GetAllTasks().Single(item => item.Id == "PM-0002");
        root.UpdateTaskState(task, "done");
        var pendingTask = TestData.Task("PM-0004", "Pending milestone work", milestone: "pending");
        root.WriteTask(pendingTask);
        root.UpdateTaskState(pendingTask, "done");
        root.Config!.Milestones["pending"].Delivery = new MilestoneDelivery
        {
            At = now.AddHours(1),
            Mode = MilestoneDeliveryMode.Ordinary,
        };
        root.Config.WriteConfig(root);

        var resolved = service.ListTriggers().Payload!.Single(trigger => trigger.Key == "beta-entry");

        Assert.True(resolved.RequirementsSatisfied);
        Assert.Equal(ActivationMode.Override, resolved.Activation!.Mode);
        Assert.Equal(now, resolved.Activation.At);
        Assert.Equal("Accepted risk", resolved.Activation.Reason);
        Assert.Equal(["PM-0002", "pending"], resolved.Activation.WaivedRequirements.Select(item => item.Source));
        var beforeReset = File.ReadAllText(root.ConfigPath);
        Assert.Equal("activation_trigger_reset_blocked", service.ResetTrigger("beta-entry").ErrorCode);
        Assert.Equal(beforeReset, File.ReadAllText(root.ConfigPath));
    }

    [Fact]
    public async Task ActivationTransitionRestoresExactConfigurationWhenReloadFails()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["launch"] = new() { Title = "Launch" },
            }));
        var persistence = new FaultingProjectConfigPersistence(root) { ReloadFailuresAfterWrite = 1 };
        var service = CreateService(root, persistence: persistence);
        var before = File.ReadAllText(root.ConfigPath);

        var result = service.ActivateTrigger("launch", null);

        Assert.Equal("activation_trigger_transition_failed", result.ErrorCode);
        Assert.Equal(2, persistence.WriteCount);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(root.Config!.ActivationTriggers["launch"].Activation);
    }

    [Fact]
    public async Task ActivationTransitionReportsRollbackFailureSeparately()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["launch"] = new() { Title = "Launch" },
            }));
        var persistence = new FaultingProjectConfigPersistence(root)
        {
            ReloadFailuresAfterWrite = 1,
            FailRestoreWrite = true,
        };
        var service = CreateService(root, persistence: persistence);

        var result = service.ActivateTrigger("launch", null);

        Assert.Equal("activation_trigger_transition_rollback_failed", result.ErrorCode);
    }

    private static ActivationTriggerService CreateService(
        ProjectRoot root,
        TimeProvider? timeProvider = null,
        IProjectConfigPersistence? persistence = null) =>
        new(
            root,
            new MilestoneActivationResolver(root),
            new MilestoneActivationValidationService(root, new MilestoneActivationGraphService(), new MilestoneActivationResolver(root)),
            new AutomaticActivationService(
                new MilestoneActivationResolver(root), timeProvider ?? TimeProvider.System),
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

    private static async Task<ProjectRoot> CreateOverrideProject(TempWorkingDirectory workspace)
    {
        var config = TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["approved"] = "Approved",
                ["pending"] = "Pending",
            },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["beta-entry"] = new()
                {
                    Title = "Beta entry",
                    Requirements =
                    [
                        new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0001" },
                        new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0002" },
                        new ActivationRequirement { Kind = ActivationRequirementKind.Milestone, Source = "approved" },
                        new ActivationRequirement { Kind = ActivationRequirementKind.Milestone, Source = "pending" },
                    ],
                },
            });
        config.Milestones["approved"].Delivery = new MilestoneDelivery
        {
            At = DateTimeOffset.Parse("2026-08-06T12:00:00Z"),
            Mode = MilestoneDeliveryMode.Ordinary,
        };
        var root = await workspace.CreateProject(config);
        var doneTask = TestData.Task("PM-0001", "Done source");
        var pendingTask = TestData.Task("PM-0002", "Pending source");
        var approvedTask = TestData.Task("PM-0003", "Approved work", milestone: "approved");
        foreach (var task in new[] { doneTask, pendingTask, approvedTask }) root.WriteTask(task);
        root.UpdateTaskState(doneTask, "done");
        root.UpdateTaskState(pendingTask, "todo");
        root.UpdateTaskState(approvedTask, "done");
        return root;
    }

    private static ProjectConfig CreateActiveManualTriggerConfig() => TestData.Config(
        activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
        {
            ["entry"] = new()
            {
                Title = "Entry",
                Activation = new ActivationRecord
                {
                    At = DateTimeOffset.Parse("2026-08-06T08:00:00Z"),
                    Mode = ActivationMode.Manual,
                },
            },
        });

    private static async Task<ProjectRoot> CreateProjectWithIdentity(
        string repositoryPath,
        string projectId,
        ProjectConfig config)
    {
        Directory.CreateDirectory(repositoryPath);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repositoryPath;
        try
        {
            var root = new ProjectRoot();
            await root.CreateProject(config);
            await File.WriteAllTextAsync(
                Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");
            return root;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
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
