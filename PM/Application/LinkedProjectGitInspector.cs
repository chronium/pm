using System.Diagnostics;

namespace PM.Application;

public sealed class LinkedProjectGitInspector : ILinkedProjectGitInspector
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

    public async Task<LinkedProjectGitMetadata> InspectAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
            return new LinkedProjectGitMetadata(null, null);

        var revision = await GitAsync(repositoryPath, cancellationToken, "rev-parse", "HEAD");
        if (!revision.Success)
            return new LinkedProjectGitMetadata(null, null);

        var status = await GitAsync(
            repositoryPath,
            cancellationToken,
            "status", "--porcelain=v1", "--untracked-files=normal");
        return new LinkedProjectGitMetadata(
            revision.Output.Trim(),
            status.Success ? status.Output.Length != 0 : null);
    }

    private static async Task<(bool Success, string Output)> GitAsync(
        string repositoryPath,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = repositoryPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            if (!process.Start()) return (false, string.Empty);

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await Task.WhenAll(output, error);
                if (cancellationToken.IsCancellationRequested) throw;
                return (false, string.Empty);
            }
            await Task.WhenAll(output, error);
            return process.ExitCode == 0
                ? (true, await output)
                : (false, string.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                           UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return (false, string.Empty);
        }
    }
}
