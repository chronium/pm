using PM.Project;

namespace PM.Tests;

internal sealed class TempWorkingDirectory : IDisposable
{
    private readonly string _previousDirectory = Environment.CurrentDirectory;

    public TempWorkingDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
        Environment.CurrentDirectory = Path;
    }

    public string Path { get; }

    public async Task<ProjectRoot> CreateProject(ProjectConfig? config = null)
    {
        var projectRoot = new ProjectRoot();
        await projectRoot.CreateProject(config ?? TestData.Config());
        return projectRoot;
    }

    public void Dispose()
    {
        Environment.CurrentDirectory = _previousDirectory;
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}
