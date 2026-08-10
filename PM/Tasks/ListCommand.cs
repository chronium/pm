using System.ComponentModel;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class ListCommand(BoardService boardService, LinkedProjectReadService linkedReads) : AsyncCommand<ListCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (settings.Family || !string.IsNullOrWhiteSpace(settings.Project))
            return await ExecuteLinkedAsync(settings, cancellationToken);

        var result = boardService.GetBoard(
            new BoardQuery(settings.Track, settings.Milestone, settings.State, settings.IncludeDelivered),
            BoardService.CliDescriptionPreviewLength);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(result.Message ?? "Unable to list tasks.").EscapeMarkup()}[/]");
            return 1;
        }

        foreach (var milestone in result.Payload!.MilestoneGroups)
        {
            AnsiConsole.Write(new Rule(Markup.Escape(milestone.Name)).RuleStyle("grey"));

            foreach (var state in milestone.States)
                AnsiConsole.Write(BuildStateTable(state));
        }

        return 0;
    }

    private async Task<int> ExecuteLinkedAsync(Settings settings, CancellationToken cancellationToken)
    {
        var request = settings.ToLinkedReadRequest();
        if (!request.Success)
        {
            WriteError(request.Message, "Unable to select linked projects.");
            return 1;
        }

        var result = await linkedReads.ListTasksAsync(
            request.Payload!,
            new BoardQuery(settings.Track, settings.Milestone, settings.State, settings.IncludeDelivered),
            cancellationToken);
        if (!result.Success)
        {
            WriteError(result.Message, "Unable to list tasks.");
            return 1;
        }

        if (result.Payload!.Items.Count == 0)
            AnsiConsole.MarkupLine("[grey]No tasks.[/]");

        foreach (var project in result.Payload.Items.GroupBy(item => item.Owner.ProjectId))
        {
            var owner = project.First().Owner;
            AnsiConsole.Write(new Rule(Markup.Escape(LinkedProjectConsole.ProjectLabel(owner))).RuleStyle("grey"));
            LinkedProjectConsole.WriteSource(owner);
            var table = new Table()
                .SimpleBorder()
                .Collapse()
                .AddColumn("ID")
                .AddColumn("Title")
                .AddColumn("Track")
                .AddColumn("Milestone")
                .AddColumn("State")
                .AddColumn("Modified")
                .AddColumn("Description");
            foreach (var item in project.Select(entry => entry.Resource))
                table.AddRow(
                    item.Task.Id.EscapeMarkup(),
                    item.Task.Title.EscapeMarkup(),
                    item.Track.EscapeMarkup(),
                    (item.Milestone ?? "-").EscapeMarkup(),
                    item.State.EscapeMarkup(),
                    FormatModifiedAt(item.Task.ModifiedAt).EscapeMarkup(),
                    item.DescriptionPreview.EscapeMarkup());
            AnsiConsole.Write(table);
        }

        LinkedProjectConsole.WriteWarnings(result.Payload.Warnings);
        return 0;
    }

    private static void WriteError(string? message, string fallback) =>
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? fallback).EscapeMarkup()}[/]");

    private static Table BuildStateTable(BoardStateGroup state)
    {
        var table = new Table()
            .Title($"{Markup.Escape(state.Name)} ([darkOrange]{state.Tasks.Count}[/])")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("ID")
            .AddColumn("Title")
            .AddColumn("Track")
            .AddColumn("State")
            .AddColumn("Modified")
            .AddColumn("Description");

        foreach (var entry in state.Tasks)
            table.AddRow(
                Markup.Escape(entry.Task.Id),
                Markup.Escape(entry.Task.Title),
                Markup.Escape(entry.Track),
                Markup.Escape(state.Key),
                Markup.Escape(FormatModifiedAt(entry.Task.ModifiedAt)),
                Markup.Escape(entry.DescriptionPreview));

        return table;
    }

    private static string FormatModifiedAt(DateTime modifiedAt)
    {
        return modifiedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public class Settings : LinkedProjectAggregateReadSettings
    {
        [CommandOption("--state <STATE>")]
        [Description("List tasks in one state")]
        public string? State { get; init; }

        [CommandOption("--track <TRACK>")]
        [Description("List tasks in one track")]
        public string? Track { get; init; }

        [CommandOption("--milestone <MILESTONE>")]
        [Description("List tasks in one milestone")]
        public string? Milestone { get; init; }

        [CommandOption("--include-delivered")]
        [Description("Include tasks assigned to delivered milestones")]
        public bool IncludeDelivered { get; init; }
    }
}
