using PM.Project;

namespace PM.Application;

public sealed record ActivationTriggerMutationResult(
    string TriggerKey,
    IReadOnlyList<string> AffectedMilestones);

public sealed class ActivationTriggerService
{
    private readonly ProjectRoot projectRoot;
    private readonly MilestoneActivationResolver resolver;
    private readonly MilestoneActivationValidationService validator;

    public ActivationTriggerService(
        ProjectRoot projectRoot,
        MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validator)
    {
        this.projectRoot = projectRoot;
        this.resolver = resolver;
        this.validator = validator;
    }

    public AppResult<IReadOnlyList<ResolvedActivationTrigger>> ListTriggers()
    {
        var snapshot = resolver.ResolveCurrentProject();
        return snapshot.Success
            ? AppResult<IReadOnlyList<ResolvedActivationTrigger>>.Ok(snapshot.Payload!.ActivationTriggers)
            : AppResult<IReadOnlyList<ResolvedActivationTrigger>>.Fail(snapshot.ErrorCode!, snapshot.Message!);
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
            prospective.WriteConfigAtomic(projectRoot);
            if (!GlobalConfig.DryRun && !projectRoot.TryReloadConfig())
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

    private static AppResult<ActivationTriggerMutationResult> ConfigFailure(AppResult<ProjectConfig> result) =>
        AppResult<ActivationTriggerMutationResult>.Fail(result.ErrorCode!, result.Message!);

    private static AppResult<ActivationTriggerMutationResult> MissingTrigger(string key) =>
        AppResult<ActivationTriggerMutationResult>.Fail(
            "missing_activation_trigger", $"Activation trigger {key} not found.");
}
