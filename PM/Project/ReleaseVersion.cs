using System.Globalization;
using System.Text.RegularExpressions;

namespace PM.Project;

public sealed partial record ReleaseVersion(int Major, int Minor, int Patch)
{
    public const int MaximumComponent = 65534;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public bool TryNextPatch(out ReleaseVersion? next) =>
        TryCreate(Major, Minor, Patch + 1, out next);

    public bool TryNextMinor(out ReleaseVersion? next) =>
        TryCreate(Major, Minor + 1, 0, out next);

    public bool TryNextMajor(out ReleaseVersion? next) =>
        TryCreate(Major + 1, 0, 0, out next);

    private static bool TryCreate(int major, int minor, int patch, out ReleaseVersion? version)
    {
        version = null;
        if (major > MaximumComponent || minor > MaximumComponent || patch > MaximumComponent)
            return false;
        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    public static bool TryParse(string content, out ReleaseVersion? version, out string? error)
    {
        version = null;
        error = null;

        var match = CanonicalPattern().Match(content);
        if (!match.Success)
        {
            error = "Release version must contain exactly three non-negative numeric components in canonical major.minor.patch form, followed by at most one newline.";
            return false;
        }

        if (!TryParseComponent(match.Groups["major"].Value, out var major) ||
            !TryParseComponent(match.Groups["minor"].Value, out var minor) ||
            !TryParseComponent(match.Groups["patch"].Value, out var patch))
        {
            error = $"Release version components must be between 0 and {MaximumComponent}.";
            return false;
        }

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    private static bool TryParseComponent(string value, out int component) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out component) &&
        component <= MaximumComponent;

    [GeneratedRegex("\\A(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?:\\r?\\n)?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPattern();
}
