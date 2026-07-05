using PM.Project;

namespace PM.Tests;

public class ProjectConfigTests
{
    [Fact]
    public void DeserializingOldYamlUsesDefaultNextIdServiceUrl()
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
    }

    [Fact]
    public void DeserializingOldYamlUsesIdPrefixAsDefaultTrack()
    {
        const string yaml = """
                            name: Legacy
                            idWidth: 4
                            idPrefix: PM
                            taskStates:
                              todo: To Do
                            """;

        var config = YamlSerde.Deserialize<ProjectConfig>(yaml);

        var track = Assert.Single(config.Tracks);
        Assert.Equal("PM", track.Key);
        Assert.Equal("PM", track.Value);
    }

    [Fact]
    public void DeserializingOldYamlUsesEmptyMilestonePriorities()
    {
        const string yaml = """
                            name: Legacy
                            idWidth: 4
                            idPrefix: PM
                            taskStates:
                              todo: To Do
                            milestones:
                              m1: Milestone 1
                            """;

        var config = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Empty(config.MilestonePriorities);
        Assert.Equal(PriorityLevel.None, PriorityLevel.Resolve(config, "m1"));
    }

    [Fact]
    public void SerializingConfigIncludesNextIdServiceUrl()
    {
        var config = TestData.Config(nextIdServiceUrl: "https://ids.example.test");

        var yaml = YamlSerde.Serialize(config);

        Assert.Contains("nextIdServiceUrl: https://ids.example.test", yaml);
    }

    [Fact]
    public void SerializingConfigIncludesTracksAndMilestones()
    {
        var config = TestData.Config(
            tracks: new Dictionary<string, string> { ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" });

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Equal("Build", roundTrip.Tracks["BUILD"]);
        Assert.Equal("Milestone 1", roundTrip.Milestones["m1"]);
    }

    [Fact]
    public void SerializingConfigIncludesMilestonePriorities()
    {
        var config = TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "high" });

        var yaml = YamlSerde.Serialize(config);
        var roundTrip = YamlSerde.Deserialize<ProjectConfig>(yaml);

        Assert.Contains("milestonePriorities:", yaml);
        Assert.Equal("high", roundTrip.MilestonePriorities["m1"]);
    }
}
