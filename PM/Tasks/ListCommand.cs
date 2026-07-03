using System.ComponentModel;
using System.Text.RegularExpressions;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tasks;

public partial class ListCommand(ProjectRoot projectRoot) : AsyncCommand<ListCommand.Settings>
{
    private const int DescriptionPreviewLength = 48;

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (ValidateProjectAndServiceHealth() != 0) return Task.FromResult(1);

        if (!string.IsNullOrWhiteSpace(settings.State) &&
            !projectRoot.Config!.TaskStates.ContainsKey(settings.State))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]State {settings.State.EscapeMarkup()} not found.[/]");
            return Task.FromResult(1);
        }

        var states = projectRoot.Config!.TaskStates
            .Where(state => string.IsNullOrWhiteSpace(settings.State) || state.Key == settings.State);

        foreach (var (state, name) in states)
        {
            var items = projectRoot.GetTasksInState(state)
                .OrderByDescending(item => item.ModifiedAt)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();

            AnsiConsole.Write(BuildStateTable(state, name, items));
        }

        return Task.FromResult(0);
    }

    private static Table BuildStateTable(string state, string name, List<TaskItem> items)
    {
        var table = new Table()
            .Title($"{Markup.Escape(name)} ([darkOrange]{items.Count}[/])")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("ID")
            .AddColumn("Title")
            .AddColumn("State")
            .AddColumn("Modified")
            .AddColumn("Description");

        foreach (var item in items)
            table.AddRow(
                Markup.Escape(item.Id),
                Markup.Escape(item.Title),
                Markup.Escape(state),
                Markup.Escape(FormatModifiedAt(item.ModifiedAt)),
                Markup.Escape(GetDescriptionPreview(item.Description)));

        return table;
    }

    private static string FormatModifiedAt(DateTime modifiedAt)
    {
        return modifiedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string GetDescriptionPreview(string description)
    {
        var firstLine = description
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => StripMarkdownPrefix(line.Trim()))
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        if (string.IsNullOrWhiteSpace(firstLine)) return string.Empty;

        return firstLine.Length <= DescriptionPreviewLength
            ? firstLine
            : $"{firstLine[..(DescriptionPreviewLength - 3)]}...";
    }

    private static string StripMarkdownPrefix(string line)
    {
        return MarkdownPrefixRegex().Replace(line, string.Empty).Trim();
    }

    private int ValidateProjectAndServiceHealth()
    {
        if (projectRoot.Exists) return 0;

        AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
        return 1;
    }

    public class Settings : CommonSettings
    {
        [CommandOption("--state <STATE>")]
        [Description("List tasks in one state")]
        public string? State { get; init; }
    }

    [GeneratedRegex(@"^(#{1,6}\s+|(?:[-*+]\s+)?\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+|>\s+)")]
    private static partial Regex MarkdownPrefixRegex();
}
