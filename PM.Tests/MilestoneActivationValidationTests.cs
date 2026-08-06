using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class MilestoneActivationValidationTests
{
    [Fact]
    public async Task ValidatesWellFormedActivationAndDeliveryRecords()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["ordinary"] = "Ordinary delivery",
            ["exceptional"] = "Exceptional delivery",
            ["pending"] = "Pending delivery",
        });
        config.ActivationTriggers = new Dictionary<string, ActivationTriggerDefinition>
        {
            ["automatic"] = new()
            {
                Title = "Automatic gate",
                Requirements = [TaskRequirement("PM-0004")],
                Activation = new()
                {
                    At = Timestamp,
                    Mode = ActivationMode.Automatic,
                },
            },
            ["manual"] = new()
            {
                Title = "Manual gate",
                Activation = new()
                {
                    At = Timestamp,
                    Mode = ActivationMode.Manual,
                },
            },
            ["override"] = new()
            {
                Title = "Override gate",
                Requirements = [TaskRequirement("PM-0001"), TaskRequirement("PM-0002")],
                Activation = new()
                {
                    At = Timestamp,
                    Mode = ActivationMode.Override,
                    Reason = "The remaining work is explicitly accepted.",
                    WaivedRequirements = [TaskRequirement("PM-0002")],
                },
            },
        };
        config.Milestones["ordinary"].RequiredActivationTriggers = ["automatic"];
        config.Milestones["ordinary"].Delivery = new()
        {
            At = Timestamp,
            Mode = MilestoneDeliveryMode.Ordinary,
        };
        config.Milestones["exceptional"].RequiredActivationTriggers = ["manual"];
        config.Milestones["exceptional"].Delivery = new()
        {
            At = Timestamp,
            Mode = MilestoneDeliveryMode.Exceptional,
            Reason = "The open task is accepted for follow-up.",
            AcceptedTaskIds = ["PM-0002"],
        };
        config.Milestones["pending"].RequiredActivationTriggers = ["override"];

        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Done", milestone: "ordinary"), "done");
        WriteTask(root, TestData.Task("PM-0002", "Accepted", milestone: "exceptional"), "todo");
        WriteTask(root, TestData.Task("PM-0003", "Pending", milestone: "pending"), "todo");
        WriteTask(root, TestData.Task("PM-0004", "Unassigned prerequisite"), "done");

        var result = new MilestoneActivationValidationService(root).ValidateProspectiveConfig(config);

        Assert.True(result.Valid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ReportsRequirementAndMilestoneTriggerReferenceIssuesWithStableCodes()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["source"] = "Source",
            ["consumer"] = "Consumer",
        });
        config.ActivationTriggers["entry"] = new ActivationTriggerDefinition
        {
            Title = "Entry",
            Requirements =
            [
                TaskRequirement("PM-9999"),
                TaskRequirement("PM-9999"),
                TaskRequirement(" "),
                MilestoneRequirement("missing"),
            ],
        };
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry", "entry", " ", "missing"];

        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Source work", milestone: "source"), "todo");
        WriteTask(root, TestData.Task("PM-0002", "Consumer work", milestone: "consumer"), "todo");

        var codes = Codes(new MilestoneActivationValidationService(root).ValidateProspectiveConfig(config));

        Assert.Contains("duplicate_activation_requirement", codes);
        Assert.Contains("missing_activation_requirement_source", codes);
        Assert.Contains("unknown_activation_task", codes);
        Assert.Contains("unknown_activation_milestone", codes);
        Assert.Contains("duplicate_milestone_trigger", codes);
        Assert.Contains("missing_milestone_trigger", codes);
        Assert.Contains("unknown_milestone_trigger", codes);
    }

    [Fact]
    public async Task ReportsInvalidActivationProvenanceCombinationsWithStableCodes()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["consumer"] = "Consumer",
        });
        config.ActivationTriggers = new Dictionary<string, ActivationTriggerDefinition>
        {
            ["automatic"] = new()
            {
                Title = "Automatic",
                Activation = new()
                {
                    Mode = ActivationMode.Automatic,
                    Reason = "Not valid for automatic activation.",
                },
            },
            ["manual"] = new()
            {
                Title = "Manual",
                Requirements = [TaskRequirement("PM-0001")],
                Activation = new()
                {
                    At = Timestamp,
                    Mode = ActivationMode.Manual,
                },
            },
            ["override"] = new()
            {
                Title = "Override",
                Requirements = [TaskRequirement("PM-0001")],
                Activation = new()
                {
                    At = Timestamp,
                    Mode = ActivationMode.Override,
                    Reason = " ",
                    WaivedRequirements = [TaskRequirement("PM-9999"), TaskRequirement("PM-9999")],
                },
            },
        };
        config.Milestones["consumer"].RequiredActivationTriggers = ["automatic", "manual", "override"];

        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Consumer work", milestone: "consumer"), "todo");

        var codes = Codes(new MilestoneActivationValidationService(root).ValidateProspectiveConfig(config));

        Assert.Contains("invalid_activation_timestamp", codes);
        Assert.Contains("invalid_automatic_activation", codes);
        Assert.Contains("invalid_manual_activation", codes);
        Assert.Contains("override_reason_required", codes);
        Assert.Contains("invalid_override_waiver", codes);
    }

    [Fact]
    public async Task ReportsInvalidDeliveryRecordsWithStableCodes()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["ordinary"] = "Ordinary",
            ["exceptional"] = "Exceptional",
        });
        config.Milestones["ordinary"].Delivery = new()
        {
            Mode = MilestoneDeliveryMode.Ordinary,
            Reason = "Ordinary delivery cannot accept open work.",
            AcceptedTaskIds = ["PM-0001"],
        };
        config.Milestones["exceptional"].Delivery = new()
        {
            At = Timestamp,
            Mode = MilestoneDeliveryMode.Exceptional,
            Reason = " ",
            AcceptedTaskIds = ["PM-9999", "PM-9999"],
        };

        var root = await workspace.CreateProject(config);
        WriteTask(root, TestData.Task("PM-0001", "Open ordinary work", milestone: "ordinary"), "todo");
        WriteTask(root, TestData.Task("PM-0002", "Open exceptional work", milestone: "exceptional"), "todo");

        var codes = Codes(new MilestoneActivationValidationService(root).ValidateProspectiveConfig(config));

        Assert.Contains("invalid_delivery_timestamp", codes);
        Assert.Contains("invalid_ordinary_delivery", codes);
        Assert.Contains("exceptional_delivery_reason_required", codes);
        Assert.Contains("invalid_exceptional_delivery_snapshot", codes);
    }

    [Fact]
    public async Task ProjectValidationUsesTheReusableValidatorAndWarningsDoNotInvalidateProject()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["empty"] = "Empty deliverable",
        });
        config.ActivationTriggers["unused"] = new ActivationTriggerDefinition
        {
            Title = "Unused gate",
        };
        var root = await workspace.CreateProject(config);
        var reusable = new MilestoneActivationValidationService(root).ValidateProspectiveConfig(config);

        var projectResult = new ProjectValidationService(root).ValidateProject();

        Assert.True(reusable.Valid);
        Assert.Contains(reusable.Issues, issue => issue.Code == "empty_milestone" && issue.Severity == "warning");
        Assert.Contains(reusable.Issues, issue => issue.Code == "unused_activation_trigger" && issue.Severity == "warning");
        Assert.True(projectResult.Success);
        Assert.True(projectResult.Payload!.Valid);
        Assert.Equal(Codes(reusable), Codes(projectResult.Payload));
    }

    [Fact]
    public async Task ReportsBlankDefinitionTitles()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new Dictionary<string, string>
        {
            ["consumer"] = " ",
        });
        config.ActivationTriggers["entry"] = new ActivationTriggerDefinition { Title = " " };
        config.Milestones["consumer"].RequiredActivationTriggers = ["entry"];
        var root = await workspace.CreateProject(config);

        var codes = Codes(new MilestoneActivationValidationService(root).ValidateProspectiveConfig(config));

        Assert.Contains("invalid_milestone_title", codes);
        Assert.Contains("invalid_activation_trigger_title", codes);
    }

    private static readonly DateTimeOffset Timestamp = new(2026, 8, 6, 8, 15, 0, TimeSpan.Zero);

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

    private static HashSet<string> Codes(ProjectValidationResult result) =>
        result.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);

    private static void WriteTask(ProjectRoot root, PM.Tasks.TaskItem task, string state)
    {
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }
}
