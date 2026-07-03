using PM.Project;
using PM.Tasks;

namespace PM.Tests;

internal static class TestData
{
    public static ProjectConfig Config(
        string name = "Test Project",
        int idWidth = 4,
        string idPrefix = "PM",
        string nextIdServiceUrl = "http://ids.example.test")
    {
        return new ProjectConfig
        {
            Name = name,
            IdWidth = idWidth,
            IdPrefix = idPrefix,
            NextIdServiceUrl = nextIdServiceUrl,
            TaskStates = new()
            {
                ["todo"] = "Queued",
                ["review"] = "Review",
                ["done"] = "Done",
            },
        };
    }

    public static TaskItem Task(string id, string title)
    {
        return new TaskItem
        {
            Id = id,
            Title = title,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }
}
