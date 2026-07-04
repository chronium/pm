using System.Net;

using PM.Application;

namespace PM.Web;

public static class BoardHtmlRenderer
{
    public static string RenderPage(BoardData board)
    {
        return Template("BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("styles.css"), StringComparison.Ordinal)
            .Replace("{{filters}}", RenderFilters(board), StringComparison.Ordinal)
            .Replace("{{board}}", RenderBoard(board), StringComparison.Ordinal);
    }

    public static string RenderBoard(BoardData board)
    {
        var milestones = string.Join(Environment.NewLine, board.MilestoneGroups.Select(RenderMilestone));

        return Template("Board.html")
            .Replace("{{milestones}}", milestones, StringComparison.Ordinal);
    }

    public static string RenderTaskDetail(BoardTask task, IReadOnlyList<BoardOption> states)
    {
        var description = string.IsNullOrWhiteSpace(task.Task.Description)
            ? "No description."
            : task.Task.Description;
        var stateOptions = states.Select(state => Template("TaskStateOption.html")
            .Replace("{{key}}", H(state.Key), StringComparison.Ordinal)
            .Replace("{{name}}", H(state.Name), StringComparison.Ordinal)
            .Replace("{{selected}}", state.Key == task.State ? " selected" : string.Empty, StringComparison.Ordinal));

        return Template("TaskDetail.html")
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

    public static string RenderDialogError(string message)
    {
        return Template("DialogError.html")
            .Replace("{{title}}", "Unable to update task", StringComparison.Ordinal)
            .Replace("{{message}}", H(message), StringComparison.Ordinal);
    }

    public static string RenderTaskUpdate(BoardData board, BoardTask task)
    {
        return RenderTaskDetail(task, board.States) + Environment.NewLine + RenderBoardOutOfBand(board);
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

    private static string RenderMilestone(BoardMilestoneGroup milestone)
    {
        return Template("MilestoneGroup.html")
            .Replace("{{milestoneName}}", H(milestone.Name), StringComparison.Ordinal)
            .Replace("{{states}}", string.Join(Environment.NewLine, milestone.States.Select(RenderState)),
                StringComparison.Ordinal);
    }

    private static string RenderState(BoardStateGroup state)
    {
        var tasks = state.Tasks.Count == 0
            ? """    <p class="empty">No tasks</p>"""
            : string.Join(Environment.NewLine, state.Tasks.Select(RenderTaskCard));

        return Template("StateGroup.html")
            .Replace("{{stateName}}", H(state.Name), StringComparison.Ordinal)
            .Replace("{{taskCount}}", state.Tasks.Count.ToString(), StringComparison.Ordinal)
            .Replace("{{tasks}}", tasks, StringComparison.Ordinal);
    }

    private static string RenderFilters(BoardData board)
    {
        var selects = string.Join(Environment.NewLine,
        [
            RenderSelect("track", "Track", board.Tracks, board.Query.Track, "All tracks"),
            RenderSelect("milestone", "Milestone", board.Milestones, board.Query.Milestone, "All milestones"),
            RenderSelect("state", "State", board.States, board.Query.State, "All states"),
        ]);

        return Template("Filters.html")
            .Replace("{{selects}}", selects, StringComparison.Ordinal);
    }

    private static string RenderSelect(
        string name,
        string label,
        IReadOnlyList<BoardOption> options,
        string? selected,
        string allLabel)
    {
        var optionHtml = options.Select(option => Template("SelectOption.html")
            .Replace("{{key}}", H(option.Key), StringComparison.Ordinal)
            .Replace("{{name}}", H(option.Name), StringComparison.Ordinal)
            .Replace("{{selected}}", option.Key == selected ? " selected" : string.Empty, StringComparison.Ordinal));

        return Template("Select.html")
            .Replace("{{label}}", H(label), StringComparison.Ordinal)
            .Replace("{{name}}", H(name), StringComparison.Ordinal)
            .Replace("{{allLabel}}", H(allLabel), StringComparison.Ordinal)
            .Replace("{{options}}", string.Join(Environment.NewLine, optionHtml), StringComparison.Ordinal);
    }

    private static string RenderTaskCard(BoardTask task)
    {
        var preview = string.IsNullOrWhiteSpace(task.DescriptionPreview)
            ? string.Empty
            : Template("TaskPreview.html")
                .Replace("{{preview}}", H(task.DescriptionPreview), StringComparison.Ordinal);

        return Template("TaskCard.html")
            .Replace("{{taskId}}", H(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{taskIdUrl}}", Url(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{title}}", H(task.Task.Title), StringComparison.Ordinal)
            .Replace("{{track}}", H(task.Track), StringComparison.Ordinal)
            .Replace("{{modifiedAt}}", H(FormatModifiedAt(task.Task.ModifiedAt)), StringComparison.Ordinal)
            .Replace("{{preview}}", preview, StringComparison.Ordinal)
            .Replace("{{filePath}}", H(task.FilePath), StringComparison.Ordinal);
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
