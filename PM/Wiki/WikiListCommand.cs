using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiListCommand(WikiService wikiService) : Command<WikiListCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = wikiService.ListPages();
        if (!result.Success)
        {
            RenderError(result.Message, "Wiki listing failed.");
            return 1;
        }

        var pages = result.Payload!;
        if (pages.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No wiki pages.[/]");
            return 0;
        }

        var table = new Table()
            .RoundedBorder()
            .AddColumn("Path")
            .AddColumn("Title")
            .AddColumn("Modified");

        foreach (var page in pages)
        {
            table.AddRow(
                page.Path.EscapeMarkup(),
                page.Title.EscapeMarkup(),
                page.ModifiedAt.ToString("u").EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static void RenderError(string? message, string fallback)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? fallback).EscapeMarkup()}[/]");
    }

    public sealed class Settings : CommonSettings;
}
