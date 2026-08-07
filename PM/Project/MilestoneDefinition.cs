using YamlDotNet.Serialization;

namespace PM.Project;

public sealed record MilestoneDefinition
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = PriorityLevel.None;
    public List<string> RequiredActivationTriggers { get; set; } = [];
    public MilestoneDelivery? Delivery { get; set; }
}

public sealed record MilestoneDelivery
{
    public DateTimeOffset At { get; set; }
    public MilestoneDeliveryMode Mode { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Reason { get; set; }

    public List<string> AcceptedTaskIds { get; set; } = [];
}

public enum MilestoneDeliveryMode
{
    Ordinary,
    Exceptional,
}
