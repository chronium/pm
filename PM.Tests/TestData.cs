using PM.Project;
using PM.Tasks;

namespace PM.Tests;

internal static class TestData
{
    public static ProjectConfig Config(
        string name = "Test Project",
        int idWidth = 4,
        string idPrefix = "PM",
        string nextIdServiceUrl = "http://ids.example.test",
        Dictionary<string, string>? tracks = null,
        Dictionary<string, string>? milestones = null)
    {
        return new ProjectConfig
        {
            Name = name,
            IdWidth = idWidth,
            IdPrefix = idPrefix,
            NextIdServiceUrl = nextIdServiceUrl,
            Tracks = tracks ?? new Dictionary<string, string> { [idPrefix] = idPrefix },
            Milestones = milestones ?? new Dictionary<string, string>(),
            TaskStates = new()
            {
                ["todo"] = "Queued",
                ["review"] = "Review",
                ["done"] = "Done",
            },
        };
    }

    public static TaskItem Task(
        string id,
        string title,
        string description = "",
        string? track = "PM",
        string? milestone = null)
    {
        return new TaskItem
        {
            Id = id,
            Title = title,
            Track = track,
            Milestone = milestone,
            Description = description,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }
}
