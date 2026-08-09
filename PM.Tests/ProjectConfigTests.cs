using PM.Project;
using YamlDotNet.Core;

namespace PM.Tests;

public class ProjectConfigTests
{
    [Fact]
    public void DeserializingOldYamlUsesCompatibilityDefaults()
    {
        const string yaml = """
                            name: Legacy
                            idWidth: 4
                            idPrefix: PM
                            taskStates:
                              todo: To Do
                            """;

        var config = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Equal(ProjectConfig.DefaultNextIdServiceUrl, config.NextIdServiceUrl);
        var track = Assert.Single(config.Tracks);
        Assert.Equal("PM", track.Key);
        Assert.Equal("PM", track.Value);
        Assert.False(config.RequiresMilestoneSchemaMigration);
    }

    [Fact]
    public void DeserializingLegacyMilestonesMaterializesOrderedDefinitions()
    {
        const string yaml = """
                            name: Legacy
                            idWidth: 4
                            idPrefix: PM
                            taskStates:
                              todo: To Do
                            milestones:
                              beta: Public beta
                              launch: Launch
                            milestonePriorities:
                              beta: high
                            """;

        var config = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.True(config.RequiresMilestoneSchemaMigration);
        Assert.Equal(["beta", "launch"], config.Milestones.Keys);
        Assert.Equal("Public beta", config.Milestones["beta"].Title);
        Assert.Equal("high", config.Milestones["beta"].Priority);
        Assert.Equal(PriorityLevel.None, config.Milestones["launch"].Priority);
        Assert.All(config.Milestones.Values, milestone =>
        {
            Assert.Equal(string.Empty, milestone.Description);
            Assert.Empty(milestone.RequiredActivationTriggers);
            Assert.Null(milestone.Delivery);
        });
    }

    [Fact]
    public void StructuredMilestonesRoundTripWithExplicitFieldsAndOrder()
    {
        var config = TestData.Config();
        config.Milestones = new Dictionary<string, MilestoneDefinition>
        {
            ["beta"] = new()
            {
                Title = "Public beta",
                Description = "Deliver an installable beta.\n\nInclude the local workflow.",
                Priority = PriorityLevel.High,
                RequiredActivationTriggers = ["beta-entry", "launch-authorized"],
                Delivery = new MilestoneDelivery
                {
                    At = new DateTimeOffset(2026, 8, 6, 8, 15, 0, TimeSpan.Zero),
                    Mode = MilestoneDeliveryMode.Exceptional,
                    Reason = "Accepted with follow-up work.",
                    AcceptedTaskIds = ["PM-0001"],
                },
            },
            ["launch"] = new() { Title = "Launch" },
        };

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.DoesNotContain("milestonePriorities:", yaml);
        Assert.Contains("description:", yaml);
        Assert.Contains("priority:", yaml);
        Assert.Contains("requiredActivationTriggers:", yaml);
        Assert.Contains("delivery:", yaml);
        Assert.DoesNotContain("delivery: \n", yaml);
        Assert.Contains("delivery: null", yaml);
        Assert.Equal(["beta", "launch"], roundTrip.Milestones.Keys);
        var beta = roundTrip.Milestones["beta"];
        Assert.Equal(config.Milestones["beta"].Description, beta.Description);
        Assert.Equal(["beta-entry", "launch-authorized"], beta.RequiredActivationTriggers);
        Assert.Equal(MilestoneDeliveryMode.Exceptional, beta.Delivery!.Mode);
        Assert.Equal(["PM-0001"], beta.Delivery.AcceptedTaskIds);
        Assert.False(roundTrip.RequiresMilestoneSchemaMigration);
    }

    [Fact]
    public void DeserializingMixedMilestoneSchemasIsRejected()
    {
        const string yaml = """
                            name: Mixed
                            idWidth: 4
                            idPrefix: PM
                            taskStates:
                              todo: To Do
                            milestones:
                              old: Old title
                              new:
                                title: New title
                                description: ''
                                priority: none
                                requiredActivationTriggers: []
                                delivery:
                            """;

        var exception = Assert.Throws<YamlException>(() => YamlSerde.Deserialize<ProjectConfig>(yaml));

        Assert.Contains("cannot mix", exception.Message);
    }

    [Fact]
    public void DeserializingInvalidDeliveryModeIsRejected()
    {
        const string yaml = """
                            name: Invalid
                            idWidth: 4
                            idPrefix: PM
                            taskStates:
                              todo: To Do
                            milestones:
                              beta:
                                title: Beta
                                description: ''
                                priority: none
                                requiredActivationTriggers: []
                                delivery:
                                  at: 2026-08-06T08:15:00Z
                                  mode: unknown
                                  reason:
                                  acceptedTaskIds: []
                            """;

        Assert.Throws<YamlException>(() => YamlSerde.Deserialize<ProjectConfig>(yaml));
    }

    [Fact]
    public void AbsentOverviewSiteRemainsAbsentAcrossRoundTrip()
    {
        var config = TestData.Config();

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.DoesNotContain("site:", yaml);
        Assert.Null(roundTrip.Site);
    }

    [Fact]
    public void OverviewSiteRoundTripsWithoutMaterializingImplicitValues()
    {
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Title = "Published PM",
            Home = new OverviewHomeDefinition
            {
                Layout = OverviewLayouts.Split,
                Primary =
                [
                    new OverviewSectionDefinition { Type = OverviewSectionKinds.Hero },
                    new OverviewSectionDefinition
                    {
                        Type = OverviewSectionKinds.Markdown,
                        Source = "wiki:overview",
                    },
                ],
                Secondary =
                [
                    new OverviewSectionDefinition
                    {
                        Type = OverviewSectionKinds.Tasks,
                        Filter = "state:todo in:all",
                        Limit = 5,
                    },
                ],
                After = [],
            },
        };

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Contains("site:", yaml);
        Assert.Contains("layout: split", yaml);
        Assert.DoesNotContain("description:", yaml);
        Assert.DoesNotContain("sections:", yaml);
        Assert.NotNull(roundTrip.Site);
        Assert.Null(roundTrip.Site.Description);
        Assert.Equal(OverviewLayouts.Split, roundTrip.Site.Home!.Layout);
        Assert.Null(roundTrip.Site.Home.Sections);
        Assert.Empty(roundTrip.Site.Home.After!);
        Assert.Equal(
            [OverviewSectionKinds.Hero, OverviewSectionKinds.Markdown],
            roundTrip.Site.Home.Primary!.Select(section => section.Type));
    }

    [Fact]
    public void OverviewSitePreservesOmittedEnabledLayoutAndSections()
    {
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition { Home = new OverviewHomeDefinition() };

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Contains("site:", yaml);
        Assert.DoesNotContain("enabled:", yaml);
        Assert.DoesNotContain("layout:", yaml);
        Assert.DoesNotContain("sections:", yaml);
        Assert.Null(roundTrip.Site!.Enabled);
        Assert.Null(roundTrip.Site.Home!.Layout);
        Assert.Null(roundTrip.Site.Home.Sections);
    }

    [Fact]
    public void ExplicitlyDisabledOverviewSiteRoundTripsWithoutChurn()
    {
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition { Enabled = false };

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Contains("site:", yaml);
        Assert.Contains("enabled: false", yaml);
        Assert.False(roundTrip.Site!.Enabled);
        Assert.Equal(yaml, YamlSerde.Serialize(roundTrip));
    }

    [Theory]
    [InlineData("site:\n  enabled: true\n  columns: 2\n")]
    [InlineData("site:\n  home:\n    columns: 2\n")]
    [InlineData("site:\n  home:\n    sections:\n    - type: hero\n      background: blue\n")]
    public void UnknownOverviewConfigurationFieldsAreRejected(string siteYaml)
    {
        var yaml = YamlSerde.Serialize(TestData.Config()) + siteYaml;

        Assert.Throws<YamlException>(() => YamlSerde.Deserialize<ProjectConfig>(yaml));
    }
}
