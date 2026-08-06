using PM.Files;
using PM.Project;

namespace PM.Application;

public sealed record ProjectSettingsData(
    string ProjectName,
    string Accent,
    IReadOnlyList<BoardOption> Statuses,
    IReadOnlyList<BoardOption> Tracks,
    IReadOnlyList<BoardOption> Milestones);

public sealed class ProjectConfigService(ProjectRoot projectRoot)
{
    public AppResult<ProjectSettingsData> GetSettings()
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<ProjectSettingsData>.Fail("missing_project", "Project not found. Run pm init first.");

        var config = projectRoot.Config;
        return AppResult<ProjectSettingsData>.Ok(new ProjectSettingsData(
            config.Name,
            ProjectAccent.TryNormalize(config.Accent, out var accent) ? accent : ProjectAccent.Default,
            config.TaskStates.Select(status => new BoardOption(status.Key, status.Value)).ToList(),
            config.Tracks.Select(track => new BoardOption(track.Key, track.Value)).ToList(),
            config.Milestones
                .Select(milestone => new BoardOption(
                    milestone.Key,
                    milestone.Value.Title,
                    PriorityLevel.Resolve(config, milestone.Key)))
                .ToList()));
    }

    public AppResult SetAccent(string accent)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        if (!ProjectAccent.TryNormalize(accent, out var normalized))
            return AppResult.Fail("invalid_accent",
                $"Project accent must be one of {string.Join(", ", ProjectAccent.Values)}.");

        var config = projectRoot.Config!;
        config.Accent = normalized;
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult AddStatus(string key, string name)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            return AppResult.Fail("invalid_status", "Status key and name are required.");

        var config = projectRoot.Config!;
        if (config.TaskStates.ContainsKey(key))
            return AppResult.Fail("duplicate_status", $"Status {key} already exists.");

        config.TaskStates[key] = name;
        projectRoot.CreateTrackedStateDirectory(key);
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult RenameStatus(string key, string name)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            return AppResult.Fail("invalid_status", "Status key and name are required.");

        var config = projectRoot.Config!;
        if (!config.TaskStates.ContainsKey(key))
            return AppResult.Fail("missing_status", $"Status {key} not found.");

        config.TaskStates[key] = name;
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult RemoveStatus(string key)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult.Fail("invalid_status", "Status key is required.");

        var config = projectRoot.Config!;
        if (!config.TaskStates.ContainsKey(key))
            return AppResult.Fail("missing_status", $"Status {key} not found.");

        if (config.TaskStates.Count == 1)
            return AppResult.Fail("last_status", "Cannot remove the last status.");

        var statePath = Path.Combine(projectRoot.StatesPath, key);
        if (FileSystem.DirectoryExists(statePath))
        {
            if (FileSystem.ReadFiles(statePath, "*.ref").Count != 0)
                return AppResult.Fail("status_in_use", $"Status {key} is referenced by one or more tasks.");

            var otherFiles = FileSystem.ReadFiles(statePath)
                .Where(file => !string.Equals(file.Name, GlobalConfig.DirectoryPlaceholderFile,
                    StringComparison.Ordinal))
                .ToList();
            if (otherFiles.Count != 0)
                return AppResult.Fail("status_directory_not_empty",
                    $"Status {key} directory contains non-task files and cannot be removed.");
        }

        config.TaskStates.Remove(key);
        if (FileSystem.DirectoryExists(statePath))
        {
            var placeholderPath = Path.Combine(statePath, GlobalConfig.DirectoryPlaceholderFile);
            if (FileSystem.FileExists(placeholderPath))
                FileSystem.DeleteFile(placeholderPath);
            FileSystem.DeleteDirectory(statePath);
        }

        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult AddTrack(string key, string name)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            return AppResult.Fail("invalid_track", "Track key and name are required.");

        var config = projectRoot.Config!;
        if (config.Tracks.ContainsKey(key))
            return AppResult.Fail("duplicate_track", $"Track {key} already exists.");

        config.Tracks[key] = name;
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult RenameTrack(string key, string name)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            return AppResult.Fail("invalid_track", "Track key and name are required.");

        var config = projectRoot.Config!;
        if (!config.Tracks.ContainsKey(key))
            return AppResult.Fail("missing_track", $"Track {key} not found.");

        config.Tracks[key] = name;
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult RemoveTrack(string key)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult.Fail("invalid_track", "Track key is required.");

        var config = projectRoot.Config!;
        if (!config.Tracks.ContainsKey(key))
            return AppResult.Fail("missing_track", $"Track {key} not found.");

        if (config.Tracks.Count == 1)
            return AppResult.Fail("last_track", "Cannot remove the last track.");

        if (projectRoot.GetAllTasks().Any(task => projectRoot.ResolveTaskTrack(task) == key))
            return AppResult.Fail("track_in_use", $"Track {key} is referenced by one or more tasks.");

        config.Tracks.Remove(key);
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult RenameMilestone(string key, string title)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title))
            return AppResult.Fail("invalid_milestone", "Milestone key and title are required.");

        var config = projectRoot.Config!;
        if (!config.Milestones.ContainsKey(key))
            return AppResult.Fail("missing_milestone", $"Milestone {key} not found.");

        config.Milestones[key].Title = title;
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult AddMilestone(
        string key,
        string title,
        string? priority = null,
        string? description = null)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title))
            return AppResult.Fail("invalid_milestone", "Milestone key and title are required.");

        if (!PriorityLevel.TryNormalize(priority, out var normalizedPriority))
            return AppResult.Fail("invalid_priority",
                $"Milestone priority must be one of {string.Join(", ", PriorityLevel.Values)}.");

        var config = projectRoot.Config!;
        if (config.Milestones.ContainsKey(key))
            return AppResult.Fail("duplicate_milestone", $"Milestone {key} already exists.");

        config.Milestones[key] = new MilestoneDefinition
        {
            Title = title,
            Description = description ?? string.Empty,
            Priority = normalizedPriority,
        };

        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult SetMilestoneDescription(string key, string description)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult.Fail("invalid_milestone", "Milestone key is required.");

        var config = projectRoot.Config!;
        if (!config.Milestones.ContainsKey(key))
            return AppResult.Fail("missing_milestone", $"Milestone {key} not found.");

        config.Milestones[key].Description = description ?? string.Empty;
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult SetMilestonePriority(string key, string priority)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult.Fail("invalid_milestone", "Milestone key is required.");

        if (!PriorityLevel.TryNormalize(priority, out var normalizedPriority))
            return AppResult.Fail("invalid_priority",
                $"Milestone priority must be one of {string.Join(", ", PriorityLevel.Values)}.");

        var config = projectRoot.Config!;
        if (!config.Milestones.ContainsKey(key))
            return AppResult.Fail("missing_milestone", $"Milestone {key} not found.");

        config.Milestones[key].Priority = normalizedPriority;

        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult RemoveMilestone(string key)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");
        if (EnsureConfigMutationAllowed() is { } migrationError) return migrationError;

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult.Fail("invalid_milestone", "Milestone key is required.");

        var config = projectRoot.Config!;
        if (!config.Milestones.ContainsKey(key))
            return AppResult.Fail("missing_milestone", $"Milestone {key} not found.");

        if (projectRoot.GetAllTasks().Any(task => task.Milestone == key))
            return AppResult.Fail("milestone_in_use", $"Milestone {key} is referenced by one or more tasks.");

        var requiringTriggers = config.ActivationTriggers
            .Where(trigger => (trigger.Value.Requirements ?? []).Any(requirement =>
                requirement.Kind == ActivationRequirementKind.Milestone &&
                string.Equals(requirement.Source, key, StringComparison.Ordinal)))
            .Select(trigger => trigger.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (requiringTriggers.Count > 0)
            return AppResult.Fail(
                "activation_requirement_in_use",
                $"Milestone {key} is required by activation trigger(s): {string.Join(", ", requiringTriggers)}.");

        config.Milestones.Remove(key);
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult<bool> MigrateMilestoneSchema()
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<bool>.Fail("missing_project", "Project not found. Run pm init first.");

        var config = projectRoot.Config;
        if (!config.RequiresMilestoneSchemaMigration)
            return AppResult<bool>.Ok(false);

        foreach (var milestone in config.LegacyMilestonePriorities.Keys)
        {
            if (!config.Milestones.ContainsKey(milestone))
                return AppResult<bool>.Fail(
                    "unknown_milestone_priority",
                    $"Milestone priority references unknown milestone {milestone}.");
        }

        foreach (var (key, milestone) in config.Milestones)
        {
            if (!PriorityLevel.TryNormalize(milestone.Priority, out _))
                return AppResult<bool>.Fail(
                    "invalid_milestone_priority",
                    $"Milestone {key} has invalid priority {milestone.Priority}.");
        }

        try
        {
            config.WriteMigratedConfig(projectRoot);
            if (!projectRoot.TryReloadConfig())
                return AppResult<bool>.Fail(
                    "milestone_schema_migration_failed",
                    "Milestone configuration was written but could not be reloaded.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult<bool>.Fail(
                "milestone_schema_migration_failed",
                $"Unable to migrate milestone configuration: {exception.Message}");
        }

        return AppResult<bool>.Ok(true);
    }

    private AppResult? EnsureConfigMutationAllowed()
    {
        return projectRoot.Config?.RequiresMilestoneSchemaMigration == true
            ? AppResult.Fail(
                "milestone_schema_migration_required",
                "Legacy milestone configuration must be migrated with pm doctor --fix before project settings can be changed.")
            : null;
    }
}
