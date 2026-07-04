using System.Net;

using PM.Application;

namespace PM.Web;

public static class BoardHtmlRenderer
{
    public static string RenderPage(BoardData board)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{filters}}", RenderFilters(board), StringComparison.Ordinal)
            .Replace("{{board}}", RenderBoard(board), StringComparison.Ordinal);
    }

    public static string RenderBoard(BoardData board)
    {
        var tasks = board.Tasks.Count == 0
            ? """  <p class="empty">No tasks match the current filters.</p>"""
            : string.Join(Environment.NewLine, board.Tasks.Select(task => RenderTaskRow(board, task)));

        return Template("Board/Board.html")
            .Replace("{{tasks}}", tasks, StringComparison.Ordinal);
    }

    public static string RenderTaskDetail(BoardTask task, IReadOnlyList<BoardOption> states)
    {
        var description = string.IsNullOrWhiteSpace(task.Task.Description)
            ? "No description."
            : task.Task.Description;
        var stateOptions = states.Select(state => Template("Controls/TaskStateOption.html")
            .Replace("{{key}}", H(state.Key), StringComparison.Ordinal)
            .Replace("{{name}}", H(state.Name), StringComparison.Ordinal)
            .Replace("{{selected}}", state.Key == task.State ? " selected" : string.Empty, StringComparison.Ordinal));

        return Template("Dialog/TaskDetail.html")
            .Replace("{{title}}", H(task.Task.Title), StringComparison.Ordinal)
            .Replace("{{taskId}}", H(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{taskIdUrl}}", Url(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{track}}", H(task.Track), StringComparison.Ordinal)
            .Replace("{{state}}", H(task.State), StringComparison.Ordinal)
            .Replace("{{modifiedAt}}", H(FormatModifiedAt(task.Task.ModifiedAt)), StringComparison.Ordinal)
            .Replace("{{filePath}}", H(task.FilePath), StringComparison.Ordinal)
            .Replace("{{description}}", H(description), StringComparison.Ordinal)
            .Replace("{{stateOptions}}", string.Join(Environment.NewLine, stateOptions), StringComparison.Ordinal);
    }

    public static string RenderTaskCreateForm(BoardData board)
    {
        var selectedTrack = string.IsNullOrWhiteSpace(board.Query.Track) ? board.Tracks.FirstOrDefault()?.Key : board.Query.Track;

        return Template("Dialog/TaskCreateForm.html")
            .Replace("{{trackOptions}}", RenderOptions(board.Tracks, selectedTrack), StringComparison.Ordinal)
            .Replace("{{milestoneOptions}}", RenderOptions(board.Milestones, board.Query.Milestone), StringComparison.Ordinal)
            .Replace("{{filterInputs}}", RenderFilterInputs(board.Query), StringComparison.Ordinal);
    }

    public static string RenderTaskEditForm(string taskId, string markdown, BoardQuery query)
    {
        return Template("Dialog/TaskEditForm.html")
            .Replace("{{taskId}}", H(taskId), StringComparison.Ordinal)
            .Replace("{{taskIdUrl}}", Url(taskId), StringComparison.Ordinal)
            .Replace("{{markdown}}", H(markdown), StringComparison.Ordinal)
            .Replace("{{filterInputs}}", RenderFilterInputs(query), StringComparison.Ordinal);
    }

    public static string RenderDialogError(string message, string title = "Unable to update task")
    {
        return Template("Dialog/DialogError.html")
            .Replace("{{title}}", H(title), StringComparison.Ordinal)
            .Replace("{{message}}", H(message), StringComparison.Ordinal);
    }

    public static string RenderTaskUpdate(BoardData board, BoardTask task)
    {
        return RenderTaskDetail(task, board.States) + Environment.NewLine + RenderBoardOutOfBand(board);
    }

    public static string RenderTaskCreated(BoardData board, BoardTask task)
    {
        return RenderTaskUpdate(board, task);
    }

    public static string RenderTaskRemoval(BoardData board)
    {
        return RenderBoardOutOfBand(board) + Environment.NewLine +
               "<div data-close-dialog></div><script>document.getElementById('task-dialog')?.close();</script>";
    }

    public static string RenderBoardOutOfBand(BoardData board)
    {
        return $"""<section id="board" hx-swap-oob="innerHTML">{RenderBoard(board)}</section>""";
    }

    private static string RenderFilters(BoardData board)
    {
        var selects = string.Join(Environment.NewLine,
        [
            RenderSelect("track", "Track", board.Tracks, board.Query.Track, "All tracks"),
            RenderSelect("milestone", "Milestone", board.Milestones, board.Query.Milestone, "All milestones"),
            RenderSelect("state", "State", board.States, board.Query.State, "All states"),
        ]);

        return Template("Controls/Filters.html")
            .Replace("{{selects}}", selects, StringComparison.Ordinal);
    }

    private static string RenderFilterInputs(BoardQuery query)
    {
        return string.Join(Environment.NewLine,
        [
            $"""<input type="hidden" name="filterTrack" value="{H(query.Track)}">""",
            $"""<input type="hidden" name="filterMilestone" value="{H(query.Milestone)}">""",
            $"""<input type="hidden" name="filterState" value="{H(query.State)}">""",
        ]);
    }

    private static string RenderSelect(
        string name,
        string label,
        IReadOnlyList<BoardOption> options,
        string? selected,
        string allLabel)
    {
        return Template("Controls/Select.html")
            .Replace("{{label}}", H(label), StringComparison.Ordinal)
            .Replace("{{name}}", H(name), StringComparison.Ordinal)
            .Replace("{{allLabel}}", H(allLabel), StringComparison.Ordinal)
            .Replace("{{options}}", RenderOptions(options, selected), StringComparison.Ordinal);
    }

    private static string RenderOptions(IReadOnlyList<BoardOption> options, string? selected)
    {
        var optionHtml = options.Select(option => Template("Controls/SelectOption.html")
            .Replace("{{key}}", H(option.Key), StringComparison.Ordinal)
            .Replace("{{name}}", H(option.Name), StringComparison.Ordinal)
            .Replace("{{selected}}", option.Key == selected ? " selected" : string.Empty, StringComparison.Ordinal));

        return string.Join(Environment.NewLine, optionHtml);
    }

    private static string RenderTaskRow(BoardData board, BoardTask task)
    {
        var preview = string.IsNullOrWhiteSpace(task.DescriptionPreview)
            ? string.Empty
            : Template("Board/TaskPreview.html")
                .Replace("{{preview}}", H(task.DescriptionPreview), StringComparison.Ordinal);

        return Template("Board/TaskRow.html")
            .Replace("{{taskId}}", H(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{taskIdUrl}}", Url(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{title}}", H(task.Task.Title), StringComparison.Ordinal)
            .Replace("{{track}}", H(task.Track), StringComparison.Ordinal)
            .Replace("{{state}}", H(OptionName(board.States, task.State, task.State)), StringComparison.Ordinal)
            .Replace("{{milestone}}", H(MilestoneName(board, task.Milestone)), StringComparison.Ordinal)
            .Replace("{{modifiedAt}}", H(FormatModifiedAt(task.Task.ModifiedAt)), StringComparison.Ordinal)
            .Replace("{{preview}}", preview, StringComparison.Ordinal);
    }

    private static string MilestoneName(BoardData board, string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return "Unassigned";
        return OptionName(board.Milestones, milestone, milestone);
    }

    private static string OptionName(IReadOnlyList<BoardOption> options, string key, string fallback)
    {
        return options.FirstOrDefault(option => option.Key == key)?.Name ?? fallback;
    }

    private static string FormatModifiedAt(DateTime modifiedAt)
    {
        return modifiedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string Template(string fileName)
    {
        return TemplateStore.Read(fileName);
    }

    private static string H(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string Url(string value)
    {
        return Uri.EscapeDataString(value);
    }
}
