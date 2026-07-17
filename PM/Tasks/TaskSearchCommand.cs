using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public sealed class TaskSearchCommand(TaskService taskService) : Command<TaskSearchCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = taskService.SearchTasks(settings.Query, settings.Limit);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{(result.Message ?? "Task search failed.").EscapeMarkup()}[/]");
            return 1;
        }

        if (result.Payload!.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No matching tasks.[/]");
            return 0;
        }

        var table = new Table()
            .SimpleBorder()
            .Collapse()
            .AddColumn("ID")
            .AddColumn("Title")
            .AddColumn("State")
            .AddColumn("Track")
            .AddColumn("Milestone")
            .AddColumn("Snippet");

        foreach (var item in result.Payload)
        {
            table.AddRow(
                $"[cyan]{item.Task.Id.EscapeMarkup()}[/]",
                item.Task.Title.EscapeMarkup(),
                item.State.EscapeMarkup(),
                item.Track.EscapeMarkup(),
                (item.Milestone ?? "-").EscapeMarkup(),
                (string.IsNullOrWhiteSpace(item.Snippet) ? "-" : item.Snippet).EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Full-text and structured task query (use in:all for project-wide search)")]
        public string Query { get; init; } = string.Empty;

        [CommandOption("--limit <COUNT>")]
        [Description("Maximum results (1-100)")]
        [DefaultValue(20)]
        public int Limit { get; init; } = 20;
    }
}
