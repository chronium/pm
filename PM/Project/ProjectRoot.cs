using System.Diagnostics.CodeAnalysis;
using System.Text;
using PM.Files;
using PM.Tasks;

namespace PM.Project;

public interface IProjectRoot
{
    bool Exists { get; }
    string? RootPath { get; }

    ProjectConfig? Config { get; }

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

        if (Exists)
            Config = ProjectConfig.ReadConfig(this);
    }

    public string TasksPath => Path.Combine(RootPath!, GlobalConfig.TasksDirName);
    public string StatesPath => Path.Combine(RootPath!, GlobalConfig.StatesDirName);

    public bool Exists { get; private set; }
    public string RootPath { get; private set; }

    public ProjectConfig? Config { get; private set; }

    private static bool TryFindProjectRoot([MaybeNullWhen(false)] out string projectRoot)
    {
        projectRoot = null;

        var currentDir = Environment.CurrentDirectory;
        while (true)
        {
            var pmDirPath = Path.Combine(currentDir, GlobalConfig.PmDirName);
            if (Directory.Exists(pmDirPath))
            {
                projectRoot = pmDirPath;
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

    private void CreateProjectDirectories()
    {
        if (RootPath == null) throw new InvalidOperationException("Project root path is not set.");

        FileSystem.CreateDirectory(TasksPath);
        FileSystem.CreateDirectory(StatesPath);

        WriteStatesDirectories();
    }

    private void WriteStatesDirectories()
    {
        if (RootPath == null) throw new InvalidOperationException("Project root path is not set.");

        foreach (var key in Config!.TaskStates.Keys)
            FileSystem.CreateDirectory(Path.Combine(StatesPath, key));
    }

    public async Task CreateProject(ProjectConfig config, CancellationToken cancellationToken = default)
    {
        Config = config;

        CreateProjectRoot(Directory.GetCurrentDirectory());
        CreateProjectDirectories();

        config.WriteConfig(this);
        await _nextIdService.PeekNextId(this, cancellationToken);
    }

    public void WriteTask(TaskItem task)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine(YamlSerde.Serialize(task));
        sb.AppendLine("---");

        var taskFilePath = Path.Combine(TasksPath, $"{task.Id}.{GlobalConfig.DefaultTaskExtension}");
        FileSystem.WriteAllText(taskFilePath, sb.ToString());
    }
}