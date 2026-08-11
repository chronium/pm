using YamlDotNet.Serialization;

namespace PM.Project;

public static class ReleaseTransitionKinds
{
    public const string Task = "task";
    public const string Milestone = "milestone";
    public const string ManualMajor = "manual-major";

    public static bool IsKnown(string kind) =>
        kind is Task or Milestone or ManualMajor;
}

public sealed record ReleaseVersionTransition
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset At { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string FromVersion { get; init; } = string.Empty;
    public string ToVersion { get; init; } = string.Empty;
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Source { get; init; }
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Reason { get; init; }
}

public sealed record ReleaseTransitionPlan(ReleaseVersionTransition Transition, string Revision);

public sealed record ReleaseReconciliationResult(
    bool Changed,
    string Action,
    ReleaseVersionTransition? Transition);

public sealed record ReleaseVersionStatus(
    bool Enabled,
    ReleaseVersion? Version,
    ReleaseVersionTransition? PendingTransition,
    ReleaseVersionTransition? LatestTransition);
