using PM.Files;
using YamlDotNet.Serialization;

namespace PM.Project;

public class ProjectConfig
{
    public const string DefaultNextIdServiceUrl = "https://pm-next-id.chronium.workers.dev";
    private Dictionary<string, string>? _tracks;

    public required string Name { get; set; }
    public required int IdWidth { get; set; }
    public required string IdPrefix { get; set; }
    public string Accent { get; set; } = ProjectAccent.Default;
    public string NextIdServiceUrl { get; set; } = DefaultNextIdServiceUrl;
    public required Dictionary<string, string> TaskStates { get; set; } = new();
    public Dictionary<string, string> Tracks
    {
        get
        {
            if (_tracks is { Count: > 0 }) return _tracks;
            _tracks = new Dictionary<string, string> { [IdPrefix] = IdPrefix };
            return _tracks;
        }
        set => _tracks = value;
    }

    public Dictionary<string, string> Milestones { get; set; } = new();
    public Dictionary<string, string> MilestonePriorities { get; set; } = new();

    [YamlIgnore]
    public string DefaultTrackKey => Tracks.Keys.First();

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
