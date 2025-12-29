using System.Diagnostics.CodeAnalysis;
using PM.Files;
using PM.Tasks;

namespace PM.Project;

public interface IProjectRoot
{
    bool Exists { get; }
    string? RootPath { get; }

    string TasksPath { get; }
    string StatesPath { get; }
}

public class ProjectRoot : IProjectRoot
{
    private readonly INextIdService _nextIdService;

    public ProjectRoot(INextIdService nextIdService)
    {
        _nextIdService = nextIdService;

        Exists = TryFindProjectRoot(out var rootPath);
        RootPath = rootPath!;
    }

    public string TasksPath => Path.Combine(RootPath!, GlobalConfig.TasksDirName);
    public string StatesPath => Path.Combine(RootPath!, GlobalConfig.StatesDirName);

    public bool Exists { get; private set; }
    public string RootPath { get; private set; }

    private static bool TryFindProjectRoot([MaybeNullWhen(false)] out string projectRoot)
    {
        projectRoot = null;

        var currentDir = Environment.CurrentDirectory;
        while (true)
        {
            var pmDirPath = Path.Combine(currentDir, GlobalConfig.PmDirName);
            if (Directory.Exists(pmDirPath))
            {
                projectRoot = currentDir;
                return true;
            }

            currentDir = Path.GetDirectoryName(currentDir);
            if (currentDir == null) break;
        }

        return false;
    }

    private void CreateProjectRoot(string projectRoot)
    {
        var rootDir = Path.Combine(projectRoot, GlobalConfig.PmDirName);
        FileSystem.CreateDirectory(rootDir);
        RootPath = rootDir;
        Exists = true;
    }

    private void CreateProjectDirectories(ProjectConfig config)
    {
        if (RootPath == null) throw new InvalidOperationException("Project root path is not set.");

        FileSystem.CreateDirectory(TasksPath);
        FileSystem.CreateDirectory(StatesPath);

        WriteStatesDirectories(config);
    }

    private void WriteStatesDirectories(ProjectConfig config)
    {
        if (RootPath == null) throw new InvalidOperationException("Project root path is not set.");

        foreach (var key in config.TaskStates.Keys)
            FileSystem.CreateDirectory(Path.Combine(StatesPath, key));
    }

    public async Task CreateProject(ProjectConfig config, CancellationToken cancellationToken = default)
    {
        CreateProjectRoot(Directory.GetCurrentDirectory());
        CreateProjectDirectories(config);

        config.WriteConfig(this);
        await _nextIdService.GetNextId(this, cancellationToken);
    }
}