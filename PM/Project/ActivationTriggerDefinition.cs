using YamlDotNet.Serialization;

namespace PM.Project;

public sealed record ActivationTriggerDefinition
{
    public string Title { get; set; } = string.Empty;
    public List<ActivationRequirement> Requirements { get; set; } = [];
    public ActivationRecord? Activation { get; set; }
}

public sealed record ActivationRequirement
{
    public ActivationRequirementKind Kind { get; set; }
    public string Source { get; set; } = string.Empty;
}

public enum ActivationRequirementKind
{
    Task,
    Milestone,
}

public sealed record ActivationRecord
{
    public DateTimeOffset At { get; set; }
    public ActivationMode Mode { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Reason { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
    public List<ActivationRequirement> WaivedRequirements { get; set; } = [];
}

public enum ActivationMode
{
    Automatic,
    Manual,
    Override,
}
