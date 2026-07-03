using System.Diagnostics;

namespace PM.Tasks;

public interface IEditorService
{
    Task<int> EditFile(string filePath, CancellationToken cancellationToken);
}

public class EditorService : IEditorService
{
    public async Task<int> EditFile(string filePath, CancellationToken cancellationToken)
    {
        var editor = Environment.GetEnvironmentVariable("VISUAL");
        if (string.IsNullOrWhiteSpace(editor)) editor = Environment.GetEnvironmentVariable("EDITOR");
        if (string.IsNullOrWhiteSpace(editor)) editor = "vim";

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                ArgumentList = { "/c", $"{editor} \"%PM_TASK_EDITOR_FILE%\"" },
            }
            : new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh",
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                ArgumentList = { "-c", $"{editor} \"$1\"", "pm-editor", filePath },
            };
        startInfo.Environment["PM_TASK_EDITOR_FILE"] = filePath;

        using var process = Process.Start(startInfo);

        if (process == null) throw new InvalidOperationException("Editor process did not start.");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
