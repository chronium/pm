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

    public static bool Exists(string path)
    {
        return File.Exists(path);
    }

    public static void DeleteFile(string path)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"Deleted [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}[/]");
        if (GlobalConfig.DryRun) return;
        File.Delete(path);
    }
}