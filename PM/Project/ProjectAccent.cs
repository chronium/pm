namespace PM.Project;

public static class ProjectAccent
{
    public const string Default = "teal";

    public static readonly IReadOnlyList<string> Values =
        [Default, "blue", "purple", "rose", "amber", "neutral"];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return Values.Contains(normalized, StringComparer.Ordinal);
    }
}
