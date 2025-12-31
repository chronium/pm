using PM;
using Spectre.Console;
using Spectre.Console.Cli;

public class DryRunInterceptor : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (settings is CommonSettings { DryRun: true }) AnsiConsole.MarkupLine("[yellow]Dry run enabled.[/]");
        GlobalConfig.DryRun = settings is CommonSettings { DryRun: true };
    }
}