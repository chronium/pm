using System.Net;

using PM.Application;

namespace PM.Web;

public static class BoardHtmlRenderer
{
    private enum SidebarPage
    {
        Board,
        Wiki,
        Settings,
    }

    public static string RenderPage(BoardData board)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{board.ProjectName} Board"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, SidebarPage.Board), StringComparison.Ordinal)
            .Replace("{{board}}", RenderBoard(board), StringComparison.Ordinal);
    }

    public static string RenderSettingsPage(BoardData board, ProjectSettingsData settings, string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(settings.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{settings.ProjectName} Settings"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, SidebarPage.Settings), StringComparison.Ordinal)
            .Replace("{{board}}", RenderSettings(settings, error), StringComparison.Ordinal);
    }

    public static string RenderWikiIndexPage(BoardData board, IReadOnlyList<WikiPageSummary> pages)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, SidebarPage.Wiki), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiIndex(pages), StringComparison.Ordinal);
    }

    public static string RenderWikiPage(BoardData board, WikiPageData page)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{page.Title} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, SidebarPage.Wiki), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiDetail(page), StringComparison.Ordinal);
    }

    public static string RenderWikiFolderPage(BoardData board, string path, IReadOnlyList<WikiPageSummary> pages)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{path} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, SidebarPage.Wiki), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiFolder(path, pages), StringComparison.Ordinal);
    }

    public static string RenderWikiCreatePage(
        BoardData board,
        string path = "",
        string title = "",
        string markdown = "",
        string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"New Wiki Page - {board.ProjectName}"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, SidebarPage.Wiki), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiCreateForm(path, title, markdown, error), StringComparison.Ordinal);
    }

    public static string RenderWikiEditPage(BoardData board, WikiPageData page, string? error = null)
    {
        return RenderWikiEditPage(board, page.Path, page.Title, page.Markdown, error);
    }

    public static string RenderWikiEditPage(
        BoardData board,
        string path,
        string title,
        string markdown,
        string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"Edit {title} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderSidebar(board, SidebarPage.Wiki), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiEditForm(path, title, markdown, error), StringComparison.Ordinal);
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

    public static string RenderWikiIndex(IReadOnlyList<WikiPageSummary> pages)
    {
        var rows = pages.Count == 0
            ? Template("Wiki/Empty.html")
            : string.Join(Environment.NewLine, pages.Select(page => Template("Wiki/IndexRow.html")
                .Replace("{{path}}", H(page.Path), StringComparison.Ordinal)
                .Replace("{{pathUrl}}", WikiPathUrl(page.Path), StringComparison.Ordinal)
                .Replace("{{title}}", H(page.Title), StringComparison.Ordinal)
                .Replace("{{modifiedAt}}", H(FormatModifiedAt(page.ModifiedAt)), StringComparison.Ordinal)));

        return Template("Wiki/Index.html")
            .Replace("{{rows}}", rows, StringComparison.Ordinal);
    }

    public static string RenderWikiFolder(string path, IReadOnlyList<WikiPageSummary> pages)
    {
        var rows = pages.Count == 0
            ? Template("Wiki/Empty.html")
            : string.Join(Environment.NewLine, pages.Select(page => Template("Wiki/IndexRow.html")
                .Replace("{{path}}", H(page.Path), StringComparison.Ordinal)
                .Replace("{{pathUrl}}", WikiPathUrl(page.Path), StringComparison.Ordinal)
                .Replace("{{title}}", H(page.Title), StringComparison.Ordinal)
                .Replace("{{modifiedAt}}", H(FormatModifiedAt(page.ModifiedAt)), StringComparison.Ordinal)));

        return Template("Wiki/Folder.html")
            .Replace("{{breadcrumbs}}", RenderWikiBreadcrumbs(path), StringComparison.Ordinal)
            .Replace("{{folder}}", H(path), StringComparison.Ordinal)
            .Replace("{{rows}}", rows, StringComparison.Ordinal);
    }

    public static string RenderWikiDetail(WikiPageData page)
    {
        return Template("Wiki/Detail.html")
            .Replace("{{title}}", H(page.Title), StringComparison.Ordinal)
            .Replace("{{path}}", H(page.Path), StringComparison.Ordinal)
            .Replace("{{pathUrl}}", WikiPathUrl(page.Path), StringComparison.Ordinal)
            .Replace("{{breadcrumbs}}", RenderWikiBreadcrumbs(page.Path), StringComparison.Ordinal)
            .Replace("{{filePath}}", H(page.FilePath), StringComparison.Ordinal)
            .Replace("{{modifiedAt}}", H(FormatModifiedAt(page.ModifiedAt)), StringComparison.Ordinal)
            .Replace("{{body}}", H(page.Body), StringComparison.Ordinal);
    }

    public static string RenderWikiCreateForm(string path, string title, string markdown, string? error = null)
    {
        return Template("Wiki/CreateForm.html")
            .Replace("{{error}}", RenderWikiFormError(error), StringComparison.Ordinal)
            .Replace("{{path}}", H(path), StringComparison.Ordinal)
            .Replace("{{title}}", H(title), StringComparison.Ordinal)
            .Replace("{{markdown}}", H(markdown), StringComparison.Ordinal)
            .Replace("{{editorAssets}}", RenderMarkdownEditorAssets(), StringComparison.Ordinal);
    }

    public static string RenderWikiEditForm(string path, string title, string markdown, string? error = null)
    {
        return Template("Wiki/EditForm.html")
            .Replace("{{error}}", RenderWikiFormError(error), StringComparison.Ordinal)
            .Replace("{{path}}", H(path), StringComparison.Ordinal)
            .Replace("{{pathUrl}}", WikiPathUrl(path), StringComparison.Ordinal)
            .Replace("{{breadcrumbs}}", RenderWikiBreadcrumbs(path), StringComparison.Ordinal)
            .Replace("{{title}}", H(title), StringComparison.Ordinal)
            .Replace("{{markdown}}", H(markdown), StringComparison.Ordinal)
            .Replace("{{editorAssets}}", RenderMarkdownEditorAssets(), StringComparison.Ordinal);
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

    private static string RenderSidebar(BoardData board, SidebarPage activePage)
    {
        return Template("Layout/Sidebar.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{filterInputs}}", RenderFilterInputs(board.Query), StringComparison.Ordinal)
            .Replace("{{wholeProjectActive}}",
                activePage == SidebarPage.Board && IsWholeProject(board.Query) ? " active" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{wholeProjectAriaCurrent}}",
                activePage == SidebarPage.Board && IsWholeProject(board.Query) ? " aria-current=\"page\"" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{wikiActive}}", activePage == SidebarPage.Wiki ? " active" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{wikiAriaCurrent}}", activePage == SidebarPage.Wiki ? " aria-current=\"page\"" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{milestoneItems}}", RenderNavItems("milestone", board.Milestones, board.Query.Milestone),
                StringComparison.Ordinal)
            .Replace("{{trackItems}}", RenderNavItems("track", board.Tracks, board.Query.Track),
                StringComparison.Ordinal)
            .Replace("{{settingsActive}}", activePage == SidebarPage.Settings ? " active" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{settingsAriaCurrent}}",
                activePage == SidebarPage.Settings ? " aria-current=\"page\"" : string.Empty,
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

    public static string WikiPathUrl(string value)
    {
        return string.Join('/', value.Split('/').Select(Uri.EscapeDataString));
    }

    private static string RenderWikiBreadcrumbs(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var crumbs = new List<string>
        {
            """<a href="/wiki">Wiki</a>""",
        };

        for (var index = 0; index < segments.Length; index++)
        {
            var segmentPath = string.Join('/', segments.Take(index + 1));
            var label = H(segments[index]);
            if (index == segments.Length - 1)
                crumbs.Add($"""<span aria-current="page">{label}</span>""");
            else
                crumbs.Add($"""<a href="/wiki/{WikiPathUrl(segmentPath)}">{label}</a>""");
        }

        return $"""<nav class="breadcrumbs" aria-label="Wiki breadcrumbs">{string.Join("<span aria-hidden=\"true\">/</span>", crumbs)}</nav>""";
    }

    private static string RenderWikiFormError(string? error)
    {
        return string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : Template("Settings/Error.html")
                .Replace("{{message}}", H(error), StringComparison.Ordinal);
    }

    private static string RenderMarkdownEditorAssets()
    {
        return Template("Markdown/EditorAssets.html");
    }
}
