using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiRemoveCommand(WikiService wikiService) : Command<WikiRemoveCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes)
        {
            AnsiConsole.MarkupLine("[red]Pass --yes to permanently remove the wiki page.[/]");
            return 1;
        }

        var result = wikiService.RemovePage(settings.Path);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Wiki page removal failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Removed wiki page [green]{settings.Path.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Wiki page path")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--yes")]
        [Description("Confirm permanent wiki page removal")]
        public bool Yes { get; init; }
    }
}
