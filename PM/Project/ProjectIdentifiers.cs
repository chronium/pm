using System.Text.RegularExpressions;

namespace PM.Project;

public static partial class ProjectIdentifiers
{
    public const int MaximumLength = 256;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaximumLength } && ProjectIdPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIdPattern();
}
