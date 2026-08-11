using Spectre.Console;

namespace PM.Files;

public static class FileSystem
{
    private static readonly AsyncLocal<FileMutationScope?> ActiveMutation = new();

    public static FileMutationScope TrackMutations(string repositoryPath)
    {
        var scope = new FileMutationScope(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath)),
            ActiveMutation.Value,
            previous => ActiveMutation.Value = previous);
        ActiveMutation.Value = scope;
        return scope;
    }

    public static string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }

    public static void WriteAllText(string path, string content)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Written [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}[/]");
        if (GlobalConfig.DryRun) return;
        var mutation = ActiveMutation.Value;
        var trackedPath = mutation?.Prepare(path);
        File.WriteAllText(path, content);
        if (trackedPath != null) mutation!.Record(trackedPath);
    }

    public static void WriteAllTextAtomic(string path, string content)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Written [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}[/]");
        if (GlobalConfig.DryRun) return;
        var mutation = ActiveMutation.Value;
        var trackedPath = mutation?.Prepare(path);

        var directory = Path.GetDirectoryName(path) ??
                        throw new InvalidOperationException("File path does not have a parent directory.");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, true);
            if (trackedPath != null) mutation!.Record(trackedPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static void WriteAllTextNew(string path, string content)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Written [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}[/]");
        if (GlobalConfig.DryRun) return;
        var mutation = ActiveMutation.Value;
        var trackedPath = mutation?.Prepare(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        if (trackedPath != null) mutation!.Record(trackedPath);
    }

    public static void CreateDirectory(string path)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Created [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}/[/]");
        if (GlobalConfig.DryRun) return;
        Directory.CreateDirectory(path);
    }

    public static bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public static bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public static void DeleteFile(string path)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Deleted [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}[/]");
        if (GlobalConfig.DryRun) return;
        var mutation = ActiveMutation.Value;
        var trackedPath = mutation?.Prepare(path);
        File.Delete(path);
        if (trackedPath != null) mutation!.Record(trackedPath);
    }

    public static void DeleteDirectory(string path)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Deleted [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}/[/]");
        if (GlobalConfig.DryRun) return;
        Directory.Delete(path);
    }

    public static List<FileInfo> ReadFiles(string path)
    {
        return ReadFiles(path, "*");
    }

    public static List<FileInfo> ReadFiles(string path, string searchPattern = "*")
    {
        var files = Directory.EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .ToList();
        return files;
    }
}

public sealed class FileMutationScope : IDisposable
{
    private readonly string repositoryPath;
    private readonly FileMutationScope? previous;
    private readonly Action<FileMutationScope?> restore;
    private readonly HashSet<string> changedPaths = new(StringComparer.Ordinal);
    private bool disposed;

    internal FileMutationScope(
        string repositoryPath,
        FileMutationScope? previous,
        Action<FileMutationScope?> restore)
    {
        this.repositoryPath = repositoryPath;
        this.previous = previous;
        this.restore = restore;
    }

    public IReadOnlyList<string> ChangedPaths => changedPaths.Order(StringComparer.Ordinal).ToList();

    internal string Prepare(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(repositoryPath, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("A mutation attempted to report a path outside its target repository.");

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    internal void Record(string relativePath) => changedPaths.Add(relativePath);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        restore(previous);
    }
}
