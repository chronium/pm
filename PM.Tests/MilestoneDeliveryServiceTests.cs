using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class MilestoneDeliveryServiceTests
{
    [Fact]
    public async Task OrdinaryDeliveryRequiresCompletedAssignedTasksAndReturnsFreshState()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["release"] = "Release" }));
        WriteTask(root, TestData.Task("PM-0001", "First", milestone: "release"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Second", milestone: "release"), "done");
        var now = DateTimeOffset.Parse("2026-08-06T16:00:00Z");
        var service = CreateService(root, new FixedTimeProvider(now));

        Assert.Equal("milestone_delivery_reason_not_allowed",
            service.PreviewDelivery("release", "not exceptional").ErrorCode);
        var preview = service.PreviewDelivery("release", null);

        Assert.True(preview.Success);
        Assert.Equal(MilestoneDeliveryMode.Ordinary, preview.Payload!.Mode);
        Assert.False(preview.Payload.RequiresConfirmation);
        Assert.Equal(2, preview.Payload.DoneTaskCount);
        Assert.Empty(preview.Payload.UnfinishedTaskIds);
        Assert.Equal("milestone_delivery_revision_required",
            service.DeliverMilestone("release", null, string.Empty, false).ErrorCode);

        var delivered = service.DeliverMilestone(
            "release", null, preview.Payload.Revision, allowExceptional: false);

        Assert.True(delivered.Success);
        var deliveredMilestone = delivered.Payload!.Value;
        Assert.Equal(MilestoneLifecycle.Delivered, deliveredMilestone.Lifecycle);
        Assert.Equal(MilestoneDeliveryMode.Ordinary, deliveredMilestone.Delivery!.Mode);
        Assert.Equal(now, deliveredMilestone.Delivery.At);
        Assert.Null(deliveredMilestone.Delivery.Reason);
        Assert.Empty(deliveredMilestone.Delivery.AcceptedTaskIds);
        var stored = ProjectConfig.ReadConfig(root).Milestones["release"].Delivery!;
        Assert.Equal(now, stored.At);
        Assert.Equal(MilestoneDeliveryMode.Ordinary, stored.Mode);
        var storedYaml = File.ReadAllText(root.ConfigPath);
        Assert.DoesNotContain("reason:", storedYaml, StringComparison.Ordinal);
        Assert.DoesNotContain("actor:", storedYaml, StringComparison.OrdinalIgnoreCase);
        Assert.All(storedYaml.Split('\n'), line =>
            Assert.False(line.EndsWith(' '), $"Serialized config line has trailing whitespace: '{line}'"));
        Assert.Equal("milestone_already_delivered", service.PreviewDelivery("release", null).ErrorCode);
    }

    [Fact]
    public async Task ExceptionalDeliveryRequiresConfirmationAndSnapshotsUnfinishedTasksInIdOrder()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["beta"] = "Beta" }));
        WriteTask(root, TestData.Task("PM-0003", "Third", milestone: "beta"), "in-progress");
        WriteTask(root, TestData.Task("PM-0001", "First", milestone: "beta"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Second", milestone: "beta"), "todo");
        var now = DateTimeOffset.Parse("2026-08-06T16:30:00Z");
        var service = CreateService(root, new FixedTimeProvider(now));
        var before = File.ReadAllText(root.ConfigPath);

        Assert.Equal("exceptional_delivery_reason_required",
            service.PreviewDelivery("beta", null).ErrorCode);
        Assert.Equal("exceptional_delivery_reason_required",
            service.PreviewDelivery("beta", " ").ErrorCode);
        var preview = service.PreviewDelivery("beta", "  Accepted for hardening.  ");
        Assert.True(preview.Success);
        Assert.Equal(MilestoneDeliveryMode.Exceptional, preview.Payload!.Mode);
        Assert.True(preview.Payload.RequiresConfirmation);
        Assert.Equal(["PM-0002", "PM-0003"], preview.Payload.UnfinishedTaskIds);

        var unconfirmed = service.DeliverMilestone(
            "beta", "  Accepted for hardening.  ", preview.Payload.Revision, allowExceptional: false);
        Assert.Equal("milestone_delivery_confirmation_required", unconfirmed.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));

        var delivered = service.DeliverMilestone(
            "beta", "  Accepted for hardening.  ", preview.Payload.Revision, allowExceptional: true);

        Assert.True(delivered.Success);
        var deliveredMilestone = delivered.Payload!.Value;
        Assert.Equal(MilestoneLifecycle.Delivered, deliveredMilestone.Lifecycle);
        Assert.Equal(MilestoneDeliveryMode.Exceptional, deliveredMilestone.Delivery!.Mode);
        Assert.Equal(now, deliveredMilestone.Delivery.At);
        Assert.Equal("Accepted for hardening.", deliveredMilestone.Delivery.Reason);
        Assert.Equal(["PM-0002", "PM-0003"], deliveredMilestone.Delivery.AcceptedTaskIds);
        var stored = ProjectConfig.ReadConfig(root).Milestones["beta"].Delivery!;
        Assert.Equal(["PM-0002", "PM-0003"], stored.AcceptedTaskIds);
        Assert.Contains("reason: Accepted for hardening.", File.ReadAllText(root.ConfigPath));
    }

    [Fact]
    public async Task DeliveryRejectsInactiveAndEmptyMilestones()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["inactive"] = "Inactive",
                ["empty"] = "Empty",
            },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["approval"] = new() { Title = "Approval" },
            });
        config.Milestones["inactive"].RequiredActivationTriggers.Add("approval");
        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Inactive work", milestone: "inactive"), "todo");
        var service = CreateService(root);
        var before = File.ReadAllText(root.ConfigPath);

        Assert.Equal("milestone_delivery_inactive",
            service.PreviewDelivery("inactive", "Override is not allowed").ErrorCode);
        Assert.Equal("empty_milestone_delivery", service.PreviewDelivery("empty", null).ErrorCode);
        Assert.Equal("missing_milestone", service.PreviewDelivery("missing", null).ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
    }

    [Fact]
    public async Task DeliveryRejectsStalePreviewWithoutWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["beta"] = "Beta" }));
        WriteTask(root, TestData.Task("PM-0001", "Open", milestone: "beta"), "todo");
        var service = CreateService(root);
        var preview = service.PreviewDelivery("beta", "Accepted");
        Assert.True(preview.Success);

        root.Config!.Milestones["beta"].Title = "Renamed beta";
        root.Config.WriteConfig(root);
        var before = File.ReadAllText(root.ConfigPath);

        var result = service.DeliverMilestone(
            "beta", "Accepted", preview.Payload!.Revision, allowExceptional: true);

        Assert.Equal("milestone_delivery_stale", result.ErrorCode);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(ProjectConfig.ReadConfig(root).Milestones["beta"].Delivery);
    }

    [Fact]
    public async Task DeliveryRevisionIsBoundToTheOwningProject()
    {
        using var workspace = new TempWorkingDirectory();
        var first = await CreateDeliveryProjectWithIdentity(
            Path.Combine(workspace.Path, "first"), "prj_first");
        var second = await CreateDeliveryProjectWithIdentity(
            Path.Combine(workspace.Path, "second"), "prj_second");
        var firstService = CreateService(first);
        var secondService = CreateService(second);

        var firstPreview = firstService.PreviewDelivery("release", null);
        var secondPreview = secondService.PreviewDelivery("release", null);

        Assert.True(firstPreview.Success);
        Assert.True(secondPreview.Success);
        Assert.NotEqual(firstPreview.Payload!.Revision, secondPreview.Payload!.Revision);
        var before = File.ReadAllText(second.ConfigPath);

        var crossProject = secondService.DeliverMilestone(
            "release", null, firstPreview.Payload.Revision, allowExceptional: false);

        Assert.Equal("milestone_delivery_stale", crossProject.ErrorCode);
        Assert.Equal(before, File.ReadAllText(second.ConfigPath));
        Assert.Null(ProjectConfig.ReadConfig(second).Milestones["release"].Delivery);
    }

    [Fact]
    public async Task DeliveredMilestoneDominatesResetGateUntilExplicitlyReopened()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new Dictionary<string, string> { ["release"] = "Release" },
            activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
            {
                ["approval"] = new()
                {
                    Title = "Approval",
                    Activation = new ActivationRecord
                    {
                        At = DateTimeOffset.Parse("2026-08-06T15:00:00Z"),
                        Mode = ActivationMode.Manual,
                    },
                },
            });
        config.Milestones["release"].RequiredActivationTriggers.Add("approval");
        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Complete", milestone: "release"), "done");
        var service = CreateService(root);
        var preview = service.PreviewDelivery("release", null);
        Assert.True(service.DeliverMilestone(
            "release", null, preview.Payload!.Revision, allowExceptional: false).Success);

        var triggers = new ActivationTriggerService(
            root,
            new MilestoneActivationResolver(root),
            new MilestoneActivationValidationService(root, new MilestoneActivationGraphService(), new MilestoneActivationResolver(root)),
            new AutomaticActivationService(new MilestoneActivationResolver(root), TimeProvider.System),
            TimeProvider.System,
            new ProjectConfigPersistence(root));
        Assert.True(triggers.ResetTrigger("approval").Success);
        var delivered = new MilestoneActivationResolver(root).ResolveCurrentProject().Payload!.Milestones.Single();
        Assert.Equal(MilestoneLifecycle.Delivered, delivered.Lifecycle);

        var reopened = service.ReopenMilestone("release");

        Assert.True(reopened.Success);
        Assert.Null(reopened.Payload!.Delivery);
        Assert.Equal(MilestoneLifecycle.Inactive, reopened.Payload.Lifecycle);
        Assert.Equal(["approval"], reopened.Payload.UnmetActivationTriggers);
        Assert.Null(ProjectConfig.ReadConfig(root).Milestones["release"].Delivery);
        Assert.Null(ProjectConfig.ReadConfig(root).ActivationTriggers["approval"].Activation);
        Assert.Equal("milestone_not_delivered", service.ReopenMilestone("release").ErrorCode);
    }

    [Fact]
    public async Task DeliveryLatchesAffectedMilestoneRequirementsInTheSameTransition()
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
        config.Milestones["beta"].RequiredActivationTriggers.Add("foundation-delivered");
        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Foundation work", milestone: "foundation"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Beta work", milestone: "beta"), "todo");
        var now = DateTimeOffset.Parse("2026-08-06T19:00:00Z");
        var service = CreateService(root, new FixedTimeProvider(now));
        var preview = service.PreviewDelivery("foundation", null);

        var result = service.DeliverMilestone(
            "foundation", null, preview.Payload!.Revision, allowExceptional: false);

        Assert.True(result.Success);
        var impact = result.Payload!.ActivationImpact;
        var trigger = Assert.Single(impact.ActivatedTriggers);
        Assert.Equal("foundation-delivered", trigger.Key);
        Assert.Equal(ActivationMode.Automatic, trigger.Activation!.Mode);
        Assert.Equal(now, trigger.Activation.At);
        var milestone = impact.MilestoneChanges.Single(change => change.MilestoneKey == "beta");
        Assert.Equal("beta", milestone.MilestoneKey);
        Assert.Equal(MilestoneLifecycle.Inactive, milestone.Before);
        Assert.Equal(MilestoneLifecycle.Active, milestone.After);

        var stored = ProjectConfig.ReadConfig(root);
        Assert.Equal(now, stored.Milestones["foundation"].Delivery!.At);
        Assert.Equal(now, stored.ActivationTriggers["foundation-delivered"].Activation!.At);
    }

    [Fact]
    public async Task DeliveryRestoresExactConfigurationWhenReloadFails()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["release"] = "Release" }));
        WriteTask(root, TestData.Task("PM-0001", "Complete", milestone: "release"), "done");
        var persistence = new FaultingProjectConfigPersistence(root) { ReloadFailuresAfterWrite = 1 };
        var service = CreateService(root, persistence: persistence);
        var preview = service.PreviewDelivery("release", null);
        var before = File.ReadAllText(root.ConfigPath);

        var result = service.DeliverMilestone(
            "release", null, preview.Payload!.Revision, allowExceptional: false);

        Assert.Equal("milestone_delivery_failed", result.ErrorCode);
        Assert.Equal(2, persistence.WriteCount);
        Assert.Equal(before, File.ReadAllText(root.ConfigPath));
        Assert.Null(root.Config!.Milestones["release"].Delivery);
    }

    [Fact]
    public async Task ReopenReportsRollbackFailureSeparately()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new Dictionary<string, string> { ["release"] = "Release" });
        config.Milestones["release"].Delivery = new MilestoneDelivery
        {
            At = DateTimeOffset.Parse("2026-08-06T15:00:00Z"),
            Mode = MilestoneDeliveryMode.Ordinary,
        };
        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Complete", milestone: "release"), "done");
        var persistence = new FaultingProjectConfigPersistence(root)
        {
            ReloadFailuresAfterWrite = 1,
            FailRestoreWrite = true,
        };
        var service = CreateService(root, persistence: persistence);

        var result = service.ReopenMilestone("release");

        Assert.Equal("milestone_reopen_rollback_failed", result.ErrorCode);
    }

    private static MilestoneDeliveryService CreateService(
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

    private static void WriteTask(ProjectRoot root, TaskItem task, string state)
    {
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private static async Task<ProjectRoot> CreateDeliveryProjectWithIdentity(
        string repositoryPath,
        string projectId)
    {
        Directory.CreateDirectory(repositoryPath);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repositoryPath;
        try
        {
            var root = new ProjectRoot();
            await root.CreateProject(TestData.Config(
                milestones: new Dictionary<string, string> { ["release"] = "Release" }));
            await File.WriteAllTextAsync(
                Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");
            WriteTask(root, TestData.Task("PM-0001", "Complete", milestone: "release"), "done");
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
}
