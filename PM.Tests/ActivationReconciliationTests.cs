using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class ActivationReconciliationTests
{
    [Fact]
    public async Task ReconcileLatchesEverySatisfiedPendingTriggerAndPreservesExistingProvenance()
    {
        using var workspace = new TempWorkingDirectory();
        var existingAt = DateTimeOffset.Parse("2026-08-06T10:00:00Z");
        var config = TaskReconciliationConfig();
        config.ActivationTriggers["manual-only"] = new ActivationTriggerDefinition
        {
            Title = "Manual authorization",
            Activation = new ActivationRecord { At = existingAt, Mode = ActivationMode.Manual },
        };
        config.ActivationTriggers["existing-override"] = new ActivationTriggerDefinition
        {
            Title = "Existing override",
            Requirements = [TaskRequirement("PM-0001"), TaskRequirement("PM-0003")],
            Activation = new ActivationRecord
            {
                At = existingAt,
                Mode = ActivationMode.Override,
                Reason = "Approved before the final import.",
                WaivedRequirements = [TaskRequirement("PM-0003")],
            },
        };
        config.ActivationTriggers["partial"] = new ActivationTriggerDefinition
        {
            Title = "Still pending",
            Requirements = [TaskRequirement("PM-0001"), TaskRequirement("PM-0004")],
        };
        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Imported one", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Imported two", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0003", "Imported three", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0004", "Still open", milestone: "foundation"), "todo");
        WriteTask(root, TestData.Task("PM-0005", "Beta work", milestone: "beta"), "todo");
        var reconciledAt = DateTimeOffset.Parse("2026-08-06T20:00:00Z");
        var service = CreateService(root, new FixedTimeProvider(reconciledAt));

        var result = service.Reconcile(dryRun: false);

        Assert.True(result.Success);
        Assert.False(result.Payload!.DryRun);
        var trigger = Assert.Single(result.Payload.ActivationImpact.ActivatedTriggers);
        Assert.Equal("beta-entry", trigger.Key);
        Assert.Equal(ActivationMode.Automatic, trigger.Activation!.Mode);
        Assert.Equal(reconciledAt, trigger.Activation.At);
        var beta = Assert.Single(result.Payload.ActivationImpact.MilestoneChanges);
        Assert.Equal("beta", beta.MilestoneKey);
        Assert.Equal(MilestoneLifecycle.Inactive, beta.Before);
        Assert.Equal(MilestoneLifecycle.Active, beta.After);

        var stored = ProjectConfig.ReadConfig(root).ActivationTriggers;
        Assert.Equal(reconciledAt, stored["beta-entry"].Activation!.At);
        Assert.Equal(existingAt, stored["manual-only"].Activation!.At);
        Assert.Equal(ActivationMode.Manual, stored["manual-only"].Activation!.Mode);
        Assert.Equal(existingAt, stored["existing-override"].Activation!.At);
        Assert.Equal("Approved before the final import.", stored["existing-override"].Activation!.Reason);
        Assert.Equal(["PM-0003"],
            stored["existing-override"].Activation!.WaivedRequirements.Select(item => item.Source));
        Assert.Null(stored["partial"].Activation);
    }

    [Fact]
    public async Task DryRunReportsExactProspectiveChangesWithoutWritingAndRealRunIsIdempotent()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TaskReconciliationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Imported one", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Imported two", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0005", "Beta work", milestone: "beta"), "todo");
        var before = File.ReadAllText(root.ConfigPath);
        var reconciledAt = DateTimeOffset.Parse("2026-08-06T20:30:00Z");
        var persistence = new RecordingProjectConfigPersistence(root);
        var service = CreateService(root, new FixedTimeProvider(reconciledAt), persistence);

        var preview = service.Reconcile(dryRun: true);

        Assert.True(preview.Success);
        Assert.True(preview.Payload!.DryRun);
        Assert.Equal(reconciledAt,
            Assert.Single(preview.Payload.ActivationImpact.ActivatedTriggers).Activation!.At);
        Assert.Equal(0, persistence.WriteCalls);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(root.Config!.ActivationTriggers["beta-entry"].Activation);

        var applied = service.Reconcile(dryRun: false);
        var activation = ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation!;
        var second = service.Reconcile(dryRun: false);

        Assert.True(applied.Success);
        Assert.Equal(1, persistence.WriteCalls);
        Assert.Equal(reconciledAt, activation.At);
        Assert.True(second.Success);
        Assert.Empty(second.Payload!.ActivationImpact.ActivatedTriggers);
        Assert.Equal(1, persistence.WriteCalls);
        Assert.Equal(activation.At,
            ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation!.At);
    }

    [Fact]
    public async Task ReconcileRecognizesImportedMilestoneDeliveryRequirements()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["foundation"] = "Foundation",
                ["beta"] = "Beta",
            },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["foundation-delivered"] = new()
                {
                    Title = "Foundation delivered",
                    Requirements =
                    [
                        new ActivationRequirement
                        {
                            Kind = ActivationRequirementKind.Milestone,
                            Source = "foundation",
                        },
                    ],
                },
            });
        config.Milestones["foundation"].Delivery = new MilestoneDelivery
        {
            At = DateTimeOffset.Parse("2026-08-06T12:00:00Z"),
            Mode = MilestoneDeliveryMode.Ordinary,
        };
        config.Milestones["beta"].RequiredActivationTriggers.Add("foundation-delivered");
        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Imported foundation", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Beta work", milestone: "beta"), "todo");

        var result = CreateService(root).Reconcile(dryRun: false);

        Assert.True(result.Success);
        Assert.Equal("foundation-delivered",
            Assert.Single(result.Payload!.ActivationImpact.ActivatedTriggers).Key);
        Assert.NotNull(ProjectConfig.ReadConfig(root)
            .ActivationTriggers["foundation-delivered"].Activation);
    }

    [Fact]
    public async Task WriteFailureLeavesMissingActivationAndExactConfigurationUntouched()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TaskReconciliationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Imported one", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Imported two", milestone: "foundation"), "done");
        var before = File.ReadAllText(root.ConfigPath);
        var persistence = new RecordingProjectConfigPersistence(root) { FailWrite = true };

        var result = CreateService(root, persistence: persistence).Reconcile(dryRun: false);

        Assert.False(result.Success);
        Assert.Equal("activation_reconciliation_write_failed", result.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation);
    }

    [Fact]
    public async Task ReloadFailureRestoresExactConfigurationAndMissingProvenance()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TaskReconciliationConfig());
        WriteTask(root, TestData.Task("PM-0001", "Imported one", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Imported two", milestone: "foundation"), "done");
        var before = File.ReadAllText(root.ConfigPath);
        var persistence = new RecordingProjectConfigPersistence(root) { ReloadFailuresAfterWrite = 1 };

        var result = CreateService(root, persistence: persistence).Reconcile(dryRun: false);

        Assert.False(result.Success);
        Assert.Equal("activation_reconciliation_write_failed", result.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["beta-entry"].Activation);
    }

    private static ProjectConfig TaskReconciliationConfig()
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

    private static ActivationTriggerService CreateService(
        ProjectRoot root,
        TimeProvider? timeProvider = null,
        IProjectConfigPersistence? persistence = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var resolver = new MilestoneActivationResolver(root);
        return new ActivationTriggerService(
            root,
            resolver,
            new MilestoneActivationValidationService(
                root, new MilestoneActivationGraphService(), resolver),
            new AutomaticActivationService(resolver, clock),
            clock,
            persistence ?? new ProjectConfigPersistence(root));
    }

    private static void WriteTask(ProjectRoot root, TaskItem task, string state)
    {
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingProjectConfigPersistence(ProjectRoot root) : IProjectConfigPersistence
    {
        private readonly ProjectConfigPersistence inner = new(root);
        private bool wrote;

        public bool FailWrite { get; init; }
        public int ReloadFailuresAfterWrite { get; set; }
        public int WriteCalls { get; private set; }

        public string ReadText() => inner.ReadText();

        public void WriteTextAtomic(string yaml)
        {
            WriteCalls++;
            if (FailWrite) throw new IOException("Injected reconciliation write failure.");
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
}
