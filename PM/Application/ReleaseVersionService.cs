using PM.Files;
using PM.Project;

namespace PM.Application;

public sealed record ReleaseVersionState(bool Enabled, ReleaseVersion? Version);

public sealed class ReleaseVersionService(ProjectRoot projectRoot)
{
    public AppResult<ReleaseVersionState> Read()
    {
        if (!projectRoot.Exists || projectRoot.RootPath == null)
            return AppResult<ReleaseVersionState>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!FileSystem.FileExists(projectRoot.ReleaseVersionPath))
            return AppResult<ReleaseVersionState>.Ok(new ReleaseVersionState(false, null));

        string content;
        try
        {
            content = FileSystem.ReadAllText(projectRoot.ReleaseVersionPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult<ReleaseVersionState>.Fail(
                "release_version_unreadable",
                $"Release version could not be read: {exception.Message}");
        }

        if (!ReleaseVersion.TryParse(content, out var version, out var error))
            return AppResult<ReleaseVersionState>.Fail("invalid_release_version", error!);

        return AppResult<ReleaseVersionState>.Ok(new ReleaseVersionState(true, version));
    }
}
