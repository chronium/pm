using System.Diagnostics.CodeAnalysis;
using PM.Files;
using PM.Tasks;
using PM.Wiki;

namespace PM.Project;

public interface IProjectRoot
{
    bool Exists { get; }
    string? RootPath { get; }

    ProjectConfig? Config { get; }

    string TasksPath { get; }
    string StatesPath { get; }
    string WikiPath { get; }
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
    public string WikiPath => Path.Combine(RootPath!, GlobalConfig.WikiDirName);

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
        FileSystem.CreateDirectory(WikiPath);

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
        WriteTaskFile(task.Id, task.ToMarkdown());
    }

    public bool TryReadTaskFile(string id, [MaybeNullWhen(false)] out string content)
    {
        content = null;
        var taskPath = GetTaskPath(id);
        if (!FileSystem.FileExists(taskPath)) return false;

        content = FileSystem.ReadAllText(taskPath);
        return true;
    }

    public void WriteTaskFile(string id, string content)
    {
        FileSystem.WriteAllText(GetTaskPath(id), content);
    }

    public void DeleteTask(TaskItem task)
    {
        foreach (var key in Config!.TaskStates.Keys)
        {
            var refPath = Path.Combine(StatesPath, key, $"{task.Id}.ref");
            if (FileSystem.FileExists(refPath))
                FileSystem.DeleteFile(refPath);
        }

        FileSystem.DeleteFile(GetTaskPath(task.Id));
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

    public List<TaskItem> GetAllTasks()
    {
        if (!FileSystem.DirectoryExists(TasksPath)) return [];

        var items = new List<TaskItem>();
        foreach (var taskFile in FileSystem.ReadFiles(TasksPath, $"*.{GlobalConfig.DefaultTaskExtension}"))
        {
            var item = TaskItem.Parse(FileSystem.ReadAllText(taskFile.FullName));
            if (item == null)
                continue;

            items.Add(item);
        }

        return items;
    }

    public string ResolveTaskTrack(TaskItem task)
    {
        return string.IsNullOrWhiteSpace(task.Track) ? Config!.DefaultTrackKey : task.Track;
    }

    public bool TryGetById(string id, [MaybeNullWhen(false)] out TaskItem task)
    {
        task = null;
        var taskPath = GetTaskPath(id);
        if (!FileSystem.FileExists(taskPath)) return false;

        task = TaskItem.Parse(FileSystem.ReadAllText(taskPath));
        return task != null;
    }

    private string GetTaskPath(string id)
    {
        return Path.Combine(TasksPath, $"{id}.{GlobalConfig.DefaultTaskExtension}");
    }

    public string GetTaskFilePath(string id)
    {
        return GetTaskPath(id);
    }

    public bool TryResolveWikiPath(string pagePath, [MaybeNullWhen(false)] out string normalizedPath,
        [MaybeNullWhen(false)] out string filePath)
    {
        normalizedPath = null;
        filePath = null;

        if (string.IsNullOrWhiteSpace(pagePath) || RootPath == null) return false;

        var trimmed = pagePath.Trim();
        if (Path.IsPathRooted(trimmed) || trimmed.Contains('\\')) return false;

        var segments = trimmed.Split('/');
        if (segments.Length == 0 || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." ||
                segment.Contains(Path.DirectorySeparatorChar) ||
                segment.Contains(Path.AltDirectorySeparatorChar)))
            return false;

        var last = segments[^1];
        var extension = Path.GetExtension(last);
        if (!string.IsNullOrEmpty(extension))
        {
            if (!string.Equals(extension, $".{GlobalConfig.DefaultTaskExtension}", StringComparison.OrdinalIgnoreCase))
                return false;

            last = Path.GetFileNameWithoutExtension(last);
            if (string.IsNullOrWhiteSpace(last) || last is "." or "..") return false;
            segments[^1] = last;
        }

        normalizedPath = string.Join('/', segments);
        var candidate = Path.GetFullPath(Path.Combine(WikiPath,
            Path.Combine(segments) + $".{GlobalConfig.DefaultTaskExtension}"));
        var wikiRoot = Path.GetFullPath(WikiPath);
        var wikiRootWithSeparator = wikiRoot.EndsWith(Path.DirectorySeparatorChar)
            ? wikiRoot
            : wikiRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(wikiRootWithSeparator, StringComparison.Ordinal))
            return false;

        filePath = candidate;
        return true;
    }

    public bool TryReadWikiFile(string pagePath, [MaybeNullWhen(false)] out string normalizedPath,
        [MaybeNullWhen(false)] out string filePath, [MaybeNullWhen(false)] out string content)
    {
        content = null;
        if (!TryResolveWikiPath(pagePath, out normalizedPath, out filePath)) return false;
        if (!FileSystem.FileExists(filePath)) return false;

        content = FileSystem.ReadAllText(filePath);
        return true;
    }

    public void WriteWikiPage(WikiPage page)
    {
        if (!TryResolveWikiPath(page.Path, out _, out var filePath))
            throw new ArgumentException("Invalid wiki path.", nameof(page));

        var directory = Path.GetDirectoryName(filePath);
        if (directory != null)
            FileSystem.CreateDirectory(directory);

        FileSystem.WriteAllText(filePath, page.ToMarkdown());
    }

    public void WriteWikiFile(string pagePath, string content)
    {
        if (!TryResolveWikiPath(pagePath, out _, out var filePath))
            throw new ArgumentException("Invalid wiki path.", nameof(pagePath));

        var directory = Path.GetDirectoryName(filePath);
        if (directory != null)
            FileSystem.CreateDirectory(directory);

        FileSystem.WriteAllText(filePath, content);
    }

    public IReadOnlyList<(string Path, string FilePath, string Content)> GetWikiMarkdownFiles()
    {
        if (!FileSystem.DirectoryExists(WikiPath)) return [];

        return Directory
            .EnumerateFiles(WikiPath, $"*.{GlobalConfig.DefaultTaskExtension}", SearchOption.AllDirectories)
            .Select(file =>
            {
                var relative = Path.GetRelativePath(WikiPath, file);
                var pagePath = Path.ChangeExtension(relative, null)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                return (pagePath, file, FileSystem.ReadAllText(file));
            })
            .OrderBy(page => page.pagePath, StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveRef(FileInfo refFile)
    {
        var refContent = FileSystem.ReadAllText(refFile.FullName);
        return Path.Combine(refFile.Directory!.FullName, refContent);
    }
}
