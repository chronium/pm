using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PM.Application;
using PM.Project;
using PM.Web;

namespace PM.Site;

public sealed partial class SiteExportService(ProjectRoot projectRoot, SiteSnapshotBuilder snapshotBuilder)
{
    public const string DefaultOutput = "dist/pm-site";
    public const string SnapshotFileName = "pm-snapshot.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = true,
    };

    public async Task<AppResult<string>> BuildAsync(
        string? output,
        bool force,
        IAngularAssetStore assets,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        if (!projectRoot.Exists || projectRoot.RootPath == null)
            return AppResult<string>.Fail("missing_project", "Project not found. Run pm init first.");
        if (!assets.HasAssets)
            return AppResult<string>.Fail("missing_angular_assets",
                "Angular UI assets are not embedded. Build the web client and publish PM with EmbedAngularAssets=true.");

        var projectDirectory = Directory.GetParent(projectRoot.RootPath)!.FullName;
        var destination = Path.GetFullPath(string.IsNullOrWhiteSpace(output)
            ? Path.Combine(projectDirectory, DefaultOutput)
            : Path.IsPathRooted(output) ? output : Path.Combine(projectDirectory, output));
        var safetyError = ValidateDestination(projectDirectory, projectRoot.RootPath, destination);
        if (safetyError != null)
            return AppResult<string>.Fail("unsafe_site_output", safetyError);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any() && !force)
            return AppResult<string>.Fail("site_output_exists",
                $"Output directory '{destination}' is not empty. Use --force to replace it.");
        if (File.Exists(destination))
            return AppResult<string>.Fail("site_output_exists", $"Output path '{destination}' is an existing file.");

        var snapshotResult = await snapshotBuilder.BuildAsync(generatedAt, cancellationToken);
        if (!snapshotResult.Success)
            return AppResult<string>.Fail(snapshotResult.ErrorCode!, snapshotResult.Message!);

        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, $".{Path.GetFileName(destination)}.stage-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".{Path.GetFileName(destination)}.backup-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stage);
            CopyAssets(assets, stage, generatedAt, snapshotResult.Payload!.Project.Accent);
            File.WriteAllText(Path.Combine(stage, SnapshotFileName),
                SerializeSnapshot(snapshotResult.Payload!), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(stage, ".nojekyll"), string.Empty, new UTF8Encoding(false));

            var hadDestination = Directory.Exists(destination);
            if (hadDestination) Directory.Move(destination, backup);
            try
            {
                Directory.Move(stage, destination);
                if (hadDestination) Directory.Delete(backup, true);
            }
            catch
            {
                if (hadDestination && !Directory.Exists(destination) && Directory.Exists(backup))
                    Directory.Move(backup, destination);
                throw;
            }

            return AppResult<string>.Ok(destination);
        }
        catch (Exception exception)
        {
            return AppResult<string>.Fail("site_export_failed", $"Static site export failed: {exception.Message}");
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }

    public static string SerializeSnapshot(SiteSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions) + "\n";

    internal static string? ValidateDestination(string projectDirectory, string pmDirectory, string destination)
    {
        projectDirectory = Normalize(projectDirectory);
        pmDirectory = Normalize(pmDirectory);
        destination = Normalize(destination);

        if (destination == projectDirectory)
            return "The site output cannot be the project root.";
        if (IsSameOrDescendant(destination, pmDirectory))
            return "The site output cannot be .pm or one of its descendants.";
        if (IsSameOrDescendant(projectDirectory, destination))
            return "The site output cannot be an ancestor of the project.";
        if (IsExistingSymlink(destination))
            return "The site output cannot be an existing symlink.";
        return null;
    }

    private static void CopyAssets(
        IAngularAssetStore assets,
        string stage,
        DateTimeOffset generatedAt,
        string accent)
    {
        foreach (var path in assets.Paths.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!IsSafeAssetPath(path) || !assets.TryGet(path, out var asset))
                throw new InvalidOperationException($"Embedded Angular asset path '{path}' is invalid.");

            var target = Path.Combine(stage, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (path == "index.html")
            {
                var html = Encoding.UTF8.GetString(asset.Content);
                File.WriteAllText(target, PrepareIndex(html, generatedAt, accent), new UTF8Encoding(false));
            }
            else
            {
                File.WriteAllBytes(target, asset.Content);
            }
        }
    }

    private static string PrepareIndex(string html, DateTimeOffset generatedAt, string accent)
    {
        html = BaseElementRegex().IsMatch(html)
            ? BaseElementRegex().Replace(html, "<base href=\"./\">", 1)
            : html.Replace("<head>", "<head><base href=\"./\">", StringComparison.OrdinalIgnoreCase);
        html = ApplyAccent(html, accent);
        var metadata =
            $"<meta name=\"pm-site-mode\" content=\"static\"><meta name=\"pm-site-snapshot\" content=\"./{SnapshotFileName}\"><meta name=\"pm-site-generated-at\" content=\"{generatedAt.ToUniversalTime():O}\">";
        return html.Replace("</head>", metadata + "</head>", StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyAccent(string html, string accent)
    {
        if (!ProjectAccent.TryNormalize(accent, out var normalized))
            normalized = ProjectAccent.Default;

        return HtmlElementRegex().Replace(html, match =>
        {
            var htmlElement = AccentAttributeRegex().Replace(match.Value, string.Empty, 1);
            var encodedAccent = HtmlEncoder.Default.Encode(normalized);
            return htmlElement.Insert(htmlElement.Length - 1, $" data-accent=\"{encodedAccent}\"");
        }, 1);
    }

    private static bool IsSafeAssetPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        !path.Contains('\\') &&
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).All(part => part is not "." and not "..");

    private static bool IsExistingSymlink(string path) =>
        (Directory.Exists(path) || File.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsSameOrDescendant(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative == "." ||
               (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                relative != ".." && !Path.IsPathRooted(relative));
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    [GeneratedRegex("<base\\s+[^>]*href=[\\\"'][^\\\"']*[\\\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BaseElementRegex();

    [GeneratedRegex("<html(?:\\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlElementRegex();

    [GeneratedRegex("\\sdata-accent=[\\\"'][^\\\"']*[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex AccentAttributeRegex();
}
