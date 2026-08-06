using PM.Files;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
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

    public Dictionary<string, MilestoneDefinition> Milestones { get; set; } = new();

    [YamlIgnore]
    public bool RequiresMilestoneSchemaMigration { get; private set; }

    [YamlIgnore]
    internal IReadOnlyDictionary<string, string> LegacyMilestonePriorities { get; private set; } =
        new Dictionary<string, string>();

    [YamlIgnore]
    public string DefaultTrackKey => Tracks.Keys.First();

    public void WriteConfig(ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists || projectRoot.RootPath is null)
            throw new InvalidOperationException("Project root does not exist.");
        if (RequiresMilestoneSchemaMigration)
            throw new InvalidOperationException(
                "Legacy milestone configuration must be migrated with pm doctor --fix before it can be written.");

        var configPath = Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile);
        FileSystem.WriteAllText(configPath, YamlSerde.Serialize(this));
    }

    internal void WriteMigratedConfig(ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists || projectRoot.RootPath is null)
            throw new InvalidOperationException("Project root does not exist.");

        var configPath = Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile);
        FileSystem.WriteAllTextAtomic(configPath, YamlSerde.Serialize(this));
    }

    public static ProjectConfig ReadConfig(ProjectRoot projectRoot)
    {
        if (!projectRoot.Exists || projectRoot.RootPath is null)
            throw new InvalidOperationException("Project root does not exist.");
        var configPath = Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile);
        var configText = FileSystem.ReadAllText(configPath);
        return Deserialize(configText);
    }

    public static ProjectConfig Deserialize(string yaml)
    {
        var shape = InspectMilestoneShape(yaml);
        if (shape == MilestoneSchemaShape.Mixed)
            throw new YamlException(
                "Milestones cannot mix legacy scalar entries with structured milestone definitions.");

        if (shape == MilestoneSchemaShape.Legacy)
            return DeserializeLegacy(yaml);

        return YamlSerde.Deserializer.Deserialize<ProjectConfig>(yaml);
    }

    private static ProjectConfig DeserializeLegacy(string yaml)
    {
        var legacy = YamlSerde.Deserializer.Deserialize<LegacyProjectConfig>(yaml);
        var priorities = legacy.MilestonePriorities ?? new Dictionary<string, string>();
        var milestones = new Dictionary<string, MilestoneDefinition>();
        foreach (var (key, title) in legacy.Milestones ?? new Dictionary<string, string>())
        {
            milestones[key] = new MilestoneDefinition
            {
                Title = title ?? string.Empty,
                Priority = priorities.TryGetValue(key, out var priority)
                    ? priority
                    : PriorityLevel.None,
            };
        }

        return new ProjectConfig
        {
            Name = legacy.Name,
            IdWidth = legacy.IdWidth,
            IdPrefix = legacy.IdPrefix,
            Accent = legacy.Accent,
            NextIdServiceUrl = legacy.NextIdServiceUrl,
            TaskStates = legacy.TaskStates ?? new Dictionary<string, string>(),
            Tracks = legacy.Tracks ?? new Dictionary<string, string>(),
            Milestones = milestones,
            RequiresMilestoneSchemaMigration = true,
            LegacyMilestonePriorities = priorities,
        };
    }

    private static MilestoneSchemaShape InspectMilestoneShape(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new YamlException("Project configuration must contain a YAML mapping.");

        var milestonesNode = Find(root, "milestones");
        var prioritiesNode = Find(root, "milestonePriorities");
        if (milestonesNode is not null and not YamlMappingNode)
            throw new YamlException("Milestones must be a YAML mapping.");

        var hasScalar = false;
        var hasMapping = false;
        if (milestonesNode is YamlMappingNode milestones)
        {
            foreach (var value in milestones.Children.Values)
            {
                hasScalar |= value is YamlScalarNode;
                hasMapping |= value is YamlMappingNode;
                if (value is not YamlScalarNode and not YamlMappingNode)
                    throw new YamlException("Each milestone must be a scalar title or structured mapping.");
            }
        }

        if (hasScalar && hasMapping || hasMapping && prioritiesNode is not null)
            return MilestoneSchemaShape.Mixed;
        if (hasScalar || prioritiesNode is not null)
            return MilestoneSchemaShape.Legacy;
        return MilestoneSchemaShape.Structured;
    }

    private static YamlNode? Find(YamlMappingNode mapping, string key)
    {
        foreach (var (nodeKey, value) in mapping.Children)
        {
            if (nodeKey is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
                return value;
        }

        return null;
    }

    private enum MilestoneSchemaShape
    {
        Structured,
        Legacy,
        Mixed,
    }

    private sealed class LegacyProjectConfig
    {
        public string Name { get; set; } = string.Empty;
        public int IdWidth { get; set; }
        public string IdPrefix { get; set; } = string.Empty;
        public string Accent { get; set; } = ProjectAccent.Default;
        public string NextIdServiceUrl { get; set; } = DefaultNextIdServiceUrl;
        public Dictionary<string, string>? TaskStates { get; set; }
        public Dictionary<string, string>? Tracks { get; set; }
        public Dictionary<string, string>? Milestones { get; set; }
        public Dictionary<string, string>? MilestonePriorities { get; set; }
    }
}
