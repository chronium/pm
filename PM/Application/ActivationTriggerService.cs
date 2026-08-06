using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PM.Project;
using PM.Tasks;
using YamlDotNet.Core;

namespace PM.Application;

public sealed record ActivationTriggerMutationResult(
    string TriggerKey,
    IReadOnlyList<string> AffectedMilestones);

public sealed record ActivationTriggerMilestoneImpact(
    string MilestoneKey,
    MilestoneLifecycle Before,
    MilestoneLifecycle After,
    IReadOnlyList<string> CurrentlyEligibleTaskIds,
    IReadOnlyList<string> TaskIdsLosingEligibility);

public sealed record ActivationTriggerRedefinitionPreview(
    string TriggerKey,
    string Revision,
    bool WillReactivateAutomatically,
    bool RequiresConfirmation,
    IReadOnlyList<ActivationTriggerMilestoneImpact> Milestones,
    IReadOnlyList<string> CurrentlyEligibleTaskIds,
    IReadOnlyList<string> TaskIdsLosingEligibility);

public sealed record ActivationTriggerRedefinitionResult(
    string TriggerKey,
    bool IsActive,
    ActivationMode? ActivationMode,
    DateTimeOffset? ActivatedAt,
    IReadOnlyList<string> AffectedMilestones);

public sealed class ActivationTriggerService
{
    private readonly ProjectRoot projectRoot;
    private readonly MilestoneActivationResolver resolver;
    private readonly MilestoneActivationValidationService validator;
    private readonly TimeProvider timeProvider;
    private readonly IProjectConfigPersistence persistence;

    public ActivationTriggerService(
        ProjectRoot projectRoot,
        MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validator,
        TimeProvider timeProvider,
        IProjectConfigPersistence persistence)
    {
        this.projectRoot = projectRoot;
        this.resolver = resolver;
        this.validator = validator;
        this.timeProvider = timeProvider;
        this.persistence = persistence;
    }

    public AppResult<IReadOnlyList<ResolvedActivationTrigger>> ListTriggers()
    {
        var snapshot = resolver.ResolveCurrentProject();
        return snapshot.Success
            ? AppResult<IReadOnlyList<ResolvedActivationTrigger>>.Ok(snapshot.Payload!.ActivationTriggers)
            : AppResult<IReadOnlyList<ResolvedActivationTrigger>>.Fail(snapshot.ErrorCode!, snapshot.Message!);
    }

    public AppResult<ResolvedActivationTrigger> ActivateTrigger(string key, string? reason)
    {
        var stateResult = ReadCurrentActivationState();
        if (!stateResult.Success)
            return AppResult<ResolvedActivationTrigger>.Fail(stateResult.ErrorCode!, stateResult.Message!);

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult<ResolvedActivationTrigger>.Fail(
                "invalid_activation_trigger", "Activation trigger key is required.");

        var state = stateResult.Payload!;
        var trigger = state.Snapshot.ActivationTriggers.SingleOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));
        if (trigger == null)
            return AppResult<ResolvedActivationTrigger>.Fail(
                "missing_activation_trigger", $"Activation trigger {key} not found.");
        if (trigger.IsActive)
            return AppResult<ResolvedActivationTrigger>.Fail(
                "activation_trigger_active", $"Activation trigger {key} is already active.");

        ActivationRecord activation;
        if (trigger.RequirementCount == 0)
        {
            if (reason != null)
                return AppResult<ResolvedActivationTrigger>.Fail(
                    "activation_reason_not_allowed",
                    $"Manual-only activation trigger {key} does not accept an override reason.");

            activation = new ActivationRecord
            {
                At = timeProvider.GetUtcNow(),
                Mode = ActivationMode.Manual,
            };
        }
        else
        {
            if (trigger.RequirementsSatisfied)
                return AppResult<ResolvedActivationTrigger>.Fail(
                    "activation_reconciliation_required",
                    $"Activation trigger {key} has satisfied requirements but no activation record. Run pm trigger reconcile.");
            if (string.IsNullOrWhiteSpace(reason))
                return AppResult<ResolvedActivationTrigger>.Fail(
                    "override_reason_required",
                    $"Activation trigger {key} has unmet requirements. Provide --reason to override them.");

            activation = new ActivationRecord
            {
                At = timeProvider.GetUtcNow(),
                Mode = ActivationMode.Override,
                Reason = reason.Trim(),
                WaivedRequirements = trigger.Requirements
                    .Where(requirement => !requirement.IsSatisfied)
                    .Select(requirement => new ActivationRequirement
                    {
                        Kind = requirement.Kind,
                        Source = requirement.Source,
                    })
                    .ToList(),
            };
        }

        return PersistTransition(state, key,
            prospective => prospective.ActivationTriggers[key].Activation = activation);
    }

    public AppResult<ResolvedActivationTrigger> ResetTrigger(string key)
    {
        var stateResult = ReadCurrentActivationState();
        if (!stateResult.Success)
            return AppResult<ResolvedActivationTrigger>.Fail(stateResult.ErrorCode!, stateResult.Message!);

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult<ResolvedActivationTrigger>.Fail(
                "invalid_activation_trigger", "Activation trigger key is required.");

        var state = stateResult.Payload!;
        var trigger = state.Snapshot.ActivationTriggers.SingleOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));
        if (trigger == null)
            return AppResult<ResolvedActivationTrigger>.Fail(
                "missing_activation_trigger", $"Activation trigger {key} not found.");
        if (!trigger.IsActive)
            return AppResult<ResolvedActivationTrigger>.Fail(
                "activation_trigger_inactive", $"Activation trigger {key} is already inactive.");
        if (trigger.RequirementsSatisfied)
            return AppResult<ResolvedActivationTrigger>.Fail(
                "activation_trigger_reset_blocked",
                $"Activation trigger {key} cannot be reset while all current requirements are satisfied.");

        return PersistTransition(state, key,
            prospective => prospective.ActivationTriggers[key].Activation = null);
    }

    public AppResult<ActivationTriggerRedefinitionPreview> PreviewRedefinition(
        string key,
        IReadOnlyList<ActivationRequirement> requirements)
    {
        var evaluation = EvaluateRedefinition(key, requirements);
        return evaluation.Success
            ? AppResult<ActivationTriggerRedefinitionPreview>.Ok(evaluation.Payload!.Preview)
            : AppResult<ActivationTriggerRedefinitionPreview>.Fail(evaluation.ErrorCode!, evaluation.Message!);
    }

    public AppResult<ActivationTriggerRedefinitionResult> RedefineTrigger(
        string key,
        IReadOnlyList<ActivationRequirement> requirements,
        string expectedRevision,
        bool allowDeactivation)
    {
        if (string.IsNullOrWhiteSpace(expectedRevision))
            return AppResult<ActivationTriggerRedefinitionResult>.Fail(
                "activation_trigger_redefine_revision_required",
                "Activation trigger redefinition requires a preview revision.");

        var evaluationResult = EvaluateRedefinition(key, requirements);
        if (!evaluationResult.Success)
            return AppResult<ActivationTriggerRedefinitionResult>.Fail(
                evaluationResult.ErrorCode!, evaluationResult.Message!);

        var evaluation = evaluationResult.Payload!;
        if (!string.Equals(expectedRevision, evaluation.Preview.Revision, StringComparison.Ordinal))
            return AppResult<ActivationTriggerRedefinitionResult>.Fail(
                "activation_trigger_redefine_stale",
                "Activation trigger redefinition impact changed. Run the command again to review a fresh preview.");
        if (evaluation.Preview.RequiresConfirmation && !allowDeactivation)
            return AppResult<ActivationTriggerRedefinitionResult>.Fail(
                "activation_trigger_redefine_confirmation_required",
                "Activation trigger redefinition would make one or more eligible milestones inactive.");

        var prospectiveYaml = YamlSerde.Serialize(evaluation.ProspectiveConfig);
        try
        {
            persistence.WriteTextAtomic(prospectiveYaml);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult<ActivationTriggerRedefinitionResult>.Fail(
                "activation_trigger_redefine_failed",
                $"Activation trigger {evaluation.Preview.TriggerKey} could not be redefined: {exception.Message}");
        }

        if (!GlobalConfig.DryRun && !persistence.Reload())
        {
            if (!TryRestoreConfig(evaluation.OriginalYaml))
                return AppResult<ActivationTriggerRedefinitionResult>.Fail(
                    "activation_trigger_redefine_rollback_failed",
                    $"Activation trigger {evaluation.Preview.TriggerKey} could not be redefined and the previous configuration could not be restored.");

            return AppResult<ActivationTriggerRedefinitionResult>.Fail(
                "activation_trigger_redefine_failed",
                $"Activation trigger {evaluation.Preview.TriggerKey} could not be redefined; the previous definition and activation provenance were restored.");
        }

        var activation = evaluation.ProspectiveConfig.ActivationTriggers[evaluation.Preview.TriggerKey].Activation;
        return AppResult<ActivationTriggerRedefinitionResult>.Ok(new ActivationTriggerRedefinitionResult(
            evaluation.Preview.TriggerKey,
            activation != null,
            activation?.Mode,
            activation?.At,
            evaluation.Preview.Milestones.Select(milestone => milestone.MilestoneKey).ToList()));
    }

    public AppResult<ActivationTriggerMutationResult> AddTrigger(
        string key,
        string title,
        IReadOnlyList<ActivationRequirement> requirements)
    {
        var configResult = GetConfig();
        if (!configResult.Success) return ConfigFailure(configResult);
        var config = configResult.Payload!;

        key = key.Trim();
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "invalid_activation_trigger", "Activation trigger key and title are required.");
        if (config.ActivationTriggers.ContainsKey(key))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "duplicate_activation_trigger", $"Activation trigger {key} already exists.");

        return Mutate(key, [], prospective =>
        {
            prospective.ActivationTriggers[key] = new ActivationTriggerDefinition
            {
                Title = title,
                Requirements = CloneRequirements(requirements),
            };
        });
    }

    public AppResult<ActivationTriggerMutationResult> RenameTrigger(string key, string title)
    {
        var configResult = GetConfig();
        if (!configResult.Success) return ConfigFailure(configResult);

        key = key.Trim();
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "invalid_activation_trigger", "Activation trigger key and title are required.");
        if (!configResult.Payload!.ActivationTriggers.ContainsKey(key))
            return MissingTrigger(key);

        var affected = GetConsumers(configResult.Payload, key);
        return Mutate(key, affected, prospective => prospective.ActivationTriggers[key].Title = title);
    }

    public AppResult<ActivationTriggerMutationResult> SetRequirements(
        string key,
        IReadOnlyList<ActivationRequirement> requirements)
    {
        var configResult = GetConfig();
        if (!configResult.Success) return ConfigFailure(configResult);

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "invalid_activation_trigger", "Activation trigger key is required.");
        if (!configResult.Payload!.ActivationTriggers.TryGetValue(key, out var trigger))
            return MissingTrigger(key);
        if (trigger.Activation != null)
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "activation_trigger_active",
                $"Activation trigger {key} is active. Use the explicit redefine workflow to change its requirements.");

        var affected = GetConsumers(configResult.Payload, key);
        return Mutate(key, affected,
            prospective => prospective.ActivationTriggers[key].Requirements = CloneRequirements(requirements));
    }

    public AppResult<ActivationTriggerMutationResult> RemoveTrigger(string key)
    {
        var configResult = GetConfig();
        if (!configResult.Success) return ConfigFailure(configResult);

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "invalid_activation_trigger", "Activation trigger key is required.");
        if (!configResult.Payload!.ActivationTriggers.ContainsKey(key))
            return MissingTrigger(key);

        var consumers = GetConsumers(configResult.Payload, key);
        if (consumers.Count > 0)
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "activation_trigger_in_use",
                $"Activation trigger {key} is required by milestone(s): {string.Join(", ", consumers)}.");

        return Mutate(key, [], prospective => prospective.ActivationTriggers.Remove(key));
    }

    public AppResult<ActivationTriggerMutationResult> AttachTrigger(string key, string milestoneKey)
    {
        var configResult = GetConfig();
        if (!configResult.Success) return ConfigFailure(configResult);

        key = key.Trim();
        milestoneKey = milestoneKey.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(milestoneKey))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "invalid_activation_trigger", "Activation trigger and milestone keys are required.");
        if (!configResult.Payload!.ActivationTriggers.ContainsKey(key))
            return MissingTrigger(key);
        if (!configResult.Payload.Milestones.TryGetValue(milestoneKey, out var milestone))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "missing_milestone", $"Milestone {milestoneKey} not found.");
        if ((milestone.RequiredActivationTriggers ?? []).Contains(key, StringComparer.Ordinal))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "activation_trigger_already_attached",
                $"Milestone {milestoneKey} already requires activation trigger {key}.");

        return Mutate(key, [milestoneKey],
            prospective => prospective.Milestones[milestoneKey].RequiredActivationTriggers.Add(key));
    }

    public AppResult<ActivationTriggerMutationResult> DetachTrigger(string key, string milestoneKey)
    {
        var configResult = GetConfig();
        if (!configResult.Success) return ConfigFailure(configResult);

        key = key.Trim();
        milestoneKey = milestoneKey.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(milestoneKey))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "invalid_activation_trigger", "Activation trigger and milestone keys are required.");
        if (!configResult.Payload!.ActivationTriggers.ContainsKey(key))
            return MissingTrigger(key);
        if (!configResult.Payload.Milestones.TryGetValue(milestoneKey, out var milestone))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "missing_milestone", $"Milestone {milestoneKey} not found.");
        if (!(milestone.RequiredActivationTriggers ?? []).Contains(key, StringComparer.Ordinal))
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "activation_trigger_not_attached",
                $"Milestone {milestoneKey} does not require activation trigger {key}.");

        return Mutate(key, [milestoneKey],
            prospective => prospective.Milestones[milestoneKey].RequiredActivationTriggers.Remove(key));
    }

    private AppResult<RedefinitionEvaluation> EvaluateRedefinition(
        string key,
        IReadOnlyList<ActivationRequirement> requirements)
    {
        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult<RedefinitionEvaluation>.Fail(
                "invalid_activation_trigger", "Activation trigger key is required.");

        var stateResult = ReadCurrentActivationState();
        if (!stateResult.Success)
            return AppResult<RedefinitionEvaluation>.Fail(stateResult.ErrorCode!, stateResult.Message!);
        var state = stateResult.Payload!;
        var originalYaml = state.OriginalYaml;
        var config = state.Config;
        if (!config.ActivationTriggers.TryGetValue(key, out var trigger))
            return AppResult<RedefinitionEvaluation>.Fail(
                "missing_activation_trigger", $"Activation trigger {key} not found.");
        if (trigger.Activation == null)
            return AppResult<RedefinitionEvaluation>.Fail(
                "activation_trigger_inactive",
                $"Activation trigger {key} is inactive. Use set-requirements to change its requirements.");

        var tasksById = state.TasksById;
        var stateByTaskId = state.StateByTaskId;
        var normalizedRequirements = CloneRequirements(requirements);
        var revision = BuildRedefinitionRevision(
            key,
            normalizedRequirements,
            originalYaml,
            tasksById,
            stateByTaskId);
        var before = state.Snapshot;

        var prospective = ProjectConfig.Deserialize(originalYaml);
        var prospectiveTrigger = prospective.ActivationTriggers[key];
        prospectiveTrigger.Requirements = normalizedRequirements;
        prospectiveTrigger.Activation = null;

        if (FirstValidationError(validator.ValidateProspectiveConfig(prospective)) is { } definitionError)
            return AppResult<RedefinitionEvaluation>.Fail(definitionError.Code, definitionError.Message);

        var pending = resolver.Resolve(prospective, tasksById, stateByTaskId);
        var pendingTrigger = pending.ActivationTriggers.Single(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));
        if (pendingTrigger.RequirementsSatisfied)
        {
            prospectiveTrigger.Activation = new ActivationRecord
            {
                At = timeProvider.GetUtcNow(),
                Mode = ActivationMode.Automatic,
            };
        }

        if (FirstValidationError(validator.ValidateProspectiveConfig(prospective)) is { } activationError)
            return AppResult<RedefinitionEvaluation>.Fail(activationError.Code, activationError.Message);

        var after = resolver.Resolve(prospective, tasksById, stateByTaskId);
        var beforeTrigger = before.ActivationTriggers.Single(item =>
            string.Equals(item.Key, key, StringComparison.Ordinal));
        var beforeMilestones = before.Milestones.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var afterMilestones = after.Milestones.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var impacts = beforeTrigger.ConsumingMilestones
            .Order(StringComparer.Ordinal)
            .Select(milestoneKey =>
            {
                var beforeMilestone = beforeMilestones[milestoneKey];
                var afterMilestone = afterMilestones[milestoneKey];
                var currentlyEligibleTasks = IsEligibleLifecycle(beforeMilestone.Lifecycle)
                    ? tasksById.Values
                        .Where(task => string.Equals(task.Milestone, milestoneKey, StringComparison.Ordinal))
                        .Where(task => !stateByTaskId.TryGetValue(task.Id, out var state) ||
                                       !string.Equals(state, "done", StringComparison.Ordinal))
                        .Select(task => task.Id)
                        .Order(StringComparer.Ordinal)
                        .ToList()
                    : [];
                var losingEligibility = afterMilestone.Lifecycle == MilestoneLifecycle.Inactive
                    ? currentlyEligibleTasks
                    : [];
                return new ActivationTriggerMilestoneImpact(
                    milestoneKey,
                    beforeMilestone.Lifecycle,
                    afterMilestone.Lifecycle,
                    currentlyEligibleTasks,
                    losingEligibility);
            })
            .ToList();
        var requiresConfirmation = impacts.Any(impact =>
            IsEligibleLifecycle(impact.Before) && impact.After == MilestoneLifecycle.Inactive);
        var currentlyEligibleTaskIds = impacts
            .SelectMany(impact => impact.CurrentlyEligibleTaskIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var taskIdsLosingEligibility = impacts
            .SelectMany(impact => impact.TaskIdsLosingEligibility)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var preview = new ActivationTriggerRedefinitionPreview(
            key,
            revision,
            prospectiveTrigger.Activation?.Mode == ActivationMode.Automatic,
            requiresConfirmation,
            impacts,
            currentlyEligibleTaskIds,
            taskIdsLosingEligibility);

        return AppResult<RedefinitionEvaluation>.Ok(new RedefinitionEvaluation(
            preview,
            prospective,
            originalYaml));
    }

    private AppResult<CurrentActivationState> ReadCurrentActivationState()
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<CurrentActivationState>.Fail(
                "missing_project", "Project not found. Run pm init first.");

        try
        {
            if (!persistence.Reload())
                return AppResult<CurrentActivationState>.Fail(
                    "activation_trigger_config_reload_failed",
                    "Activation trigger configuration could not be reloaded.");

            var originalYaml = persistence.ReadText();
            var config = ProjectConfig.Deserialize(originalYaml);
            if (config.RequiresMilestoneSchemaMigration)
                return AppResult<CurrentActivationState>.Fail(
                    "milestone_schema_migration_required",
                    "Legacy milestone configuration must be migrated with pm doctor --fix before project settings can be changed.");

            var tasksById = projectRoot.GetAllTasks()
                .GroupBy(task => task.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var stateByTaskId = tasksById.Values.ToDictionary(
                task => task.Id,
                task => projectRoot.TryGetState(task, out var taskState) ? taskState : string.Empty,
                StringComparer.Ordinal);
            var snapshot = resolver.Resolve(config, tasksById, stateByTaskId);
            return AppResult<CurrentActivationState>.Ok(new CurrentActivationState(
                originalYaml,
                config,
                tasksById,
                stateByTaskId,
                snapshot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or YamlException)
        {
            return AppResult<CurrentActivationState>.Fail(
                "invalid_project", $"Project configuration could not be read: {exception.Message}");
        }
    }

    private AppResult<ResolvedActivationTrigger> PersistTransition(
        CurrentActivationState state,
        string key,
        Action<ProjectConfig> mutation)
    {
        var prospective = ProjectConfig.Deserialize(state.OriginalYaml);
        mutation(prospective);

        if (FirstValidationError(validator.ValidateProspectiveConfig(prospective)) is { } validationError)
            return AppResult<ResolvedActivationTrigger>.Fail(validationError.Code, validationError.Message);

        try
        {
            persistence.WriteTextAtomic(YamlSerde.Serialize(prospective));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult<ResolvedActivationTrigger>.Fail(
                "activation_trigger_transition_failed",
                $"Activation trigger {key} could not be changed: {exception.Message}");
        }

        if (GlobalConfig.DryRun)
        {
            var snapshot = resolver.Resolve(prospective, state.TasksById, state.StateByTaskId);
            return AppResult<ResolvedActivationTrigger>.Ok(snapshot.ActivationTriggers.Single(trigger =>
                string.Equals(trigger.Key, key, StringComparison.Ordinal)));
        }

        try
        {
            if (persistence.Reload())
            {
                var refreshed = resolver.ResolveCurrentProject();
                if (refreshed.Success)
                {
                    var refreshedTrigger = refreshed.Payload!.ActivationTriggers.SingleOrDefault(trigger =>
                        string.Equals(trigger.Key, key, StringComparison.Ordinal));
                    if (refreshedTrigger != null)
                        return AppResult<ResolvedActivationTrigger>.Ok(refreshedTrigger);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or YamlException)
        {
            // Restore the exact previous document below.
        }

        if (!TryRestoreConfig(state.OriginalYaml))
            return AppResult<ResolvedActivationTrigger>.Fail(
                "activation_trigger_transition_rollback_failed",
                $"Activation trigger {key} could not be changed and the previous configuration could not be restored.");

        return AppResult<ResolvedActivationTrigger>.Fail(
            "activation_trigger_transition_failed",
            $"Activation trigger {key} could not be changed; the previous activation provenance was restored.");
    }

    private AppResult<ProjectConfig> GetConfig()
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<ProjectConfig>.Fail("missing_project", "Project not found. Run pm init first.");
        if (projectRoot.Config.RequiresMilestoneSchemaMigration)
            return AppResult<ProjectConfig>.Fail(
                "milestone_schema_migration_required",
                "Legacy milestone configuration must be migrated with pm doctor --fix before project settings can be changed.");
        return AppResult<ProjectConfig>.Ok(projectRoot.Config);
    }

    private AppResult<ActivationTriggerMutationResult> Mutate(
        string key,
        IReadOnlyList<string> affectedMilestones,
        Action<ProjectConfig> mutation)
    {
        var configResult = GetConfig();
        if (!configResult.Success)
            return AppResult<ActivationTriggerMutationResult>.Fail(configResult.ErrorCode!, configResult.Message!);

        var prospective = ProjectConfig.Deserialize(YamlSerde.Serialize(configResult.Payload!));
        mutation(prospective);

        var validation = validator.ValidateProspectiveConfig(prospective);
        var error = validation.Issues.FirstOrDefault(issue =>
            string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
        if (error != null)
            return AppResult<ActivationTriggerMutationResult>.Fail(error.Code, error.Message);

        try
        {
            persistence.WriteTextAtomic(YamlSerde.Serialize(prospective));
            if (!GlobalConfig.DryRun && !persistence.Reload())
                return AppResult<ActivationTriggerMutationResult>.Fail(
                    "activation_trigger_config_reload_failed",
                    "Activation trigger configuration was written but could not be reloaded.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult<ActivationTriggerMutationResult>.Fail(
                "activation_trigger_write_failed",
                $"Activation trigger configuration could not be written: {exception.Message}");
        }

        return AppResult<ActivationTriggerMutationResult>.Ok(new ActivationTriggerMutationResult(
            key,
            affectedMilestones.Order(StringComparer.Ordinal).ToList()));
    }

    private static List<ActivationRequirement> CloneRequirements(
        IReadOnlyList<ActivationRequirement>? requirements) =>
        (requirements ?? [])
        .Select(requirement => new ActivationRequirement
        {
            Kind = requirement.Kind,
            Source = requirement.Source?.Trim() ?? string.Empty,
        })
        .ToList();

    private static IReadOnlyList<string> GetConsumers(ProjectConfig config, string key) =>
        config.Milestones
            .Where(milestone => (milestone.Value.RequiredActivationTriggers ?? [])
                .Contains(key, StringComparer.Ordinal))
            .Select(milestone => milestone.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

    private bool TryRestoreConfig(string yaml)
    {
        try
        {
            persistence.WriteTextAtomic(yaml);
            return persistence.Reload();
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return false;
        }
    }

    private static ProjectValidationIssue? FirstValidationError(ProjectValidationResult validation) =>
        validation.Issues.FirstOrDefault(issue =>
            string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    private static bool IsEligibleLifecycle(MilestoneLifecycle lifecycle) =>
        lifecycle is MilestoneLifecycle.Active or MilestoneLifecycle.ReadyToDeliver;

    private static string BuildRedefinitionRevision(
        string triggerKey,
        IReadOnlyList<ActivationRequirement> requirements,
        string yaml,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashValue(hash, "activation-trigger-redefinition");
        AppendHashValue(hash, triggerKey);
        foreach (var requirement in requirements)
        {
            AppendHashValue(hash, requirement.Kind.ToString());
            AppendHashValue(hash, requirement.Source);
        }
        AppendHashValue(hash, yaml);
        foreach (var task in tasksById.Values.OrderBy(task => task.Id, StringComparer.Ordinal))
        {
            AppendHashValue(hash, task.Id);
            AppendHashValue(hash, task.Milestone ?? string.Empty);
            AppendHashValue(hash, stateByTaskId.GetValueOrDefault(task.Id, string.Empty));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendHashValue(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private static AppResult<ActivationTriggerMutationResult> ConfigFailure(AppResult<ProjectConfig> result) =>
        AppResult<ActivationTriggerMutationResult>.Fail(result.ErrorCode!, result.Message!);

    private static AppResult<ActivationTriggerMutationResult> MissingTrigger(string key) =>
        AppResult<ActivationTriggerMutationResult>.Fail(
            "missing_activation_trigger", $"Activation trigger {key} not found.");

    private sealed record RedefinitionEvaluation(
        ActivationTriggerRedefinitionPreview Preview,
        ProjectConfig ProspectiveConfig,
        string OriginalYaml);

    private sealed record CurrentActivationState(
        string OriginalYaml,
        ProjectConfig Config,
        IReadOnlyDictionary<string, TaskItem> TasksById,
        IReadOnlyDictionary<string, string> StateByTaskId,
        MilestoneActivationSnapshot Snapshot);
}
