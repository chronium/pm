using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiListCommand(WikiService wikiService, LinkedProjectReadService linkedReads)
    : AsyncCommand<WikiListCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Family || !string.IsNullOrWhiteSpace(settings.Project))
            return await ExecuteLinkedAsync(settings, cancellationToken);

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

    private async Task<int> ExecuteLinkedAsync(Settings settings, CancellationToken cancellationToken)
    {
        var request = settings.ToLinkedReadRequest();
        if (!request.Success)
        {
            RenderError(request.Message, "Unable to select linked projects.");
            return 1;
        }

        var result = await linkedReads.ListWikiPagesAsync(request.Payload!, cancellationToken);
        if (!result.Success)
        {
            RenderError(result.Message, "Wiki listing failed.");
            return 1;
        }

        if (result.Payload!.Items.Count == 0)
            AnsiConsole.MarkupLine("[grey]No wiki pages.[/]");
        foreach (var project in result.Payload.Items.GroupBy(item => item.Owner.ProjectId))
        {
            var owner = project.First().Owner;
            AnsiConsole.Write(new Rule(Markup.Escape(LinkedProjectConsole.ProjectLabel(owner))).RuleStyle("grey"));
            LinkedProjectConsole.WriteSource(owner);
            var table = CreateTable();
            foreach (var entry in project)
                table.AddRow(
                    entry.Resource.Path.EscapeMarkup(),
                    entry.Resource.Title.EscapeMarkup(),
                    entry.Resource.ModifiedAt.ToString("u").EscapeMarkup());
            AnsiConsole.Write(table);
        }
        LinkedProjectConsole.WriteWarnings(result.Payload.Warnings);
        return 0;
    }

    private static Table CreateTable() => new Table()
        .RoundedBorder()
        .AddColumn("Path")
        .AddColumn("Title")
        .AddColumn("Modified");

    private static void RenderError(string? message, string fallback)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? fallback).EscapeMarkup()}[/]");
    }

    public sealed class Settings : LinkedProjectAggregateReadSettings;
}
