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
    public ProjectRoot()
    {
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
        await Task.CompletedTask;
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

    public void UpdateTaskState(TaskItem task, string state)
    {
        if (TryGetState(task, out var currentState))
            FileSystem.DeleteFile(Path.Combine(StatesPath, currentState, $"{task.Id}.ref"));

        var stateDir = Path.Combine(StatesPath, state);
        var stateRelativePath = Path.GetRelativePath(stateDir, TasksPath);

        FileSystem.WriteAllText(Path.Combine(StatesPath, state, $"{task.Id}.ref"),
            $"{stateRelativePath}/{task.Id}.{GlobalConfig.DefaultTaskExtension}");
    }

    public bool TryGetState(TaskItem task, [MaybeNullWhen(false)] out string state)
    {
        state = null;
        foreach (var key in Config!.TaskStates.Keys)
        {
            var statePath = Path.Combine(StatesPath, key, $"{task.Id}.ref");
            if (FileSystem.FileExists(statePath))
            {
                state = key;
                return true;
            }
        }

        return false;
    }

    public List<TaskItem> GetTasksInState(string state)
    {
        var statePath = Path.Combine(StatesPath, state);
        if (!FileSystem.DirectoryExists(statePath)) return [];

        var items = new List<TaskItem>();

        foreach (var refFile in FileSystem.ReadFiles(statePath, "*.ref"))
        {
            var item = TaskItem.Parse(FileSystem.ReadAllText(ResolveRef(refFile)));
            if (item == null)
                continue;

            items.Add(item);
        }

        return items;
    }

    public bool TryGetById(string id, [MaybeNullWhen(false)] out TaskItem task)
    {
        task = null;
        var taskPath = Path.Combine(TasksPath, $"{id}.{GlobalConfig.DefaultTaskExtension}");
        if (!FileSystem.FileExists(taskPath)) return false;

        task = TaskItem.Parse(FileSystem.ReadAllText(taskPath));
        return task != null;
    }

    private static string ResolveRef(FileInfo refFile)
    {
        var refContent = FileSystem.ReadAllText(refFile.FullName);
        return Path.Combine(refFile.Directory!.FullName, refContent);
    }
}
