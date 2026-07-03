using PM.Project;

namespace PM.Application;

public sealed class ProjectConfigService(ProjectRoot projectRoot)
{
    public AppResult AddTrack(string key, string name)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

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

    public AppResult RemoveTrack(string key)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

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

    public AppResult AddMilestone(string key, string title)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        key = key.Trim();
        title = title.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title))
            return AppResult.Fail("invalid_milestone", "Milestone key and title are required.");

        var config = projectRoot.Config!;
        if (config.Milestones.ContainsKey(key))
            return AppResult.Fail("duplicate_milestone", $"Milestone {key} already exists.");

        config.Milestones[key] = title;
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }

    public AppResult RemoveMilestone(string key)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult.Fail("invalid_milestone", "Milestone key is required.");

        var config = projectRoot.Config!;
        if (!config.Milestones.ContainsKey(key))
            return AppResult.Fail("missing_milestone", $"Milestone {key} not found.");

        if (projectRoot.GetAllTasks().Any(task => task.Milestone == key))
            return AppResult.Fail("milestone_in_use", $"Milestone {key} is referenced by one or more tasks.");

        config.Milestones.Remove(key);
        config.WriteConfig(projectRoot);
        return AppResult.Ok();
    }
}
