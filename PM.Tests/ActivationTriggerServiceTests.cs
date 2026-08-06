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

    private static ActivationTriggerService CreateService(ProjectRoot root) =>
        new(root, new MilestoneActivationResolver(root), new MilestoneActivationValidationService(root));

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
