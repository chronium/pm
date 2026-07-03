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
    public void SerializingConfigIncludesNextIdServiceUrl()
    {
        var config = TestData.Config(nextIdServiceUrl: "https://ids.example.test");

        var yaml = YamlSerde.Serialize(config);

        Assert.Contains("nextIdServiceUrl: https://ids.example.test", yaml);
    }
}
