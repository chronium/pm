using PM.Project;
using PM.Tasks;

namespace PM.Application;

public enum MilestoneLifecycle
{
    Delivered,
    Inactive,
    ReadyToDeliver,
    Active,
}

public sealed record ResolvedActivationRequirementReference(
    ActivationRequirementKind Kind,
    string Source);

public sealed record ResolvedActivationRequirement(
    ActivationRequirementKind Kind,
    string Source,
    bool IsSatisfied,
    bool WasWaivedAtActivation);

public sealed record ResolvedActivationProvenance(
    DateTimeOffset At,
    ActivationMode Mode,
    string? Reason,
    IReadOnlyList<ResolvedActivationRequirementReference> WaivedRequirements);

public sealed record ResolvedActivationTrigger(
    string Key,
    string Title,
    bool IsActive,
    ResolvedActivationProvenance? Activation,
    int SatisfiedRequirementCount,
    int RequirementCount,
    bool RequirementsSatisfied,
    bool IsLatchedDespiteUnmetRequirements,
    IReadOnlyList<ResolvedActivationRequirement> Requirements,
    IReadOnlyList<string> ConsumingMilestones);

public sealed record ResolvedMilestoneDelivery(
    DateTimeOffset At,
    MilestoneDeliveryMode Mode,
    string? Reason,
    IReadOnlyList<string> AcceptedTaskIds,
    bool IsValid);

public sealed record ResolvedMilestone(
    string Key,
    string Title,
    string Description,
    string Priority,
    MilestoneLifecycle Lifecycle,
    int AssignedTaskCount,
    int DoneTaskCount,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers,
    ResolvedMilestoneDelivery? Delivery);

public sealed record MilestoneActivationSnapshot(
    IReadOnlyList<ResolvedActivationTrigger> ActivationTriggers,
    IReadOnlyList<ResolvedMilestone> Milestones);

public sealed class MilestoneActivationResolver(ProjectRoot projectRoot)
{
    public AppResult<MilestoneActivationSnapshot> ResolveCurrentProject()
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<MilestoneActivationSnapshot>.Fail(
                "missing_project",
                "Project not found. Run pm init first.");

        var tasksById = BuildTaskLookup(projectRoot.GetAllTasks());
        var stateByTaskId = tasksById.Values.ToDictionary(
            task => task.Id,
            task => projectRoot.TryGetState(task, out var state) ? state : string.Empty,
            StringComparer.Ordinal);

        return AppResult<MilestoneActivationSnapshot>.Ok(
            Resolve(projectRoot.Config, tasksById, stateByTaskId));
    }

    public MilestoneActivationSnapshot Resolve(
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId)
    {
        var assignedTasks = config.Milestones.Keys.ToDictionary(
            key => key,
            key => (IReadOnlyList<TaskItem>)tasksById.Values
                .Where(task => string.Equals(task.Milestone, key, StringComparison.Ordinal))
                .ToList(),
            StringComparer.Ordinal);
        var deliveryEvaluations = config.Milestones.ToDictionary(
            milestone => milestone.Key,
            milestone => milestone.Value.Delivery == null
                ? null
                : MilestoneDeliveryEvaluator.Evaluate(
                    milestone.Value.Delivery,
                    assignedTasks[milestone.Key],
                    stateByTaskId),
            StringComparer.Ordinal);

        var triggers = config.ActivationTriggers
            .Select(trigger => ResolveTrigger(
                trigger.Key,
                trigger.Value,
                config,
                tasksById,
                stateByTaskId,
                deliveryEvaluations))
            .ToList();
        var triggersByKey = triggers.ToDictionary(trigger => trigger.Key, StringComparer.Ordinal);
        var milestones = config.Milestones
            .Select(milestone => ResolveMilestone(
                milestone.Key,
                milestone.Value,
                assignedTasks[milestone.Key],
                stateByTaskId,
                triggersByKey,
                deliveryEvaluations[milestone.Key]))
            .ToList();

        return new MilestoneActivationSnapshot(triggers, milestones);
    }

    private static ResolvedActivationTrigger ResolveTrigger(
        string triggerKey,
        ActivationTriggerDefinition trigger,
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId,
        IReadOnlyDictionary<string, MilestoneDeliveryEvaluation?> deliveryEvaluations)
    {
        var waivedRequirements = (trigger.Activation?.WaivedRequirements ?? [])
            .Select(RequirementKey.From)
            .ToHashSet();
        var requirements = (trigger.Requirements ?? [])
            .Select(requirement => new ResolvedActivationRequirement(
                requirement.Kind,
                requirement.Source,
                IsRequirementSatisfied(requirement, tasksById, stateByTaskId, deliveryEvaluations),
                waivedRequirements.Contains(RequirementKey.From(requirement))))
            .ToList();
        var satisfiedCount = requirements.Count(requirement => requirement.IsSatisfied);
        var requirementsSatisfied = requirements.Count > 0 && satisfiedCount == requirements.Count;
        var isActive = trigger.Activation != null;
        var activation = trigger.Activation == null
            ? null
            : new ResolvedActivationProvenance(
                trigger.Activation.At,
                trigger.Activation.Mode,
                trigger.Activation.Reason,
                (trigger.Activation.WaivedRequirements ?? [])
                .Select(requirement => new ResolvedActivationRequirementReference(
                    requirement.Kind,
                    requirement.Source))
                .ToList());
        var consumingMilestones = config.Milestones
            .Where(milestone => (milestone.Value.RequiredActivationTriggers ?? [])
                .Contains(triggerKey, StringComparer.Ordinal))
            .Select(milestone => milestone.Key)
            .ToList();

        return new ResolvedActivationTrigger(
            triggerKey,
            trigger.Title,
            isActive,
            activation,
            satisfiedCount,
            requirements.Count,
            requirementsSatisfied,
            isActive && requirements.Count > 0 && !requirementsSatisfied,
            requirements,
            consumingMilestones);
    }

    private static bool IsRequirementSatisfied(
        ActivationRequirement requirement,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId,
        IReadOnlyDictionary<string, MilestoneDeliveryEvaluation?> deliveryEvaluations) =>
        requirement.Kind switch
        {
            ActivationRequirementKind.Task =>
                tasksById.ContainsKey(requirement.Source) &&
                stateByTaskId.TryGetValue(requirement.Source, out var state) &&
                string.Equals(state, "done", StringComparison.Ordinal),
            ActivationRequirementKind.Milestone =>
                deliveryEvaluations.TryGetValue(requirement.Source, out var delivery) &&
                delivery?.IsValid == true,
            _ => false,
        };

    private static ResolvedMilestone ResolveMilestone(
        string milestoneKey,
        MilestoneDefinition milestone,
        IReadOnlyList<TaskItem> assignedTasks,
        IReadOnlyDictionary<string, string> stateByTaskId,
        IReadOnlyDictionary<string, ResolvedActivationTrigger> triggersByKey,
        MilestoneDeliveryEvaluation? deliveryEvaluation)
    {
        var requiredTriggerKeys = milestone.RequiredActivationTriggers ?? [];
        var unmetTriggerKeys = requiredTriggerKeys
            .Where(triggerKey => !triggersByKey.TryGetValue(triggerKey, out var trigger) || !trigger.IsActive)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var doneTaskCount = assignedTasks.Count(task =>
            stateByTaskId.TryGetValue(task.Id, out var state) &&
            string.Equals(state, "done", StringComparison.Ordinal));
        var lifecycle = deliveryEvaluation?.IsValid == true
            ? MilestoneLifecycle.Delivered
            : unmetTriggerKeys.Count > 0
                ? MilestoneLifecycle.Inactive
                : assignedTasks.Count > 0 && doneTaskCount == assignedTasks.Count
                    ? MilestoneLifecycle.ReadyToDeliver
                    : MilestoneLifecycle.Active;
        var delivery = milestone.Delivery == null
            ? null
            : new ResolvedMilestoneDelivery(
                milestone.Delivery.At,
                milestone.Delivery.Mode,
                milestone.Delivery.Reason,
                (milestone.Delivery.AcceptedTaskIds ?? []).ToList(),
                deliveryEvaluation?.IsValid == true);

        return new ResolvedMilestone(
            milestoneKey,
            milestone.Title,
            milestone.Description,
            milestone.Priority,
            lifecycle,
            assignedTasks.Count,
            doneTaskCount,
            requiredTriggerKeys.ToList(),
            unmetTriggerKeys,
            delivery);
    }

    private static Dictionary<string, TaskItem> BuildTaskLookup(IEnumerable<TaskItem> tasks) =>
        tasks.GroupBy(task => task.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private readonly record struct RequirementKey(ActivationRequirementKind Kind, string Source)
    {
        public static RequirementKey From(ActivationRequirement requirement) =>
            new(requirement.Kind, requirement.Source ?? string.Empty);
    }
}
