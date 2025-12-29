using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM;

public class TimingInterceptor : ICommandInterceptor
{
    private Stopwatch? _stopwatch;

    public void Intercept(CommandContext context, CommandSettings settings)
    {
        _stopwatch = Stopwatch.StartNew();
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result)
    {
        _stopwatch?.Stop();
        AnsiConsole.MarkupLineInterpolated(
            $"Command completed in {_stopwatch?.ElapsedMilliseconds}ms [dim](exit code {result})[/]");
    }
}