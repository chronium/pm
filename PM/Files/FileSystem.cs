using Spectre.Console;

namespace PM.Files;

public static class FileSystem
{
    public static string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }

    public static void WriteFileWithText(string path, string content)
    {
        AnsiConsole.MarkupLineInterpolated($"Written [green]{path}[/]");
        if (GlobalConfig.DryRun) return;
        File.WriteAllText(path, content);
    }

    public static void WriteAllText(string path, string content)
    {
        if (GlobalConfig.DryRun) return;
        File.WriteAllText(path, content);
    }

    public static void CreateDirectory(string path)
    {
        AnsiConsole.MarkupLineInterpolated($"Created [green]{path}/[/]");
        if (GlobalConfig.DryRun) return;
        Directory.CreateDirectory(path);
    }
}