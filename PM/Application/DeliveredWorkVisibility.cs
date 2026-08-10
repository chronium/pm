namespace PM.Application;

internal static class DeliveredWorkVisibility
{
    public static IReadOnlySet<string> ResolveDeliveredMilestoneKeys(MilestoneActivationSnapshot snapshot) =>
        snapshot.Milestones
            .Where(milestone => milestone.Lifecycle == MilestoneLifecycle.Delivered)
            .Select(milestone => milestone.Key)
            .ToHashSet(StringComparer.Ordinal);

    public static bool Includes(
        string? milestoneKey,
        bool includeDelivered,
        IReadOnlySet<string> deliveredMilestoneKeys) =>
        includeDelivered ||
        string.IsNullOrWhiteSpace(milestoneKey) ||
        !deliveredMilestoneKeys.Contains(milestoneKey);
}
