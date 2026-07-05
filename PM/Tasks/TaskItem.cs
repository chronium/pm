using System.Text.RegularExpressions;
using PM.Project;
using YamlDotNet.Serialization;

namespace PM.Tasks;

public partial record TaskItem
{
    private static readonly Regex FrontMatterPattern = FrontMatterRegex();
    public required string Id { get; init; }
    public required string Title { get; init; }
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Track { get; init; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Milestone { get; init; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Priority { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; init; } = DateTime.UtcNow;

    [YamlIgnore]
    public string Description { get; init; } = string.Empty;

    public static TaskItem? Parse(string markdownContent)
    {
        return TryParse(markdownContent, out var task, out _, out _) ? task : null;
    }

    public static bool TryParse(
        string markdownContent,
        out TaskItem? task,
        out string errorCode,
        out string message)
    {
        var match = FrontMatterPattern.Match(markdownContent);
        if (!match.Success)
        {
            task = null;
            errorCode = "invalid_task_markdown";
            message = "Task file has invalid frontmatter or body.";
            return false;
        }

        try
        {
            task = YamlSerde.Deserialize<TaskItem>(match.Groups["yaml"].Value);
        }
        catch
        {
            task = null;
            errorCode = "invalid_task_markdown";
            message = "Task file has invalid frontmatter or body.";
            return false;
        }

        if (task == null || string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.Title))
        {
            task = null;
            errorCode = "invalid_task_markdown";
            message = "Task file has invalid frontmatter or body.";
            return false;
        }

        if (!PriorityLevel.TryNormalizeTaskOverride(task.Priority, out var normalizedPriority))
        {
            errorCode = "invalid_task_priority";
            message = $"Task {task.Id} has invalid priority {task.Priority}.";
            task = null;
            return false;
        }

        task = task with
        {
            Priority = normalizedPriority,
            Description = NormalizeBody(match.Groups["body"].Value),
        };
        errorCode = string.Empty;
        message = string.Empty;
        return true;
    }

    public string ToMarkdown()
    {
        var yaml = YamlSerde.Serialize(this);
        return string.IsNullOrWhiteSpace(Description)
            ? $"---\n{yaml}---\n\n"
            : $"---\n{yaml}---\n\n{Description}";
    }

    private static string NormalizeBody(string body)
    {
        if (body.StartsWith("\r\n", StringComparison.Ordinal)) return body[2..];
        if (body.StartsWith('\n')) return body[1..];
        return body;
    }

    [GeneratedRegex(@"\A---[ \t]*\r?\n(?<yaml>.*?)\r?\n---[ \t]*(?:\r?\n|$)(?<body>.*)\z", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
}
