using System.ComponentModel;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiSearchCommand(WikiService wikiService, LinkedProjectReadService linkedReads)
    : AsyncCommand<WikiSearchCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Family || !string.IsNullOrWhiteSpace(settings.Project))
            return await ExecuteLinkedAsync(settings, cancellationToken);

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

    private async Task<int> ExecuteLinkedAsync(Settings settings, CancellationToken cancellationToken)
    {
        var request = settings.ToLinkedReadRequest();
        if (!request.Success)
        {
            RenderError(request.Message);
            return 1;
        }

        var result = await linkedReads.SearchWikiPagesAsync(
            settings.Query, settings.Limit, request.Payload, cancellationToken);
        if (!result.Success)
        {
            RenderError(result.Message);
            return 1;
        }

        if (result.Payload!.Items.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No matching wiki pages.[/]");
            LinkedProjectConsole.WriteWarnings(result.Payload.Warnings);
            return 0;
        }

        foreach (var owner in result.Payload.Items.Select(item => item.Owner).DistinctBy(owner => owner.ProjectId))
            LinkedProjectConsole.WriteSource(owner);
        var table = CreateTable(includeProject: true);
        foreach (var entry in result.Payload.Items)
            table.AddRow(
                LinkedProjectConsole.ProjectLabel(entry.Owner).EscapeMarkup(),
                entry.Resource.Path.EscapeMarkup(),
                entry.Resource.Title.EscapeMarkup(),
                entry.Resource.ModifiedAt.ToString("u").EscapeMarkup(),
                entry.Resource.MatchCount.ToString().EscapeMarkup(),
                entry.Resource.Snippet.EscapeMarkup());
        AnsiConsole.Write(table);
        LinkedProjectConsole.WriteWarnings(result.Payload.Warnings);
        return 0;
    }

    private static Table CreateTable(bool includeProject = false)
    {
        var table = new Table().SimpleBorder().Collapse();
        if (includeProject) table.AddColumn("Project");
        return table
            .AddColumn("Path")
            .AddColumn("Title")
            .AddColumn("Modified")
            .AddColumn("Matches")
            .AddColumn("Snippet");
    }

    private static void RenderError(string? message) =>
        AnsiConsole.MarkupLineInterpolated(
            $"[red]{(message ?? "Wiki search failed.").EscapeMarkup()}[/]");

    public sealed class Settings : LinkedProjectAggregateReadSettings
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
