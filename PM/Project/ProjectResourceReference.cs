using System.Text;

namespace PM.Project;

public enum ProjectResourceKind
{
    Task,
    Wiki,
}

public sealed record ProjectResourceReference
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private ProjectResourceReference(string projectId, ProjectResourceKind kind, string resourcePath)
    {
        ProjectId = projectId;
        Kind = kind;
        ResourcePath = resourcePath;
    }

    public string ProjectId { get; }
    public ProjectResourceKind Kind { get; }
    public string ResourcePath { get; }

    public static bool LooksLikeReference(string? value) =>
        value?.TrimStart().StartsWith("pm:", StringComparison.OrdinalIgnoreCase) == true;

    public static bool TryCreate(
        string projectId,
        ProjectResourceKind kind,
        string resourcePath,
        out ProjectResourceReference? reference,
        out string message)
    {
        reference = null;
        if (!ProjectIdentifiers.IsValid(projectId))
            return Fail("Project ID is invalid.", out message);

        var segments = resourcePath.Split('/');
        if (segments.Length == 0 || segments.Any(segment => !IsValidResourceSegment(segment)))
            return Fail("Resource path contains an invalid segment.", out message);

        if (kind == ProjectResourceKind.Task && segments.Length != 1)
            return Fail("Task references require exactly one task ID segment.", out message);

        if (kind == ProjectResourceKind.Wiki &&
            segments[^1].EndsWith($".{GlobalConfig.DefaultTaskExtension}", StringComparison.OrdinalIgnoreCase))
            return Fail("Wiki references omit the Markdown file extension.", out message);

        reference = new ProjectResourceReference(projectId, kind, string.Join('/', segments));
        message = string.Empty;
        return true;
    }

    public static bool TryParse(
        string? value,
        out ProjectResourceReference? reference,
        out string message)
    {
        reference = null;
        var input = value?.Trim() ?? string.Empty;
        if (!input.StartsWith("pm://", StringComparison.OrdinalIgnoreCase))
            return Fail("Project reference must use the pm:// scheme.", out message);
        if (input.Contains('?') || input.Contains('#'))
            return Fail("Project references cannot contain a query or fragment.", out message);

        var authorityStart = "pm://".Length;
        var pathStart = input.IndexOf('/', authorityStart);
        if (pathStart < 0 ||
            !string.Equals(input[authorityStart..pathStart], "project", StringComparison.OrdinalIgnoreCase))
            return Fail("Project reference authority must be project.", out message);

        var rawSegments = input[(pathStart + 1)..].Split('/');
        if (rawSegments.Length < 3 || rawSegments.Any(string.IsNullOrEmpty))
            return Fail("Project reference path is incomplete.", out message);
        if (!TryDecodeSegment(rawSegments[0], out var projectId) || !ProjectIdentifiers.IsValid(projectId))
            return Fail("Project reference contains an invalid project ID.", out message);

        var kind = rawSegments[1] switch
        {
            "task" => ProjectResourceKind.Task,
            "wiki" => ProjectResourceKind.Wiki,
            _ => (ProjectResourceKind?)null,
        };
        if (kind == null)
            return Fail("Project reference resource kind must be task or wiki.", out message);

        var decoded = new List<string>();
        foreach (var rawSegment in rawSegments.Skip(2))
        {
            if (!TryDecodeSegment(rawSegment, out var segment) || !IsValidResourceSegment(segment))
                return Fail("Project reference contains an invalid resource segment.", out message);
            decoded.Add(segment);
        }

        return TryCreate(projectId, kind.Value, string.Join('/', decoded), out reference, out message);
    }

    public string ToCanonicalUri()
    {
        var kind = Kind == ProjectResourceKind.Task ? "task" : "wiki";
        var resource = string.Join('/', ResourcePath.Split('/').Select(Uri.EscapeDataString));
        return $"pm://project/{Uri.EscapeDataString(ProjectId)}/{kind}/{resource}";
    }

    public override string ToString() => ToCanonicalUri();

    private static bool TryDecodeSegment(string rawSegment, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            var bytes = new List<byte>();
            var literalStart = 0;
            for (var index = 0; index < rawSegment.Length;)
            {
                if (rawSegment[index] != '%')
                {
                    index++;
                    continue;
                }

                if (index + 2 >= rawSegment.Length ||
                    !byte.TryParse(rawSegment.AsSpan(index + 1, 2),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var escaped))
                    return false;

                if (index > literalStart)
                    bytes.AddRange(Encoding.UTF8.GetBytes(rawSegment[literalStart..index]));
                bytes.Add(escaped);
                index += 3;
                literalStart = index;
            }

            if (literalStart < rawSegment.Length)
                bytes.AddRange(Encoding.UTF8.GetBytes(rawSegment[literalStart..]));
            decoded = StrictUtf8.GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsValidResourceSegment(string segment) =>
        !string.IsNullOrWhiteSpace(segment) &&
        segment is not "." and not ".." &&
        !segment.Contains('/') &&
        !segment.Contains('\\') &&
        !segment.Any(char.IsControl);

    private static bool Fail(string failure, out string message)
    {
        message = failure;
        return false;
    }
}
