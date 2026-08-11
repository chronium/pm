using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed record MilestoneLifecycleChange(
    string MilestoneKey,
    MilestoneLifecycle Before,
    MilestoneLifecycle After);

public sealed record AutomaticActivationImpact(
    IReadOnlyList<ResolvedActivationTrigger> ActivatedTriggers,
    IReadOnlyList<MilestoneLifecycleChange> MilestoneChanges)
{
    public static AutomaticActivationImpact None { get; } = new([], []);
}

public sealed record LifecycleMutationResult<T>(
    T Value,
    AutomaticActivationImpact ActivationImpact,
    ReleaseVersionTransition? ReleaseTransition = null);

public sealed record TaskLifecycleMutationImpact(
    AutomaticActivationImpact ActivationImpact,
    ReleaseVersionTransition? ReleaseTransition);

public sealed class AutomaticActivationService(
    MilestoneActivationResolver resolver,
    TimeProvider timeProvider)
{
    public AutomaticActivationImpact ApplyAffected(
        ProjectConfig prospective,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId,
        MilestoneActivationSnapshot before,
        ActivationRequirementKind affectedKind,
        string affectedSource)
    {
        var candidateKeys = prospective.ActivationTriggers
            .Where(entry => entry.Value.Activation == null)
            .Where(entry => (entry.Value.Requirements ?? []).Any(requirement =>
                requirement.Kind == affectedKind &&
                string.Equals(requirement.Source, affectedSource, StringComparison.Ordinal)))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        return ApplyCandidates(prospective, tasksById, stateByTaskId, before, candidateKeys);
    }

    public AutomaticActivationImpact ApplyAllSatisfied(
        ProjectConfig prospective,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId,
        MilestoneActivationSnapshot before)
    {
        var candidateKeys = prospective.ActivationTriggers
            .Where(entry => entry.Value.Activation == null && (entry.Value.Requirements ?? []).Count > 0)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        return ApplyCandidates(prospective, tasksById, stateByTaskId, before, candidateKeys);
    }

    private AutomaticActivationImpact ApplyCandidates(
        ProjectConfig prospective,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId,
        MilestoneActivationSnapshot before,
        IReadOnlySet<string> candidateKeys)
    {
        if (candidateKeys.Count == 0) return AutomaticActivationImpact.None;

        var pending = resolver.Resolve(prospective, tasksById, stateByTaskId);
        var activatedKeys = pending.ActivationTriggers
            .Where(trigger => candidateKeys.Contains(trigger.Key) &&
                              !trigger.IsActive &&
                              trigger.RequirementsSatisfied)
            .Select(trigger => trigger.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (activatedKeys.Count == 0) return AutomaticActivationImpact.None;

        var activatedAt = timeProvider.GetUtcNow();
        foreach (var key in activatedKeys)
        {
            prospective.ActivationTriggers[key].Activation = new ActivationRecord
            {
                At = activatedAt,
                Mode = ActivationMode.Automatic,
            };
        }

        var after = resolver.Resolve(prospective, tasksById, stateByTaskId);
        var beforeMilestones = before.Milestones.ToDictionary(milestone => milestone.Key, StringComparer.Ordinal);
        var milestoneChanges = after.Milestones
            .Where(milestone => beforeMilestones.TryGetValue(milestone.Key, out var previous) &&
                                previous.Lifecycle != milestone.Lifecycle)
            .Select(milestone => new MilestoneLifecycleChange(
                milestone.Key,
                beforeMilestones[milestone.Key].Lifecycle,
                milestone.Lifecycle))
            .OrderBy(change => change.MilestoneKey, StringComparer.Ordinal)
            .ToList();
        var activatedTriggers = after.ActivationTriggers
            .Where(trigger => activatedKeys.Contains(trigger.Key, StringComparer.Ordinal))
            .OrderBy(trigger => trigger.Key, StringComparer.Ordinal)
            .ToList();

        return new AutomaticActivationImpact(activatedTriggers, milestoneChanges);
    }
}
