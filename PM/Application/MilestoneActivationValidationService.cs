using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed class MilestoneActivationValidationService
{
    private readonly ProjectRoot projectRoot;
    private readonly MilestoneActivationGraphService activationGraph;

    public MilestoneActivationValidationService(
        ProjectRoot projectRoot,
        MilestoneActivationGraphService activationGraph)
    {
        this.projectRoot = projectRoot;
        this.activationGraph = activationGraph;
    }

    public ProjectValidationResult ValidateProspectiveConfig(ProjectConfig config)
    {
        var tasksById = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        foreach (var task in projectRoot.GetAllTasks())
            tasksById.TryAdd(task.Id, task);
        return CreateResult(Validate(config, tasksById));
    }

    public IReadOnlyList<ProjectValidationIssue> Validate(
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById)
    {
        var issues = new List<ProjectValidationIssue>();
        var configPath = projectRoot.ConfigPath;
        var stateByTaskId = tasksById.Values.ToDictionary(
            task => task.Id,
            task => projectRoot.TryGetState(task, out var state) ? state : string.Empty,
            StringComparer.Ordinal);

        ValidateTriggers(issues, config, tasksById, configPath);
        ValidateMilestones(issues, config, tasksById, stateByTaskId, configPath);
        ValidateActivationCycles(issues, config, tasksById, configPath);

        return issues;
    }

    private void ValidateActivationCycles(
        List<ProjectValidationIssue> issues,
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        string configPath)
    {
        foreach (var cycle in activationGraph.Build(config, tasksById).Cycles)
            AddError(
                issues,
                "activation_cycle",
                $"Milestone activation cycle detected: {string.Join(" -> ", cycle.Path)}.",
                configPath);
    }

    private void ValidateTriggers(
        List<ProjectValidationIssue> issues,
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        string configPath)
    {
        var consumedTriggers = config.Milestones.Values
            .SelectMany(milestone => milestone.RequiredActivationTriggers ?? [])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (triggerKey, trigger) in config.ActivationTriggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Title))
                AddError(issues, "invalid_activation_trigger_title",
                    $"Activation trigger {triggerKey} must have a non-empty title.", configPath);

            var requirements = trigger.Requirements ?? [];
            ValidateRequirements(issues, triggerKey, requirements, config, tasksById, configPath);
            ValidateActivation(issues, triggerKey, requirements, trigger.Activation, configPath);

            if (!consumedTriggers.Contains(triggerKey))
                issues.Add(new ProjectValidationIssue(
                    "warning",
                    "unused_activation_trigger",
                    $"Activation trigger {triggerKey} is not required by any milestone.",
                    configPath));
        }
    }

    private static void ValidateRequirements(
        List<ProjectValidationIssue> issues,
        string triggerKey,
        IReadOnlyList<ActivationRequirement> requirements,
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        string configPath)
    {
        var seen = new HashSet<RequirementKey>();
        foreach (var requirement in requirements)
        {
            var key = RequirementKey.From(requirement);
            if (!seen.Add(key))
                AddError(issues, "duplicate_activation_requirement",
                    $"Activation trigger {triggerKey} contains duplicate requirement {Format(requirement)}.",
                    configPath);

            if (string.IsNullOrWhiteSpace(requirement.Source))
            {
                AddError(issues, "missing_activation_requirement_source",
                    $"Activation trigger {triggerKey} contains a requirement with no source.", configPath);
                continue;
            }

            switch (requirement.Kind)
            {
                case ActivationRequirementKind.Task when !tasksById.ContainsKey(requirement.Source):
                    AddError(issues, "unknown_activation_task",
                        $"Activation trigger {triggerKey} references unknown task {requirement.Source}.",
                        configPath, requirement.Source);
                    break;
                case ActivationRequirementKind.Milestone when !config.Milestones.ContainsKey(requirement.Source):
                    AddError(issues, "unknown_activation_milestone",
                        $"Activation trigger {triggerKey} references unknown milestone {requirement.Source}.",
                        configPath);
                    break;
            }
        }
    }

    private static void ValidateActivation(
        List<ProjectValidationIssue> issues,
        string triggerKey,
        IReadOnlyList<ActivationRequirement> requirements,
        ActivationRecord? activation,
        string configPath)
    {
        if (activation is null) return;

        if (activation.At == default)
            AddError(issues, "invalid_activation_timestamp",
                $"Activation trigger {triggerKey} has no activation timestamp.", configPath);

        var waived = activation.WaivedRequirements ?? [];
        switch (activation.Mode)
        {
            case ActivationMode.Automatic:
                if (requirements.Count == 0 || !string.IsNullOrWhiteSpace(activation.Reason) || waived.Count > 0)
                    AddError(issues, "invalid_automatic_activation",
                        $"Automatic activation for trigger {triggerKey} requires a non-empty definition and cannot include override fields.",
                        configPath);
                break;

            case ActivationMode.Manual:
                if (requirements.Count > 0 || !string.IsNullOrWhiteSpace(activation.Reason) || waived.Count > 0)
                    AddError(issues, "invalid_manual_activation",
                        $"Manual activation for trigger {triggerKey} is only valid for a manual-only trigger and cannot include override fields.",
                        configPath);
                break;

            case ActivationMode.Override:
                if (string.IsNullOrWhiteSpace(activation.Reason))
                    AddError(issues, "override_reason_required",
                        $"Override activation for trigger {triggerKey} requires a reason.", configPath);

                var requirementSet = requirements.Select(RequirementKey.From).ToHashSet();
                var waivedSet = new HashSet<RequirementKey>();
                var invalidWaiver = requirements.Count == 0 || waived.Count == 0;
                foreach (var requirement in waived)
                {
                    var key = RequirementKey.From(requirement);
                    if (!waivedSet.Add(key) || !requirementSet.Contains(key))
                        invalidWaiver = true;
                }

                if (invalidWaiver)
                    AddError(issues, "invalid_override_waiver",
                        $"Override activation for trigger {triggerKey} must waive one or more unique requirements from its definition.",
                        configPath);
                break;
        }
    }

    private void ValidateMilestones(
        List<ProjectValidationIssue> issues,
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId,
        string configPath)
    {
        foreach (var (milestoneKey, milestone) in config.Milestones)
        {
            if (string.IsNullOrWhiteSpace(milestone.Title))
                AddError(issues, "invalid_milestone_title",
                    $"Milestone {milestoneKey} must have a non-empty title.", configPath);

            var triggerKeys = milestone.RequiredActivationTriggers ?? [];
            var seenTriggers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var triggerKey in triggerKeys)
            {
                if (string.IsNullOrWhiteSpace(triggerKey))
                {
                    AddError(issues, "missing_milestone_trigger",
                        $"Milestone {milestoneKey} contains an empty required activation trigger reference.",
                        configPath);
                    continue;
                }

                if (!seenTriggers.Add(triggerKey))
                    AddError(issues, "duplicate_milestone_trigger",
                        $"Milestone {milestoneKey} requires activation trigger {triggerKey} more than once.",
                        configPath);

                if (!config.ActivationTriggers.ContainsKey(triggerKey))
                    AddError(issues, "unknown_milestone_trigger",
                        $"Milestone {milestoneKey} requires unknown activation trigger {triggerKey}.", configPath);
            }

            var assignedTasks = tasksById.Values
                .Where(task => string.Equals(task.Milestone, milestoneKey, StringComparison.Ordinal))
                .ToList();
            if (assignedTasks.Count == 0)
                issues.Add(new ProjectValidationIssue(
                    "warning",
                    "empty_milestone",
                    $"Milestone {milestoneKey} has no assigned tasks.",
                    configPath));

            ValidateDelivery(
                issues,
                milestoneKey,
                milestone.Delivery,
                assignedTasks,
                stateByTaskId,
                configPath);
        }
    }

    private void ValidateDelivery(
        List<ProjectValidationIssue> issues,
        string milestoneKey,
        MilestoneDelivery? delivery,
        IReadOnlyList<TaskItem> assignedTasks,
        IReadOnlyDictionary<string, string> stateByTaskId,
        string configPath)
    {
        if (delivery is null) return;

        var evaluation = MilestoneDeliveryEvaluator.Evaluate(delivery, assignedTasks, stateByTaskId);

        if (!evaluation.HasTimestamp)
            AddError(issues, "invalid_delivery_timestamp",
                $"Milestone {milestoneKey} has no delivery timestamp.", configPath);

        switch (delivery.Mode)
        {
            case MilestoneDeliveryMode.Ordinary:
                if (!evaluation.ModeFieldsValid)
                    AddError(issues, "invalid_ordinary_delivery",
                        $"Ordinary delivery for milestone {milestoneKey} requires at least one assigned task, all assigned tasks done, and no exceptional-delivery fields.",
                        configPath);
                break;

            case MilestoneDeliveryMode.Exceptional:
                if (!evaluation.HasReason)
                    AddError(issues, "exceptional_delivery_reason_required",
                        $"Exceptional delivery for milestone {milestoneKey} requires a reason.", configPath);

                if (!evaluation.SnapshotValid)
                    AddError(issues, "invalid_exceptional_delivery_snapshot",
                        $"Exceptional delivery for milestone {milestoneKey} must record every currently unfinished assigned task exactly once.",
                        configPath);
                break;
        }
    }

    private static ProjectValidationResult CreateResult(IReadOnlyList<ProjectValidationIssue> issues) =>
        new(issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)), issues);

    private static void AddError(
        List<ProjectValidationIssue> issues,
        string code,
        string message,
        string path,
        string? taskId = null) =>
        issues.Add(new ProjectValidationIssue("error", code, message, path, taskId));

    private static string Format(ActivationRequirement requirement) =>
        $"{requirement.Kind.ToString().ToLowerInvariant()}:{requirement.Source}";

    private readonly record struct RequirementKey(ActivationRequirementKind Kind, string Source)
    {
        public static RequirementKey From(ActivationRequirement requirement) =>
            new(requirement.Kind, requirement.Source ?? string.Empty);
    }
}
