using PM.Project;
using PM.Tasks;

namespace PM.Application;

internal sealed record MilestoneDeliveryEvaluation(
    bool HasTimestamp,
    bool HasReason,
    bool ModeFieldsValid,
    bool SnapshotValid,
    bool IsValid);

internal static class MilestoneDeliveryEvaluator
{
    public static MilestoneDeliveryEvaluation Evaluate(
        MilestoneDelivery delivery,
        IReadOnlyList<TaskItem> assignedTasks,
        IReadOnlyDictionary<string, string> stateByTaskId)
    {
        var unfinishedTaskIds = assignedTasks
            .Where(task => !stateByTaskId.TryGetValue(task.Id, out var state) ||
                           !string.Equals(state, "done", StringComparison.Ordinal))
            .Select(task => task.Id)
            .ToList();
        var unfinishedSet = unfinishedTaskIds.ToHashSet(StringComparer.Ordinal);
        var acceptedTaskIds = delivery.AcceptedTaskIds ?? [];
        var acceptedSet = acceptedTaskIds.ToHashSet(StringComparer.Ordinal);
        var snapshotValid = unfinishedSet.Count > 0 &&
                            acceptedSet.Count == acceptedTaskIds.Count &&
                            acceptedSet.SetEquals(unfinishedSet);
        var hasReason = !string.IsNullOrWhiteSpace(delivery.Reason);
        var modeFieldsValid = delivery.Mode switch
        {
            MilestoneDeliveryMode.Ordinary =>
                assignedTasks.Count > 0 && unfinishedTaskIds.Count == 0 &&
                !hasReason && acceptedTaskIds.Count == 0,
            MilestoneDeliveryMode.Exceptional => hasReason && snapshotValid,
            _ => false,
        };
        var hasTimestamp = delivery.At != default;

        return new MilestoneDeliveryEvaluation(
            hasTimestamp,
            hasReason,
            modeFieldsValid,
            snapshotValid,
            hasTimestamp && modeFieldsValid);
    }
}
