using System.Diagnostics.CodeAnalysis;
using PM.Files;
using PM.Tasks;
using PM.Wiki;
using YamlDotNet.Core;

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

public sealed record TaskOrderScope(string Track, string State, string? Milestone);

public sealed record TaskOrderEntry
{
    public string Track { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string? Milestone { get; init; }
    public List<string> TaskIds { get; init; } = [];
}

public sealed record TaskOrderFile
{
    public List<TaskOrderEntry> Orders { get; init; } = [];
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

    private ProjectRoot(string pmRootPath)
    {
        Exists = true;
        RootPath = pmRootPath;
        Config = ProjectConfig.ReadConfig(this);
    }

    public string TasksPath => Path.Combine(RootPath!, GlobalConfig.TasksDirName);
    public string StatesPath => Path.Combine(RootPath!, GlobalConfig.StatesDirName);
    public string WikiPath => Path.Combine(RootPath!, GlobalConfig.WikiDirName);
    public string TaskOrderPath => Path.Combine(RootPath!, GlobalConfig.TaskOrderFile);
    public string ConfigPath => Path.Combine(RootPath!, GlobalConfig.PmConfigFile);
    public string LinkedProjectsPath => Path.Combine(RootPath!, GlobalConfig.LinkedProjectsFile);
    public string RepositoryPath => Directory.GetParent(RootPath!)!.FullName;

    public bool Exists { get; private set; }
    public string RootPath { get; private set; }

    public ProjectConfig? Config { get; private set; }

    public static bool TryOpenExact(
        string repositoryPath,
        [MaybeNullWhen(false)] out ProjectRoot projectRoot)
    {
        projectRoot = null;
        if (string.IsNullOrWhiteSpace(repositoryPath)) return false;

        try
        {
            var canonicalRepositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            var pmRootPath = Path.Combine(canonicalRepositoryPath, GlobalConfig.PmDirName);
            if (!Directory.Exists(pmRootPath)) return false;

            projectRoot = new ProjectRoot(pmRootPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or InvalidDataException or YamlException)
        {
            return false;
        }
    }

    public bool TryReadProjectId([MaybeNullWhen(false)] out string projectId)
    {
        projectId = null;
        if (!Exists) return false;

        try
        {
            var path = Path.Combine(RootPath, GlobalConfig.ProjectIdFile);
            if (!File.Exists(path)) return false;

            var value = File.ReadAllText(path).Trim();
            if (!ProjectIdentifiers.IsValid(value)) return false;

            projectId = value;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryReloadConfig()
    {
        if (!Exists || RootPath == null) return false;

        try
        {
            var config = ProjectConfig.ReadConfig(this);
            if (string.IsNullOrWhiteSpace(config.Name) ||
                string.IsNullOrWhiteSpace(config.IdPrefix) ||
                config.IdWidth <= 0 ||
                config.TaskStates is not { Count: > 0 } ||
                config.Tracks is not { Count: > 0 })
                return false;

            Config = config;
            return true;
        }
        catch
        {
            return false;
        }
    }

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

        CreateTrackedDirectory(TasksPath);
        FileSystem.CreateDirectory(StatesPath);
        FileSystem.CreateDirectory(WikiPath);

        WriteStatesDirectories();
    }

    private void WriteStatesDirectories()
    {
        if (RootPath == null) throw new InvalidOperationException("Project root path is not set.");

        foreach (var key in Config!.TaskStates.Keys)
            CreateTrackedStateDirectory(key);
    }

    private static void CreateTrackedDirectory(string path)
    {
        FileSystem.CreateDirectory(path);
        var placeholderPath = Path.Combine(path, GlobalConfig.DirectoryPlaceholderFile);
        if (!FileSystem.FileExists(placeholderPath))
            FileSystem.WriteAllText(placeholderPath, string.Empty);
    }

    public void CreateTrackedStateDirectory(string state)
    {
        FileSystem.CreateDirectory(StatesPath);
        CreateTrackedDirectory(Path.Combine(StatesPath, state));
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
        FileSystem.CreateDirectory(TasksPath);
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
        RemoveTaskFromOrders(task.Id);
    }

    public void UpdateTaskState(TaskItem task, string state)
    {
        var track = ResolveTaskTrack(task);
        var hasCurrentState = TryGetState(task, out var currentState);
        var stateDir = Path.Combine(StatesPath, state);
        FileSystem.CreateDirectory(stateDir);

        var stateRelativePath = Path.GetRelativePath(stateDir, TasksPath);
        var destinationPath = Path.Combine(stateDir, $"{task.Id}.ref");
        var destinationExisted = FileSystem.FileExists(destinationPath);
        var destinationContent = destinationExisted ? FileSystem.ReadAllText(destinationPath) : null;
        var taskOrderExisted = FileSystem.FileExists(TaskOrderPath);
        var taskOrderContent = taskOrderExisted ? FileSystem.ReadAllText(TaskOrderPath) : null;

        FileSystem.WriteAllText(destinationPath,
            $"{stateRelativePath}/{task.Id}.{GlobalConfig.DefaultTaskExtension}");

        if (!hasCurrentState || string.Equals(currentState, state, StringComparison.Ordinal))
            return;

        try
        {
            MoveTaskOrderScope(task.Id, new TaskOrderScope(track, currentState!, task.Milestone),
                new TaskOrderScope(track, state, task.Milestone));
            FileSystem.DeleteFile(Path.Combine(StatesPath, currentState!, $"{task.Id}.ref"));
        }
        catch
        {
            RestoreFile(destinationPath, destinationExisted, destinationContent);
            RestoreFile(TaskOrderPath, taskOrderExisted, taskOrderContent);
            throw;
        }
    }

    private static void RestoreFile(string path, bool existed, string? content)
    {
        try
        {
            if (existed)
                FileSystem.WriteAllText(path, content ?? string.Empty);
            else if (FileSystem.FileExists(path))
                FileSystem.DeleteFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the original storage failure.
        }
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

    public IReadOnlyList<(string FilePath, string Content)> GetTaskMarkdownFiles()
    {
        if (!FileSystem.DirectoryExists(TasksPath)) return [];

        return Directory
            .EnumerateFiles(TasksPath, $"*.{GlobalConfig.DefaultTaskExtension}", SearchOption.TopDirectoryOnly)
            .Select(file => (file, FileSystem.ReadAllText(file)))
            .OrderBy(task => task.file, StringComparer.Ordinal)
            .ToList();
    }

    public TaskOrderFile ReadTaskOrder()
    {
        if (!FileSystem.FileExists(TaskOrderPath))
            return new TaskOrderFile();

        try
        {
            return YamlSerde.Deserialize<TaskOrderFile>(FileSystem.ReadAllText(TaskOrderPath)) ?? new TaskOrderFile();
        }
        catch
        {
            return new TaskOrderFile();
        }
    }

    public void WriteTaskOrder(TaskOrderFile order)
    {
        FileSystem.WriteAllText(TaskOrderPath, YamlSerde.Serialize(order));
    }

    public LinkedProjectManifest? ReadLinkedProjectsManifest()
    {
        if (!FileSystem.FileExists(LinkedProjectsPath))
            return null;

        return YamlSerde.Deserialize<LinkedProjectManifest>(FileSystem.ReadAllText(LinkedProjectsPath));
    }

    public void WriteLinkedProjectsManifest(LinkedProjectManifest manifest)
    {
        FileSystem.WriteAllText(LinkedProjectsPath, YamlSerde.Serialize(manifest));
    }

    public void DeleteLinkedProjectsManifest()
    {
        if (FileSystem.FileExists(LinkedProjectsPath))
            FileSystem.DeleteFile(LinkedProjectsPath);
    }

    public IReadOnlyList<string> GetTaskOrder(TaskOrderScope scope)
    {
        return ReadTaskOrder().Orders
            .FirstOrDefault(entry => MatchesScope(entry, scope))
            ?.TaskIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList() ?? [];
    }

    public bool SetTaskOrder(TaskOrderScope scope, IReadOnlyList<string> taskIds)
    {
        var order = ReadTaskOrder();
        var normalized = taskIds.Select(id => id.Trim()).ToList();
        var existing = order.Orders.FirstOrDefault(entry => MatchesScope(entry, scope));
        if (existing != null && existing.TaskIds.SequenceEqual(normalized, StringComparer.Ordinal))
            return false;

        order.Orders.RemoveAll(entry => MatchesScope(entry, scope));
        order.Orders.Add(new TaskOrderEntry
        {
            Track = scope.Track,
            State = scope.State,
            Milestone = NormalizeMilestone(scope.Milestone),
            TaskIds = normalized,
        });
        WriteTaskOrder(order);
        return true;
    }

    public void RemoveTaskFromOrders(string taskId)
    {
        var order = ReadTaskOrder();
        var changed = false;
        foreach (var entry in order.Orders)
            changed |= entry.TaskIds.RemoveAll(id => string.Equals(id, taskId, StringComparison.Ordinal)) > 0;

        if (changed) WriteTaskOrder(order);
    }

    public void MoveTaskOrderScope(string taskId, TaskOrderScope oldScope, TaskOrderScope newScope)
    {
        if (oldScope == newScope) return;

        var order = ReadTaskOrder();
        var changed = false;
        foreach (var entry in order.Orders.Where(entry => MatchesScope(entry, oldScope)))
            changed |= entry.TaskIds.RemoveAll(id => string.Equals(id, taskId, StringComparison.Ordinal)) > 0;

        var target = order.Orders.FirstOrDefault(entry => MatchesScope(entry, newScope));
        if (target != null && !target.TaskIds.Contains(taskId, StringComparer.Ordinal))
        {
            target.TaskIds.Add(taskId);
            changed = true;
        }

        if (changed) WriteTaskOrder(order);
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

    private static bool MatchesScope(TaskOrderEntry entry, TaskOrderScope scope)
    {
        return string.Equals(entry.Track, scope.Track, StringComparison.Ordinal) &&
               string.Equals(entry.State, scope.State, StringComparison.Ordinal) &&
               string.Equals(NormalizeMilestone(entry.Milestone), NormalizeMilestone(scope.Milestone),
                   StringComparison.Ordinal);
    }

    private static string? NormalizeMilestone(string? milestone)
    {
        return string.IsNullOrWhiteSpace(milestone) ? null : milestone.Trim();
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
