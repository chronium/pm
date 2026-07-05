namespace PM.Project;

using PM.Tasks;

public sealed record PriorityResolution(string Priority, string Source);

public static class PriorityLevel
{
    public const string None = "none";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Urgent = "urgent";
    public const string SourceTask = "task";
    public const string SourceMilestone = "milestone";
    public const string SourceNone = "none";

    public static readonly IReadOnlyList<string> Values = [None, Low, Medium, High, Urgent];

    public static bool TryNormalize(string? value, out string priority)
    {
        priority = string.IsNullOrWhiteSpace(value)
            ? None
            : value.Trim().ToLowerInvariant();

        return Values.Contains(priority, StringComparer.Ordinal);
    }

    public static bool TryNormalizeTaskOverride(string? value, out string? priority)
    {
        priority = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!TryNormalize(value, out var normalized))
            return false;

        priority = normalized;
        return true;
    }

    public static bool TryNormalizePatchValue(string? value, out string? priority)
    {
        priority = null;
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), "inherit", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!TryNormalize(value, out var normalized))
            return false;

        priority = normalized;
        return true;
    }

    public static string Resolve(ProjectConfig config, string? milestone)
    {
        return ResolveMilestoneDefault(config, milestone);
    }

    public static string ResolveMilestoneDefault(ProjectConfig config, string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone) ||
            !config.MilestonePriorities.TryGetValue(milestone, out var configured) ||
            !TryNormalize(configured, out var priority))
            return None;

        return priority;
    }

    public static PriorityResolution Resolve(ProjectConfig config, TaskItem task)
    {
        if (!string.IsNullOrWhiteSpace(task.Priority) &&
            TryNormalize(task.Priority, out var taskPriority))
            return new PriorityResolution(taskPriority, SourceTask);

        var milestonePriority = ResolveMilestoneDefault(config, task.Milestone);
        return string.Equals(milestonePriority, None, StringComparison.Ordinal)
            ? new PriorityResolution(None, SourceNone)
            : new PriorityResolution(milestonePriority, SourceMilestone);
    }

    public static int Rank(string priority)
    {
        return priority switch
        {
            Urgent => 4,
            High => 3,
            Medium => 2,
            Low => 1,
            _ => 0,
        };
    }
}
