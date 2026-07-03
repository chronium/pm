using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace PM.Tasks;

public partial record TaskItem
{
    private static readonly Regex FrontMatterPattern = FrontMatterRegex();
    public required string Id { get; init; }
    public required string Title { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; init; } = DateTime.UtcNow;

    [YamlIgnore]
    public string Description { get; init; } = string.Empty;

    public static TaskItem? Parse(string markdownContent)
    {
        var match = FrontMatterPattern.Match(markdownContent);
        if (!match.Success) return null;

        TaskItem? task;
        try
        {
            task = YamlSerde.Deserialize<TaskItem>(match.Groups["yaml"].Value);
        }
        catch
        {
            return null;
        }

        if (task == null || string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.Title))
            return null;

        return task with { Description = NormalizeBody(match.Groups["body"].Value) };
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
