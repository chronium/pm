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
}
