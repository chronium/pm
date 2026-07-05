using System.Net;

using PM.Application;
using PM.Project;

namespace PM.Web;

public static class BoardHtmlRenderer
{
    private enum ShellMode
    {
        Tasks,
        Wiki,
    }

    private enum SidebarPage
    {
        Board,
        Settings,
    }

    private enum WikiSidebarPage
    {
        Home,
        Other,
    }

    private enum WikiTreeActiveKind
    {
        None,
        Page,
        Folder,
    }

    private sealed class WikiTreeFolder(string label, string path)
    {
        public string Label { get; } = label;
        public string Path { get; } = path;
        public Dictionary<string, WikiTreeFolder> Folders { get; } = new(StringComparer.Ordinal);
        public List<WikiTreePage> Pages { get; } = [];
    }

    private sealed record WikiTreePage(string Title, string Path);

    public static string RenderPage(BoardData board)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{board.ProjectName} Board"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Tasks), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderTaskSidebar(board, SidebarPage.Board), StringComparison.Ordinal)
            .Replace("{{board}}", RenderBoard(board), StringComparison.Ordinal);
    }

    public static string RenderSettingsPage(
        BoardData board,
        ProjectSettingsData settings,
        string? error = null,
        ProjectValidationResult? validation = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(settings.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{settings.ProjectName} Settings"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(settings.ProjectName, ShellMode.Tasks), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderTaskSidebar(board, SidebarPage.Settings), StringComparison.Ordinal)
            .Replace("{{board}}", RenderSettings(settings, error, validation), StringComparison.Ordinal);
    }

    public static string RenderWikiIndexPage(BoardData board, IReadOnlyList<WikiPageSummary> pages)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar(pages, WikiSidebarPage.Home), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiIndex(pages), StringComparison.Ordinal);
    }

    public static string RenderWikiPage(
        BoardData board,
        WikiPageData page,
        IReadOnlyList<WikiPageSummary>? sidebarPages = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{page.Title} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar(sidebarPages ?? [], WikiSidebarPage.Other, page.Path,
                WikiTreeActiveKind.Page), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiDetail(page), StringComparison.Ordinal);
    }

    public static string RenderWikiFolderPage(
        BoardData board,
        string path,
        IReadOnlyList<WikiPageSummary> pages,
        IReadOnlyList<WikiPageSummary>? sidebarPages = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"{path} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar(sidebarPages ?? pages, WikiSidebarPage.Other, path,
                WikiTreeActiveKind.Folder), StringComparison.Ordinal)
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
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar([], WikiSidebarPage.Other), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiCreateForm(path, title, markdown, error), StringComparison.Ordinal);
    }

    public static string RenderWikiCreatePage(
        BoardData board,
        IReadOnlyList<WikiPageSummary> sidebarPages,
        string path = "",
        string title = "",
        string markdown = "",
        string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"New Wiki Page - {board.ProjectName}"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar(sidebarPages, WikiSidebarPage.Other), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiCreateForm(path, title, markdown, error), StringComparison.Ordinal);
    }

    public static string RenderWikiEditPage(BoardData board, WikiPageData page, string? error = null)
    {
        return RenderWikiEditPage(board, page.Path, page.Title, page.Body, error);
    }

    public static string RenderWikiEditPage(
        BoardData board,
        WikiPageData page,
        IReadOnlyList<WikiPageSummary> sidebarPages,
        string? error = null)
    {
        return RenderWikiEditPage(board, page.Path, page.Title, page.Body, sidebarPages, error);
    }

    public static string RenderWikiEditPage(
        BoardData board,
        string path,
        string title,
        string body,
        string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"Edit {title} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar([], WikiSidebarPage.Other), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiEditForm(path, title, body, error), StringComparison.Ordinal);
    }

    public static string RenderWikiEditPage(
        BoardData board,
        string path,
        string title,
        string body,
        IReadOnlyList<WikiPageSummary> sidebarPages,
        string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"Edit {title} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar(sidebarPages, WikiSidebarPage.Other, path,
                WikiTreeActiveKind.Page), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiEditForm(path, title, body, error), StringComparison.Ordinal);
    }

    public static string RenderWikiMetadataPage(
        BoardData board,
        WikiPageData page,
        IReadOnlyList<WikiPageSummary> sidebarPages,
        string? error = null)
    {
        return RenderWikiMetadataPage(board, page.Path, page.Path, page.Title, sidebarPages, error);
    }

    public static string RenderWikiMetadataPage(
        BoardData board,
        string currentPath,
        string path,
        string title,
        IReadOnlyList<WikiPageSummary> sidebarPages,
        string? error = null)
    {
        return Template("Layout/BoardPage.html")
            .Replace("{{projectName}}", H(board.ProjectName), StringComparison.Ordinal)
            .Replace("{{pageTitle}}", H($"Metadata {title} - {board.ProjectName} Wiki"), StringComparison.Ordinal)
            .Replace("{{styles}}", Template("Assets/styles.css"), StringComparison.Ordinal)
            .Replace("{{topBar}}", RenderTopBar(board.ProjectName, ShellMode.Wiki), StringComparison.Ordinal)
            .Replace("{{sidebar}}", RenderWikiSidebar(sidebarPages, WikiSidebarPage.Other, currentPath,
                WikiTreeActiveKind.Page), StringComparison.Ordinal)
            .Replace("{{board}}", RenderWikiMetadataForm(currentPath, path, title, error), StringComparison.Ordinal);
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
            .Replace("{{priority}}", RenderPriorityPill(task.Priority), StringComparison.Ordinal)
            .Replace("{{dependencies}}", RenderDependencies(task.Dependencies), StringComparison.Ordinal)
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

    public static string RenderTaskEditForm(
        BoardTask task,
        IReadOnlyList<BoardOption> states,
        BoardQuery query,
        string? title = null,
        string? targetState = null,
        string? description = null,
        string? priority = null,
        string? error = null)
    {
        var selectedState = targetState ?? task.State;
        var selectedPriority = priority ?? task.Task.Priority;
        return Template("Dialog/TaskEditForm.html")
            .Replace("{{error}}", RenderDialogFormError(error), StringComparison.Ordinal)
            .Replace("{{taskId}}", H(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{taskIdUrl}}", Url(task.Task.Id), StringComparison.Ordinal)
            .Replace("{{title}}", H(title ?? task.Task.Title), StringComparison.Ordinal)
            .Replace("{{stateOptions}}", RenderOptions(states, selectedState), StringComparison.Ordinal)
            .Replace("{{priorityOptions}}", RenderTaskPriorityOptions(selectedPriority), StringComparison.Ordinal)
            .Replace("{{description}}", H(description ?? task.Task.Description), StringComparison.Ordinal)
            .Replace("{{filterInputs}}", RenderFilterInputs(query), StringComparison.Ordinal)
            .Replace("{{editorAssets}}", RenderMarkdownEditorAssets(), StringComparison.Ordinal);
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

    public static string RenderSettings(
        ProjectSettingsData settings,
        string? error = null,
        ProjectValidationResult? validation = null)
    {
        var errorHtml = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : Template("Settings/Error.html")
                .Replace("{{message}}", H(error), StringComparison.Ordinal);

        return Template("Settings/Settings.html")
            .Replace("{{error}}", errorHtml, StringComparison.Ordinal)
            .Replace("{{health}}", RenderProjectHealth(validation), StringComparison.Ordinal)
            .Replace("{{priorityOptions}}", RenderPriorityOptions(PriorityLevel.None), StringComparison.Ordinal)
            .Replace("{{statusItems}}", RenderSettingsItems("statuses", settings.Statuses, "name"),
                StringComparison.Ordinal)
            .Replace("{{trackItems}}", RenderSettingsItems("tracks", settings.Tracks, "name"),
                StringComparison.Ordinal)
            .Replace("{{milestoneItems}}", RenderMilestoneSettingsItems(settings.Milestones),
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

    public static string RenderWikiMetadataForm(string currentPath, string path, string title, string? error = null)
    {
        return Template("Wiki/MetadataForm.html")
            .Replace("{{error}}", RenderWikiFormError(error), StringComparison.Ordinal)
            .Replace("{{currentPath}}", H(currentPath), StringComparison.Ordinal)
            .Replace("{{currentPathUrl}}", WikiPathUrl(currentPath), StringComparison.Ordinal)
            .Replace("{{path}}", H(path), StringComparison.Ordinal)
            .Replace("{{title}}", H(title), StringComparison.Ordinal)
            .Replace("{{breadcrumbs}}", RenderWikiBreadcrumbs(currentPath), StringComparison.Ordinal);
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

    private static string RenderTopBar(string projectName, ShellMode activeMode)
    {
        return Template("Layout/TopBar.html")
            .Replace("{{projectName}}", H(projectName), StringComparison.Ordinal)
            .Replace("{{tasksActive}}", activeMode == ShellMode.Tasks ? " active" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{tasksAriaCurrent}}", activeMode == ShellMode.Tasks ? " aria-current=\"page\"" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{wikiActive}}", activeMode == ShellMode.Wiki ? " active" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{wikiAriaCurrent}}", activeMode == ShellMode.Wiki ? " aria-current=\"page\"" : string.Empty,
                StringComparison.Ordinal);
    }

    private static string RenderTaskSidebar(BoardData board, SidebarPage activePage)
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

    private static string RenderWikiSidebar(
        IReadOnlyList<WikiPageSummary> pages,
        WikiSidebarPage activePage,
        string? activePath = null,
        WikiTreeActiveKind activeKind = WikiTreeActiveKind.None)
    {
        return Template("Layout/WikiSidebar.html")
            .Replace("{{wikiHomeActive}}", activePage == WikiSidebarPage.Home ? " active" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{wikiHomeAriaCurrent}}",
                activePage == WikiSidebarPage.Home ? " aria-current=\"page\"" : string.Empty,
                StringComparison.Ordinal)
            .Replace("{{wikiTree}}", RenderWikiTree(pages, activePath, activeKind), StringComparison.Ordinal);
    }

    private static string RenderWikiTree(
        IReadOnlyList<WikiPageSummary> pages,
        string? activePath,
        WikiTreeActiveKind activeKind)
    {
        if (pages.Count == 0) return string.Empty;

        var root = new WikiTreeFolder(string.Empty, string.Empty);
        foreach (var page in pages)
        {
            var segments = page.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;

            var folder = root;
            for (var index = 0; index < segments.Length - 1; index++)
            {
                var segmentPath = string.Join('/', segments.Take(index + 1));
                if (!folder.Folders.TryGetValue(segments[index], out var child))
                {
                    child = new WikiTreeFolder(segments[index], segmentPath);
                    folder.Folders.Add(segments[index], child);
                }

                folder = child;
            }

            folder.Pages.Add(new WikiTreePage(page.Title, page.Path));
        }

        var items = RenderWikiTreeItems(root, activePath, activeKind, 0);
        return string.IsNullOrWhiteSpace(items)
            ? string.Empty
            : $"""
        <section class="nav-section wiki-tree" aria-labelledby="wiki-pages-nav-title">
          <h2 id="wiki-pages-nav-title">Pages</h2>
{items}
        </section>
""";
    }

    private static string RenderWikiTreeItems(
        WikiTreeFolder folder,
        string? activePath,
        WikiTreeActiveKind activeKind,
        int depth)
    {
        var rendered = new List<string>();
        foreach (var child in folder.Folders.Values.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            rendered.Add(RenderWikiTreeFolder(child, activePath, activeKind, depth));

        foreach (var page in folder.Pages.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
            rendered.Add(RenderWikiTreePage(page, activePath, activeKind, depth));

        return string.Join(Environment.NewLine, rendered);
    }

    private static string RenderWikiTreeFolder(
        WikiTreeFolder folder,
        string? activePath,
        WikiTreeActiveKind activeKind,
        int depth)
    {
        var isActive = activeKind == WikiTreeActiveKind.Folder && string.Equals(folder.Path, activePath,
            StringComparison.Ordinal);
        var isOpen = IsWikiTreeBranchOpen(folder.Path, activePath);
        var children = RenderWikiTreeItems(folder, activePath, activeKind, depth + 1);

        return $"""
          <details class="wiki-tree-folder" style="--tree-depth: {depth}"{(isOpen ? " open" : string.Empty)}>
            <summary><a class="wiki-tree-link wiki-tree-folder-link{(isActive ? " active" : string.Empty)}" href="/wiki/{WikiPathUrl(folder.Path)}"{(isActive ? " aria-current=\"page\"" : string.Empty)}>{H(folder.Label)}</a></summary>
            <div class="wiki-tree-children">
{children}
            </div>
          </details>
""";
    }

    private static string RenderWikiTreePage(
        WikiTreePage page,
        string? activePath,
        WikiTreeActiveKind activeKind,
        int depth)
    {
        var isActive = activeKind == WikiTreeActiveKind.Page && string.Equals(page.Path, activePath,
            StringComparison.Ordinal);

        return $"""          <a class="wiki-tree-link wiki-tree-page-link{(isActive ? " active" : string.Empty)}" style="--tree-depth: {depth}" href="/wiki/{WikiPathUrl(page.Path)}"{(isActive ? " aria-current=\"page\"" : string.Empty)}>{H(page.Title)}</a>""";
    }

    private static bool IsWikiTreeBranchOpen(string folderPath, string? activePath)
    {
        return !string.IsNullOrWhiteSpace(activePath)
               && (string.Equals(folderPath, activePath, StringComparison.Ordinal)
                   || activePath.StartsWith(folderPath + "/", StringComparison.Ordinal));
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

    private static string RenderPriorityOptions(string selected)
    {
        return string.Join(Environment.NewLine, PriorityLevel.Values.Select(priority =>
            $"""        <option value="{H(priority)}"{(priority == selected ? " selected" : string.Empty)}>{H(priority)}</option>"""));
    }

    private static string RenderTaskPriorityOptions(string? selected)
    {
        var options = new List<string>
        {
            $"""        <option value=""{(string.IsNullOrWhiteSpace(selected) ? " selected" : string.Empty)}>Inherit</option>""",
        };
        options.AddRange(PriorityLevel.Values.Select(priority =>
            $"""        <option value="{H(priority)}"{(priority == selected ? " selected" : string.Empty)}>{H(priority)}</option>"""));
        return string.Join(Environment.NewLine, options);
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
            .Replace("{{priority}}", RenderPriorityPill(task.Priority), StringComparison.Ordinal)
            .Replace("{{modifiedAt}}", H(FormatModifiedAt(task.Task.ModifiedAt)), StringComparison.Ordinal)
            .Replace("{{preview}}", preview, StringComparison.Ordinal);
    }

    private static string RenderPriorityPill(string priority)
    {
        return string.Equals(priority, PriorityLevel.None, StringComparison.Ordinal)
            ? string.Empty
            : $"""    <span class="priority-pill">{H(priority)}</span>""";
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
            .Replace("{{valueLabel}}", valueName == "title" ? "Title" : "Name", StringComparison.Ordinal)
            .Replace("{{extra}}", string.Empty, StringComparison.Ordinal)));
    }

    private static string RenderMilestoneSettingsItems(IReadOnlyList<BoardOption> milestones)
    {
        return string.Join(Environment.NewLine, milestones.Select(milestone =>
        {
            var priorityForm = $"""
        <form class="settings-priority" hx-post="/settings/milestones/{Url(milestone.Key)}/priority" hx-target="#settings" hx-swap="outerHTML">
          <label data-field>Priority <select name="priority">
{RenderPriorityOptions(milestone.Priority)}
          </select></label>
          <button class="outline small" type="submit">Set priority</button>
        </form>
""";

            return Template("Settings/Item.html")
                .Replace("{{key}}", H(milestone.Key), StringComparison.Ordinal)
                .Replace("{{keyUrl}}", Url(milestone.Key), StringComparison.Ordinal)
                .Replace("{{name}}", H(milestone.Name), StringComparison.Ordinal)
                .Replace("{{collection}}", "milestones", StringComparison.Ordinal)
                .Replace("{{valueName}}", "title", StringComparison.Ordinal)
                .Replace("{{valueLabel}}", "Title", StringComparison.Ordinal)
                .Replace("{{extra}}", priorityForm, StringComparison.Ordinal);
        }));
    }

    private static string RenderProjectHealth(ProjectValidationResult? validation)
    {
        if (validation == null)
            return Template("Settings/Health.html")
                .Replace("{{healthClass}}", "unknown", StringComparison.Ordinal)
                .Replace("{{healthSummary}}", "Project health has not been checked.", StringComparison.Ordinal)
                .Replace("{{healthIssues}}", string.Empty, StringComparison.Ordinal);

        if (validation.Valid)
            return Template("Settings/Health.html")
                .Replace("{{healthClass}}", "valid", StringComparison.Ordinal)
                .Replace("{{healthSummary}}", "Project validation passed.", StringComparison.Ordinal)
                .Replace("{{healthIssues}}", string.Empty, StringComparison.Ordinal);

        var issues = string.Join(Environment.NewLine, validation.Issues.Select(RenderProjectHealthIssue));
        return Template("Settings/Health.html")
            .Replace("{{healthClass}}", "invalid", StringComparison.Ordinal)
            .Replace("{{healthSummary}}", H($"Project validation found {validation.Issues.Count} issue(s)."),
                StringComparison.Ordinal)
            .Replace("{{healthIssues}}", issues, StringComparison.Ordinal);
    }

    private static string RenderProjectHealthIssue(ProjectValidationIssue issue)
    {
        var context = RenderProjectHealthContext(issue);
        return Template("Settings/HealthIssue.html")
            .Replace("{{severity}}", H(issue.Severity), StringComparison.Ordinal)
            .Replace("{{code}}", H(issue.Code), StringComparison.Ordinal)
            .Replace("{{message}}", H(issue.Message), StringComparison.Ordinal)
            .Replace("{{context}}", context, StringComparison.Ordinal);
    }

    private static string RenderProjectHealthContext(ProjectValidationIssue issue)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(issue.TaskId)) parts.Add($"Task {issue.TaskId}");
        if (!string.IsNullOrWhiteSpace(issue.WikiPath)) parts.Add($"Wiki {issue.WikiPath}");
        if (!string.IsNullOrWhiteSpace(issue.State)) parts.Add($"State {issue.State}");
        if (!string.IsNullOrWhiteSpace(issue.Path)) parts.Add($"Path {issue.Path}");

        return parts.Count == 0
            ? string.Empty
            : $"""<span class="settings-health-context">{H(string.Join(" | ", parts))}</span>""";
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

    private static string RenderDialogFormError(string? error)
    {
        return string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : Template("Settings/Error.html")
                .Replace("{{message}}", H(error), StringComparison.Ordinal);
    }

    private static string RenderDependencies(DependencyStatus dependencies)
    {
        if (dependencies.DependsOn.Count == 0)
            return string.Empty;

        var ids = string.Join(", ", dependencies.DependsOn.Select(H));
        return $"""
                 <div class="task-dependencies">
                   <span>Dependencies</span>
                   <span>{ids}</span>
                   <span>{H(dependencies.Summary)}</span>
                 </div>
                 """;
    }

    private static string RenderMarkdownEditorAssets()
    {
        return Template("Markdown/EditorAssets.html");
    }
}
