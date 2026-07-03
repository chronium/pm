using PM.Files;

namespace PM.Project;

public class ProjectConfig
{
    public const string DefaultNextIdServiceUrl = "http://localhost:8080";

    public required string Name { get; set; }
    public required int IdWidth { get; set; }
    public required string IdPrefix { get; set; }
    public string NextIdServiceUrl { get; set; } = DefaultNextIdServiceUrl;
    public required Dictionary<string, string> TaskStates { get; set; } = new();

    public void WriteConfig(ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists || projectRoot.RootPath is null)
            throw new InvalidOperationException("Project root does not exist.");
        var configPath = Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile);
        FileSystem.WriteAllText(configPath, YamlSerde.Serialize(this));
    }

    public static ProjectConfig ReadConfig(ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists || projectRoot.RootPath is null)
            throw new InvalidOperationException("Project root does not exist.");
        var configPath = Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile);
        var configText = FileSystem.ReadAllText(configPath);
        return YamlSerde.Deserialize<ProjectConfig>(configText);
    }
}
