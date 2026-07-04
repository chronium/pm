using System.Net;

using PM.Application;

namespace PM.Web;

public static class BoardHtmlRenderer
{
    public static string RenderPage(BoardData board)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{board.ProjectName} Board"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, false), StringComparison.Ordinal)
            .Replace("{{board}}", RenderBoard(board), StringComparison.Ordinal);
    }

    public static string RenderSettingsPage(BoardData board, ProjectSettingsData settings, string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(settings.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{settings.ProjectName} Settings"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, true), StringComparison.Ordinal)
            .Replace("{{board}}", RenderSettings(settings, error), StringComparison.Ordinal);
    }

    public static string RenderBoard(BoardData board)
    {
        var rows = board.States
            .Where(state => string.IsNullOrWhiteSpace(board.Query.State) || state.Key == board.Query.State)
            .Reverse()
            .Select(state => RenderStateRows(board, state))
            .ToList();

        var tasks = board.Tasks.Count == 0
            ? """  <p class="empty">No tasks match the current filters.</p>"""
            : string.Join(Environment.NewLine, rows);

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

    public static string RenderSettings(ProjectSettingsData settings, string? error = null)
    {
        var errorHtml = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : Template("Settings/Error.html")
                .Replace("{{message}}", H(error), StringComparison.Ordinal);

        return Template("Settings/Settings.html")
            .Replace("{{error}}", errorHtml, StringComparison.Ordinal)
            .Replace("{{statusItems}}", RenderSettingsItems("statuses", settings.Statuses, "name"),
                StringComparison.Ordinal)
            .Replace("{{trackItems}}", RenderSettingsItems("tracks", settings.Tracks, "name"),
                StringComparison.Ordinal)
            .Replace("{{milestoneItems}}", RenderSettingsItems("milestones", settings.Milestones, "title"),
                StringComparison.Ordinal);
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

    private static string RenderSidebar(BoardData board, bool settingsActive)
    {
        return Template("Layout/Sidebar.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{filterInputs}}", RenderFilterInputs(board.Query), StringComparison.Ordinal)
            .Replace("{{wholeProjectActive}}", IsWholeProject(board.Query) && !settingsActive ? " active" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{milestoneItems}}", RenderNavItems("milestone", board.Milestones, board.Query.Milestone),
                StringComparison.Ordinal)
            .Replace("{{trackItems}}", RenderNavItems("track", board.Tracks, board.Query.Track),
                StringComparison.Ordinal)
            .Replace("{{settingsActive}}", settingsActive ? " active" : string.Empty, StringComparison.Ordinal)
            .Replace("{{settingsAriaCurrent}}", settingsActive ? " aria-current=\"page\"" : string.Empty,
                StringComparison.Ordinal);
    }

    private static string RenderNavItems(string queryName, IReadOnlyList<BoardOption> options, string? selected)
    {
        return string.Join(Environment.NewLine, options.Select(option =>
        {
            var active = option.Key == selected;
            return Template("Layout/NavItem.html")
                .Replace("{{active}}", active ? " active" : string.Empty, StringComparison.Ordinal)
                .Replace("{{href}}", H($"/?{queryName}={Url(option.Key)}"), StringComparison.Ordinal)
                .Replace("{{ariaCurrent}}", active ? " aria-current=\"page\"" : string.Empty,
                    StringComparison.Ordinal)
                .Replace("{{name}}", H(option.Name), StringComparison.Ordinal);
        }));
    }

    private static string RenderStateRows(BoardData board, BoardOption state)
    {
        var tasks = board.Tasks
            .Where(task => task.State == state.Key)
            .ToList();

        var rows = string.Join(Environment.NewLine, tasks.Select(task => RenderTaskRow(board, task)));
        var stateRow = Template("Board/StateRow.html")
            .Replace("{{stateKey}}", H(state.Key), StringComparison.Ordinal)
            .Replace("{{stateName}}", H(state.Name), StringComparison.Ordinal)
            .Replace("{{count}}", H(tasks.Count.ToString()), StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(rows)
            ? stateRow
            : stateRow + Environment.NewLine + rows;
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

    private static string RenderSettingsItems(
        string collection,
        IReadOnlyList<BoardOption> options,
        string valueName)
    {
        return string.Join(Environment.NewLine, options.Select(option => Template("Settings/Item.html")
            .Replace("{{key}}", H(option.Key), StringComparison.Ordinal)
            .Replace("{{keyUrl}}", Url(option.Key), StringComparison.Ordinal)
            .Replace("{{name}}", H(option.Name), StringComparison.Ordinal)
            .Replace("{{collection}}", H(collection), StringComparison.Ordinal)
            .Replace("{{valueName}}", H(valueName), StringComparison.Ordinal)
            .Replace("{{valueLabel}}", valueName == "title" ? "Title" : "Name", StringComparison.Ordinal)));
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

    private static bool IsWholeProject(BoardQuery query)
    {
        return string.IsNullOrWhiteSpace(query.Track)
               && string.IsNullOrWhiteSpace(query.Milestone)
               && string.IsNullOrWhiteSpace(query.State);
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
