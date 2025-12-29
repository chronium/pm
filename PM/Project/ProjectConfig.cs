using PM.Files;

namespace PM.Project;

public class ProjectConfig
{
    public required string Name { get; set; }
    public required int IdWidth { get; set; }
    public required string IdPrefix { get; set; }
    public required Dictionary<string, string> TaskStates { get; set; } = new();

    public void WriteConfig(ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists || projectRoot.RootPath is null)
            throw new InvalidOperationException("Project root does not exist.");
        var configPath = Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile);
        FileSystem.WriteFileWithText(configPath, YamlSerde.Serialize(this));
    }
}