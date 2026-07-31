using System.ComponentModel;
using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Wiki;

public sealed class WikiShowCommand(
    WikiService wikiService,
    LinkedProjectReadService linkedReads,
    ISyntaxHighlighter highlighter)
    : AsyncCommand<WikiShowCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.Project))
            return await ExecuteLinkedAsync(settings, cancellationToken);

        var result = wikiService.ReadPage(settings.Path);
        if (!result.Success)
        {
            RenderError(result.Message, "Wiki page not found.");
            return 1;
        }

        RenderPage(result.Payload!);
        return 0;
    }

    private async Task<int> ExecuteLinkedAsync(Settings settings, CancellationToken cancellationToken)
    {
        var request = settings.ToProjectReadRequest();
        if (!request.Success)
        {
            RenderError(request.Message, "Unable to select linked project.");
            return 1;
        }

        var result = await linkedReads.GetWikiPageAsync(settings.Path, settings.Project, cancellationToken);
        if (!result.Success)
        {
            RenderError(result.Message, "Wiki page not found.");
            return 1;
        }

        var page = AssertSingle(result.Payload!);
        LinkedProjectConsole.WriteSource(page.Owner);
        RenderPage(page.Resource);
        LinkedProjectConsole.WriteWarnings(result.Payload!.Warnings);
        return 0;
    }

    private static LinkedProjectResource<WikiPageData> AssertSingle(LinkedProjectReadResult<WikiPageData> result) =>
        result.Items.Single();

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

    public sealed class Settings : LinkedProjectSelectorSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Wiki page path")]
        public string Path { get; init; } = string.Empty;
    }
}
