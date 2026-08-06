using PM.Project;
using YamlDotNet.Core;

namespace PM.Tests;

public class ActivationTriggerConfigTests
{
    [Fact]
    public void MissingActivationTriggersUsesEmptyDictionary()
    {
        const string yaml = """
                            name: Existing
                            idWidth: 4
                            idPrefix: PM
                            taskStates:
                              todo: To Do
                            milestones: {}
                            """;

        var config = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Empty(config.ActivationTriggers);
    }

    [Fact]
    public void ActivationTriggersRoundTripAllPersistenceShapesInOrder()
    {
        var at = new DateTimeOffset(2026, 8, 6, 8, 15, 0, TimeSpan.Zero);
        var taskRequirement = new ActivationRequirement
        {
            Kind = ActivationRequirementKind.Task,
            Source = "PM-0001",
        };
        var milestoneRequirement = new ActivationRequirement
        {
            Kind = ActivationRequirementKind.Milestone,
            Source = "foundation",
        };
        var config = TestData.Config(activationTriggers: new Dictionary<string, ActivationTriggerDefinition>
        {
            ["beta-entry"] = new()
            {
                Title = "Beta entry criteria",
                Requirements = [taskRequirement, milestoneRequirement],
            },
            ["architecture-ready"] = new()
            {
                Title = "Architecture ready",
                Requirements = [milestoneRequirement],
                Activation = new ActivationRecord
                {
                    At = at,
                    Mode = ActivationMode.Automatic,
                },
            },
            ["launch-authorized"] = new()
            {
                Title = "Launch authorized",
                Activation = new ActivationRecord
                {
                    At = at.AddMinutes(1),
                    Mode = ActivationMode.Manual,
                },
            },
            ["beta-override"] = new()
            {
                Title = "Beta override",
                Requirements = [taskRequirement, milestoneRequirement],
                Activation = new ActivationRecord
                {
                    At = at.AddMinutes(2),
                    Mode = ActivationMode.Override,
                    Reason = "Architecture approval will complete during hardening.",
                    WaivedRequirements = [milestoneRequirement],
                },
            },
        });

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Equal(
            ["beta-entry", "architecture-ready", "launch-authorized", "beta-override"],
            roundTrip.ActivationTriggers.Keys);
        Assert.Equal(
            [ActivationRequirementKind.Task, ActivationRequirementKind.Milestone],
            roundTrip.ActivationTriggers["beta-entry"].Requirements.Select(requirement => requirement.Kind));
        Assert.Equal(
            ["PM-0001", "foundation"],
            roundTrip.ActivationTriggers["beta-entry"].Requirements.Select(requirement => requirement.Source));
        Assert.Null(roundTrip.ActivationTriggers["beta-entry"].Activation);
        var automatic = roundTrip.ActivationTriggers["architecture-ready"].Activation!;
        Assert.Equal(at, automatic.At);
        Assert.Equal(ActivationMode.Automatic, automatic.Mode);
        var manual = roundTrip.ActivationTriggers["launch-authorized"].Activation!;
        Assert.Equal(at.AddMinutes(1), manual.At);
        Assert.Equal(ActivationMode.Manual, manual.Mode);
        var activation = roundTrip.ActivationTriggers["beta-override"].Activation!;
        Assert.Equal(at.AddMinutes(2), activation.At);
        Assert.Equal(ActivationMode.Override, activation.Mode);
        Assert.Equal("Architecture approval will complete during hardening.", activation.Reason);
        var waived = Assert.Single(activation.WaivedRequirements);
        Assert.Equal(ActivationRequirementKind.Milestone, waived.Kind);
        Assert.Equal("foundation", waived.Source);

        Assert.Contains("activationTriggers:", yaml);
        Assert.Contains("kind: task", yaml);
        Assert.Contains("kind: milestone", yaml);
        Assert.Contains("mode: automatic", yaml);
        Assert.Contains("mode: manual", yaml);
        Assert.Contains("mode: override", yaml);
        Assert.Contains("activation: null", yaml);
        Assert.DoesNotContain("activation: \n", yaml);
        Assert.DoesNotContain("actor", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Count(yaml, "reason:"));
        Assert.Equal(1, Count(yaml, "waivedRequirements:"));
    }

    [Theory]
    [InlineData("requirements:\n      - kind: unknown\n        source: PM-0001\n    activation: null")]
    [InlineData("requirements: []\n    activation:\n      at: 2026-08-06T08:15:00Z\n      mode: unknown")]
    public void UnknownRequirementKindOrActivationModeIsRejected(string triggerBody)
    {
        var yaml = $$"""
                     name: Invalid
                     idWidth: 4
                     idPrefix: PM
                     taskStates:
                       todo: To Do
                     milestones: {}
                     activationTriggers:
                       invalid:
                         title: Invalid
                         {{triggerBody}}
                     """;

        Assert.Throws<YamlException>(() => YamlSerde.Deserialize<ProjectConfig>(yaml));
    }

    private static int Count(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
