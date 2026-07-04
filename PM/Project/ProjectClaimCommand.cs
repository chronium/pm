using PM.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public sealed class ProjectClaimCommand(ProjectRoot projectRoot, INextIdService nextIdService) : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CommonSettings settings,
        CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run pm init first.[/]");
            return 1;
        }

        var registration = await nextIdService.RegisterProject(projectRoot, cancellationToken);
        AnsiConsole.MarkupLineInterpolated($"Project ID: [green]{registration.ProjectId}[/]");

        if (!string.IsNullOrWhiteSpace(registration.RecoveryKey))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Save this PM recovery key somewhere safe. It will not be shown again.[/]");
            AnsiConsole.MarkupLineInterpolated($"[green]{registration.RecoveryKey}[/]");
        }

        return 0;
    }
}
