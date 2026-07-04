using Spectre.Console;

namespace PM.Files;

public static class FileSystem
{
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
