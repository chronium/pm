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
        File.WriteAllText(path, content);
        ActiveMutation.Value?.Record(path);
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
        File.Delete(path);
        ActiveMutation.Value?.Record(path);
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

    internal void Record(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(repositoryPath, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("A mutation attempted to report a path outside its target repository.");

        changedPaths.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        restore(previous);
    }
}
