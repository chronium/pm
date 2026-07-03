using System.ComponentModel;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public class ListCommand(BoardService boardService) : AsyncCommand<ListCommand.Settings>
{
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var result = boardService.GetBoard(
            new BoardQuery(settings.Track, settings.Milestone, settings.State),
            BoardService.CliDescriptionPreviewLength);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(result.Message ?? "Unable to list tasks.").EscapeMarkup()}[/]");
            return Task.FromResult(1);
        }

        foreach (var milestone in result.Payload!.MilestoneGroups)
        {
            AnsiConsole.Write(new Rule(Markup.Escape(milestone.Name)).RuleStyle("grey"));

            foreach (var state in milestone.States)
                AnsiConsole.Write(BuildStateTable(state));
        }

        return Task.FromResult(0);
    }

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

    public class Settings : CommonSettings
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
    }
}
