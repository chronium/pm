using System.ComponentModel;
using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Wiki;

public sealed class WikiShowCommand(WikiService wikiService, ISyntaxHighlighter highlighter)
    : Command<WikiShowCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = wikiService.ReadPage(settings.Path);
        if (!result.Success)
        {
            RenderError(result.Message, "Wiki page not found.");
            return 1;
        }

        RenderPage(result.Payload!);
        return 0;
    }

    private void RenderPage(WikiPageData page)
    {
        AnsiConsole.MarkupLineInterpolated($"[bold]{page.Title.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLineInterpolated($"Path: [green]{page.Path.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLineInterpolated($"File: {page.FilePath.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"Modified: {page.ModifiedAt:u}");
        AnsiConsole.WriteLine();

        var sb = new StringBuilder();
        highlighter.Highlight(page.Body, "md", new MarkupTokenRenderer(sb));

        var panel = new Panel(string.IsNullOrWhiteSpace(sb.ToString()) ? "[grey]Empty page.[/]" : sb.ToString())
        {
            Header = new($"{page.Path}.{GlobalConfig.DefaultTaskExtension}"),
            Border = new RoundedBoxBorder(),
        };

        AnsiConsole.Write(panel);
    }

    private static void RenderError(string? message, string fallback)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? fallback).EscapeMarkup()}[/]");
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Wiki page path")]
        public string Path { get; init; } = string.Empty;
    }
}
