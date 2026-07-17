using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiSearchCommand(WikiService wikiService) : Command<WikiSearchCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = wikiService.SearchPages(settings.Query, settings.Limit);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Wiki search failed.").EscapeMarkup()}[/]");
            return 1;
        }

        if (result.Payload!.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No matching wiki pages.[/]");
            return 0;
        }

        var table = new Table()
            .SimpleBorder()
            .Collapse()
            .AddColumn("Path")
            .AddColumn("Title")
            .AddColumn("Modified")
            .AddColumn("Matches")
            .AddColumn("Snippet");

        foreach (var page in result.Payload)
        {
            table.AddRow(
                page.Path.EscapeMarkup(),
                page.Title.EscapeMarkup(),
                page.ModifiedAt.ToString("u").EscapeMarkup(),
                page.MatchCount.ToString().EscapeMarkup(),
                page.Snippet.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Full-text wiki query")]
        public string Query { get; init; } = string.Empty;

        [CommandOption("--limit <COUNT>")]
        [Description("Maximum results (1-100)")]
        [DefaultValue(20)]
        public int Limit { get; init; } = 20;
    }
}
