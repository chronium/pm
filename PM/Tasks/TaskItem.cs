using System.Text.RegularExpressions;

namespace PM.Tasks;

public partial record TaskItem
{
    private static readonly Regex FrontMatterPattern = FrontMatterRegex();
    public required string Id { get; init; }
    public required string Title { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; init; } = DateTime.UtcNow;

    public static TaskItem? Parse(string markdownContent)
    {
        var match = FrontMatterPattern.Match(markdownContent);
        return !match.Success ? null : YamlSerde.Deserialize<TaskItem>(match.Groups[1].Value);
    }

    [GeneratedRegex(@"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
}