using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public sealed class TaskNextCommand(BoardService boardService) : Command<TaskNextCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = boardService.GetNextTask(new NextTaskQuery(
            Normalize(settings.Track),
            Normalize(settings.Milestone),
            ReadyOnly: !settings.IncludeBlocked));
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Task recommendation failed.").EscapeMarkup()}[/]");
            return 1;
        }

        var recommendation = result.Payload!;
        if (!recommendation.Found || recommendation.Task == null)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]{recommendation.Reason.EscapeMarkup()}[/]");
            return 0;
        }

        var task = recommendation.Task;
        var table = new Table()
            .SimpleBorder()
            .Collapse()
            .AddColumn("ID")
            .AddColumn("Title")
            .AddColumn("State")
            .AddColumn("Track")
            .AddColumn("Milestone")
            .AddColumn("Priority")
            .AddColumn("Dependencies");
        table.AddRow(
            $"[cyan]{task.Task.Id.EscapeMarkup()}[/]",
            task.Task.Title.EscapeMarkup(),
            task.State.EscapeMarkup(),
            task.Track.EscapeMarkup(),
            (task.Milestone ?? "-").EscapeMarkup(),
            task.Priority.EscapeMarkup(),
            task.Dependencies.Summary.EscapeMarkup());
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated($"[grey]{recommendation.Reason.EscapeMarkup()}[/]");
        return 0;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--track <TRACK>")]
        [Description("Limit the recommendation to a track")]
        public string? Track { get; init; }

        [CommandOption("--milestone <MILESTONE>")]
        [Description("Limit the recommendation to a milestone")]
        public string? Milestone { get; init; }

        [CommandOption("--include-blocked")]
        [Description("Return the best blocked task when no dependency-ready task exists")]
        public bool IncludeBlocked { get; init; }
    }
}
