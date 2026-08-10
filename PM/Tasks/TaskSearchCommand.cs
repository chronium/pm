using System.ComponentModel;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public sealed class TaskSearchCommand(TaskService taskService, LinkedProjectReadService linkedReads)
    : AsyncCommand<TaskSearchCommand.Settings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Family || !string.IsNullOrWhiteSpace(settings.Project))
            return await ExecuteLinkedAsync(settings, cancellationToken);

        var result = taskService.SearchTasks(
            settings.Query,
            settings.Limit,
            new TaskSearchContext(IncludeDelivered: settings.IncludeDelivered));
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

    private async Task<int> ExecuteLinkedAsync(Settings settings, CancellationToken cancellationToken)
    {
        var request = settings.ToLinkedReadRequest();
        if (!request.Success)
        {
            WriteError(request.Message);
            return 1;
        }

        var result = await linkedReads.SearchTasksAsync(
            settings.Query,
            settings.Limit,
            request.Payload,
            new TaskSearchContext(IncludeDelivered: settings.IncludeDelivered),
            cancellationToken);
        if (!result.Success)
        {
            WriteError(result.Message);
            return 1;
        }

        if (result.Payload!.Items.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No matching tasks.[/]");
            LinkedProjectConsole.WriteWarnings(result.Payload.Warnings);
            return 0;
        }

        foreach (var owner in result.Payload.Items.Select(item => item.Owner).DistinctBy(owner => owner.ProjectId))
            LinkedProjectConsole.WriteSource(owner);
        var table = BuildTable(includeProject: true);
        foreach (var entry in result.Payload.Items)
        {
            var item = entry.Resource;
            table.AddRow(
                LinkedProjectConsole.ProjectLabel(entry.Owner).EscapeMarkup(),
                $"[cyan]{item.Task.Id.EscapeMarkup()}[/]",
                item.Task.Title.EscapeMarkup(),
                item.State.EscapeMarkup(),
                item.Track.EscapeMarkup(),
                (item.Milestone ?? "-").EscapeMarkup(),
                (string.IsNullOrWhiteSpace(item.Snippet) ? "-" : item.Snippet).EscapeMarkup());
        }
        AnsiConsole.Write(table);
        LinkedProjectConsole.WriteWarnings(result.Payload.Warnings);
        return 0;
    }

    private static Table BuildTable(bool includeProject = false)
    {
        var table = new Table().SimpleBorder().Collapse();
        if (includeProject) table.AddColumn("Project");
        return table
            .AddColumn("ID")
            .AddColumn("Title")
            .AddColumn("State")
            .AddColumn("Track")
            .AddColumn("Milestone")
            .AddColumn("Snippet");
    }

    private static void WriteError(string? message) =>
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? "Task search failed.").EscapeMarkup()}[/]");

    public sealed class Settings : LinkedProjectAggregateReadSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Full-text and structured task query (use in:all for project-wide search)")]
        public string Query { get; init; } = string.Empty;

        [CommandOption("--include-delivered")]
        [Description("Include tasks assigned to delivered milestones")]
        public bool IncludeDelivered { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("Maximum results (1-100)")]
        [DefaultValue(20)]
        public int Limit { get; init; } = 20;
    }
}
