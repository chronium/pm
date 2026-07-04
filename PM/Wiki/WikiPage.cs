using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace PM.Wiki;

public sealed partial record WikiPage
{
    private static readonly Regex FrontMatterPattern = FrontMatterRegex();

    [YamlIgnore]
    public required string Path { get; init; }

    public required string Title { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; init; } = DateTime.UtcNow;

    [YamlIgnore]
    public string Body { get; init; } = string.Empty;

    public static WikiPage? Parse(string path, string markdownContent)
    {
        var match = FrontMatterPattern.Match(markdownContent);
        if (!match.Success) return null;

        WikiPageFrontmatter? frontmatter;
        try
        {
            frontmatter = YamlSerde.Deserialize<WikiPageFrontmatter>(match.Groups["yaml"].Value);
        }
        catch
        {
            return null;
        }

        if (frontmatter == null ||
            string.IsNullOrWhiteSpace(frontmatter.Title) ||
            frontmatter.CreatedAt == null ||
            frontmatter.ModifiedAt == null)
            return null;

        return new WikiPage
        {
            Path = path,
            Title = frontmatter.Title,
            CreatedAt = frontmatter.CreatedAt.Value,
            ModifiedAt = frontmatter.ModifiedAt.Value,
            Body = NormalizeBody(match.Groups["body"].Value),
        };
    }

    public string ToMarkdown()
    {
        var yaml = YamlSerde.Serialize(this);
        return string.IsNullOrWhiteSpace(Body)
            ? $"---\n{yaml}---\n\n"
            : $"---\n{yaml}---\n\n{Body}";
    }

    private static string NormalizeBody(string body)
    {
        if (body.StartsWith("\r\n", StringComparison.Ordinal)) return body[2..];
        if (body.StartsWith('\n')) return body[1..];
        return body;
    }

    [GeneratedRegex(@"\A---[ \t]*\r?\n(?<yaml>.*?)\r?\n---[ \t]*(?:\r?\n|$)(?<body>.*)\z", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();

    private sealed record WikiPageFrontmatter
    {
        public string? Title { get; init; }
        public DateTime? CreatedAt { get; init; }
        public DateTime? ModifiedAt { get; init; }
    }
}
