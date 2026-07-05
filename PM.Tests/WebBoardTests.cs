using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using PM.Application;
using PM.Project;
using PM.Tasks;
using PM.Web;
using PM.Wiki;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Tests;

public class WebBoardTests
{
    [Fact]
    public async Task WebOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var command = new WebCommand(projectRoot, new BoardService(projectRoot),
            new TaskService(projectRoot, new RecordingNextIdService()), new ProjectConfigService(projectRoot),
            new WikiService(projectRoot), new ProjectValidationService(projectRoot));

        var (exitCode, output) = await ExecuteWebCommand(command, new WebCommand.Settings());

        Assert.Equal(1, exitCode);
        Assert.Contains("Project not found", output);
    }

    [Fact]
    public async Task WebOpenFlagLaunchesResolvedLocalUrl()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var port = GetAvailablePort();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string? openedUrl = null;
        var command = new RecordingOpenWebCommand(
            projectRoot,
            new BoardService(projectRoot),
            new TaskService(projectRoot, new RecordingNextIdService()),
            new ProjectConfigService(projectRoot),
            new WikiService(projectRoot),
            new ProjectValidationService(projectRoot),
            url =>
            {
                openedUrl = url;
                cancellation.Cancel();
            });

        var (exitCode, output) = await ExecuteWebCommand(command, new WebCommand.Settings
        {
            Port = port,
            Open = true,
        }, cancellation.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal($"http://127.0.0.1:{port}", openedUrl);
        Assert.Contains($"Serving board at http://127.0.0.1:{port}", output);
    }

    [Fact]
    public async Task BoardDataGroupsByMilestoneAndState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var assigned = TestData.Task("BUILD-0001", "Assigned task", track: "BUILD", milestone: "m1");
        var unassigned = TestData.Task("PM-0001", "Unassigned task");
        projectRoot.WriteTask(assigned);
        projectRoot.WriteTask(unassigned);
        projectRoot.UpdateTaskState(assigned, "review");
        projectRoot.UpdateTaskState(unassigned, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;

        var milestone = Assert.Single(board.MilestoneGroups, group => group.Key == "m1");
        Assert.Equal("Milestone 1", milestone.Name);
        Assert.Contains(milestone.States.Single(state => state.Key == "review").Tasks,
            task => task.Task.Id == "BUILD-0001");

        var defaultMilestone = Assert.Single(board.MilestoneGroups, group => group.Key == null);
        Assert.Contains(defaultMilestone.States.Single(state => state.Key == "todo").Tasks,
            task => task.Task.Id == "PM-0001");
    }

    [Fact]
    public async Task BoardDataFiltersTrackMilestoneAndStateTogether()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var match = TestData.Task("BUILD-0001", "Matching task", track: "BUILD", milestone: "m1");
        var wrongTrack = TestData.Task("PM-0001", "Wrong track", track: "PM", milestone: "m1");
        var wrongMilestone = TestData.Task("BUILD-0002", "Wrong milestone", track: "BUILD", milestone: "m2");
        var wrongState = TestData.Task("BUILD-0003", "Wrong state", track: "BUILD", milestone: "m1");
        foreach (var item in new[] { match, wrongTrack, wrongMilestone, wrongState }) projectRoot.WriteTask(item);
        projectRoot.UpdateTaskState(match, "review");
        projectRoot.UpdateTaskState(wrongTrack, "review");
        projectRoot.UpdateTaskState(wrongMilestone, "review");
        projectRoot.UpdateTaskState(wrongState, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery("BUILD", "m1", "review")).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        Assert.Equal("Matching task", boardTask.Task.Title);
    }

    [Fact]
    public async Task LegacyTaskWithoutTrackUsesDefaultTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM"));
        var task = TestData.Task("PM-0001", "Legacy task", track: null);
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var boardTask = Assert.Single(board.Tasks);

        Assert.Equal("PM", boardTask.Track);
    }

    [Fact]
    public async Task BoardPageContainsExpectedTaskHtml()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Render <task>", "# Heading\n\nDetails");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var html = BoardHtmlRenderer.RenderPage(board);

        Assert.Contains("Render &lt;task&gt;", html);
        Assert.Contains("Heading", html);
        Assert.Contains("hx-get=\"/task/new\"", html);
        Assert.Contains("hx-get=\"/task/PM-0001\"", html);
        Assert.Contains("aria-label=\"Application navigation\"", html);
        Assert.Contains("class=\"mode-link active\" href=\"/\" aria-current=\"page\">Tasks</a>", html);
        Assert.Contains("class=\"mode-link\" href=\"/wiki\">Wiki</a>", html);
        Assert.Contains("aria-label=\"Board navigation\"", html);
        Assert.Contains("Whole project", html);
        Assert.Contains("Milestones", html);
        Assert.Contains("Tracks", html);
        Assert.Contains("https://unpkg.com/@knadh/oat@0.6.2/oat.min.css", html);
        Assert.Contains("https://unpkg.com/@knadh/oat@0.6.2/oat.min.js", html);
        Assert.Contains("class=\"board-list\"", html);
        Assert.Contains("class=\"state-row\"", html);
        Assert.Contains("class=\"task-row\"", html);
        Assert.Contains("dialog id=\"task-dialog\"", html);
        Assert.Contains("hx-target=\"#task-dialog\"", html);
        Assert.Contains("#task-dialog", html);
        Assert.Contains("overscroll-behavior: contain;", html);
        Assert.Contains("htmx:beforeSwap", html);
        Assert.DoesNotContain("class=\"state-section\"", html);
        Assert.DoesNotContain("class=\"state-tasks\"", html);
        Assert.DoesNotContain("<select name=\"track\"", html);
        Assert.DoesNotContain("<select name=\"milestone\"", html);
        Assert.DoesNotContain("<select name=\"state\"", html);
        Assert.DoesNotContain("<article class=\"task-detail", html);
        Assert.DoesNotContain("class=\"states\"", html);
        Assert.DoesNotContain("class=\"state\"", html);
        Assert.DoesNotContain(projectRoot.GetTaskFilePath("PM-0001"), html);
    }

    [Fact]
    public async Task BoardPageShowsEscapedPriorityPillForPrioritizedMilestoneTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "high" }));
        var task = TestData.Task("PM-0001", "Prioritized", milestone: "m1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var html = BoardHtmlRenderer.RenderPage(board);

        Assert.Contains("class=\"priority-pill\">high</span>", html);
        Assert.Contains("Milestone &lt;one&gt;", html);
    }

    [Fact]
    public async Task BoardPageRendersLeftNavLinksAndActiveFilter()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build <track>" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "urgent" }));

        var trackBoard = new BoardService(projectRoot).GetBoard(new BoardQuery(Track: "BUILD")).Payload!;
        var trackHtml = BoardHtmlRenderer.RenderPage(trackBoard);

        Assert.Contains("href=\"/?track=BUILD\"", trackHtml);
        Assert.Contains("href=\"/?milestone=m1\"", trackHtml);
        Assert.Contains("Build &lt;track&gt;", trackHtml);
        Assert.Contains("Milestone &lt;one&gt;", trackHtml);
        Assert.Contains("class=\"nav-item active\" href=\"/?track=BUILD\" aria-current=\"page\"", trackHtml);
        Assert.DoesNotContain("class=\"nav-item active\" href=\"/?milestone=m1\"", trackHtml);
        Assert.Contains("name=\"filterTrack\" value=\"BUILD\"", trackHtml);
        Assert.Contains("name=\"filterMilestone\" value=\"\"", trackHtml);

        var milestoneBoard = new BoardService(projectRoot).GetBoard(new BoardQuery(Milestone: "m1")).Payload!;
        var milestoneHtml = BoardHtmlRenderer.RenderPage(milestoneBoard);

        Assert.Contains("class=\"nav-item active\" href=\"/?milestone=m1\" aria-current=\"page\"", milestoneHtml);
        Assert.DoesNotContain("class=\"nav-item active\" href=\"/?track=BUILD\"", milestoneHtml);
        Assert.Contains("name=\"filterTrack\" value=\"\"", milestoneHtml);
        Assert.Contains("name=\"filterMilestone\" value=\"m1\"", milestoneHtml);
    }

    [Fact]
    public async Task SettingsLinkRendersInSidebarAndSettingsPageListsProjectOptions()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build <track>" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "urgent" }));
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var settings = new ProjectConfigService(projectRoot).GetSettings().Payload!;
        var validation = new ProjectValidationService(projectRoot).ValidateProject().Payload!;

        var boardHtml = BoardHtmlRenderer.RenderPage(board);
        var settingsHtml = BoardHtmlRenderer.RenderSettingsPage(board, settings, validation: validation);

        Assert.Contains("href=\"/settings\"", boardHtml);
        Assert.Contains("Project settings", settingsHtml);
        Assert.Contains("Project health", settingsHtml);
        Assert.Contains("Project validation passed.", settingsHtml);
        Assert.Contains("class=\"nav-item settings-link active\" href=\"/settings\" aria-current=\"page\"", settingsHtml);
        Assert.Contains("Queued", settingsHtml);
        Assert.Contains("Build &lt;track&gt;", settingsHtml);
        Assert.Contains("Milestone &lt;one&gt;", settingsHtml);
    }

    [Fact]
    public async Task SettingsFormsRenderAddRenameRemoveControlsWithEscapedValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build <track>" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "urgent" }));
        var settings = new ProjectConfigService(projectRoot).GetSettings().Payload!;
        var validation = new ProjectValidationService(projectRoot).ValidateProject().Payload!;

        var html = BoardHtmlRenderer.RenderSettings(settings, validation: validation);

        Assert.Contains("hx-post=\"/settings/statuses\"", html);
        Assert.Contains("hx-post=\"/settings/statuses/todo/rename\"", html);
        Assert.Contains("hx-post=\"/settings/statuses/todo/remove\"", html);
        Assert.Contains("hx-post=\"/settings/tracks/BUILD/rename\"", html);
        Assert.Contains("value=\"Build &lt;track&gt;\"", html);
        Assert.Contains("hx-post=\"/settings/milestones/m1/rename\"", html);
        Assert.Contains("hx-post=\"/settings/milestones/m1/priority\"", html);
        Assert.Contains("value=\"Milestone &lt;one&gt;\"", html);
        Assert.Contains("<option value=\"urgent\" selected>urgent</option>", html);
        Assert.Contains("<select name=\"priority\">", html);
        Assert.Contains("hx-target=\"#settings\"", html);
        Assert.DoesNotContain("Build <track>", html);
    }

    [Fact]
    public async Task SettingsMutationFragmentsReflectSuccessAndBlockedDeleteErrors()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Todo task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new ProjectConfigService(projectRoot);
        var validationService = new ProjectValidationService(projectRoot);

        var rename = service.RenameStatus("todo", "Ready");
        var refreshed = BoardHtmlRenderer.RenderSettings(service.GetSettings().Payload!,
            validation: validationService.ValidateProject().Payload!);
        var blocked = service.RemoveStatus("todo");
        var error = BoardHtmlRenderer.RenderSettings(service.GetSettings().Payload!, blocked.Message,
            validationService.ValidateProject().Payload!);

        Assert.True(rename.Success);
        Assert.Contains("value=\"Ready\"", refreshed);
        Assert.Contains("Project validation passed.", refreshed);
        Assert.Equal("status_in_use", blocked.ErrorCode);
        Assert.Contains("role=\"alert\"", error);
        Assert.Contains("Status todo is referenced by one or more tasks.", error);
        Assert.Contains("value=\"Ready\"", error);
        Assert.Contains("Project validation passed.", error);
    }

    [Fact]
    public async Task SettingsHealthRendersEscapedValidationIssues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project" }));
        var task = TestData.Task("PM-0001", "Escaped task", track: "missing<tr>");
        projectRoot.WriteTask(task);
        var settings = new ProjectConfigService(projectRoot).GetSettings().Payload!;
        var validation = new ProjectValidationService(projectRoot).ValidateProject().Payload!;

        var html = BoardHtmlRenderer.RenderSettings(settings, validation: validation);

        Assert.Contains("Project validation found 2 issue(s).", html);
        Assert.Contains("unknown_task_track", html);
        Assert.Contains("Task PM-0001 references unknown track missing&lt;tr&gt;.", html);
        Assert.Contains("Task PM-0001", html);
        Assert.Contains("Path ", html);
        Assert.DoesNotContain("missing<tr>", html);
    }

    [Fact]
    public async Task SettingsMutationFragmentIncludesRefreshedProjectHealth()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project" }));
        var task = TestData.Task("BUILD-0001", "Build task", track: "BUILD");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var (app, client) = await CreateWebClient(projectRoot);
        await using var appRegistration = app;
        using var clientRegistration = client;

        var response = await client.PostAsync("/settings/tracks",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = "BUILD",
                ["name"] = "Build",
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("id=\"settings\"", html);
        Assert.Contains("value=\"Build\"", html);
        Assert.Contains("Project validation passed.", html);
        Assert.DoesNotContain("unknown_task_track", html);
    }

    [Fact]
    public async Task SettingsMilestonePriorityMutationsRefreshSettingsAndHealth()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var (app, client) = await CreateWebClient(projectRoot);
        await using var appRegistration = app;
        using var clientRegistration = client;

        var add = await client.PostAsync("/settings/milestones",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"] = "m2",
                ["title"] = "Milestone 2",
                ["priority"] = "high",
            }));
        var addHtml = await add.Content.ReadAsStringAsync();
        var update = await client.PostAsync("/settings/milestones/m1/priority",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["priority"] = "urgent",
            }));
        var updateHtml = await update.Content.ReadAsStringAsync();

        Assert.True(add.IsSuccessStatusCode);
        Assert.Contains("value=\"Milestone 2\"", addHtml);
        Assert.Contains("<option value=\"high\" selected>high</option>", addHtml);
        Assert.Contains("Project validation passed.", addHtml);
        Assert.True(update.IsSuccessStatusCode);
        Assert.Contains("<option value=\"urgent\" selected>urgent</option>", updateHtml);
        Assert.Contains("Project validation passed.", updateHtml);
    }

    [Fact]
    public async Task TopModeBarAndModeSidebarsReflectActiveWorkspace()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            name: "Project <name>",
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build <track>" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" }));
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var settings = new ProjectConfigService(projectRoot).GetSettings().Payload!;
        var validation = new ProjectValidationService(projectRoot).ValidateProject().Payload!;

        var boardHtml = BoardHtmlRenderer.RenderPage(board);
        var wikiHtml = BoardHtmlRenderer.RenderWikiIndexPage(board, []);
        var settingsHtml = BoardHtmlRenderer.RenderSettingsPage(board, settings, validation: validation);

        Assert.Contains("Project &lt;name&gt;", boardHtml);
        Assert.Contains("class=\"nav-item active\" href=\"/\" aria-current=\"page\"", boardHtml);
        Assert.Contains("class=\"mode-link active\" href=\"/\" aria-current=\"page\">Tasks</a>", boardHtml);
        Assert.Contains("class=\"mode-link\" href=\"/wiki\">Wiki</a>", boardHtml);
        Assert.Contains("Build &lt;track&gt;", boardHtml);
        Assert.Contains("Milestone &lt;one&gt;", boardHtml);
        Assert.Contains("href=\"/settings\"", boardHtml);
        Assert.DoesNotContain("Wiki home", boardHtml);

        Assert.Contains("aria-label=\"Wiki navigation\"", wikiHtml);
        Assert.Contains("class=\"mode-link\" href=\"/\">Tasks</a>", wikiHtml);
        Assert.Contains("class=\"mode-link active\" href=\"/wiki\" aria-current=\"page\">Wiki</a>", wikiHtml);
        Assert.Contains("class=\"nav-item active\" href=\"/wiki\" aria-current=\"page\">Wiki home</a>", wikiHtml);
        Assert.Contains("href=\"/wiki/new\"", wikiHtml);
        Assert.DoesNotContain("Milestones", wikiHtml);
        Assert.DoesNotContain("Tracks", wikiHtml);
        Assert.DoesNotContain("href=\"/settings\"", wikiHtml);

        Assert.Contains("class=\"mode-link active\" href=\"/\" aria-current=\"page\">Tasks</a>", settingsHtml);
        Assert.Contains("class=\"nav-item settings-link active\" href=\"/settings\" aria-current=\"page\"", settingsHtml);
    }

    [Fact]
    public async Task WikiIndexRendersEmptyAndPopulatedStatesWithEscapedValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;

        var emptyHtml = BoardHtmlRenderer.RenderWikiIndexPage(board, []);

        Assert.Contains("No wiki pages yet.", emptyHtml);
        Assert.Contains("href=\"/wiki/new\"", emptyHtml);

        var page = new WikiPage
        {
            Path = "architecture/rendering",
            Title = "Render <wiki>",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc),
            Body = "Body",
        };
        projectRoot.WriteWikiPage(page);
        var pages = new WikiService(projectRoot).ListPages().Payload!;

        var html = BoardHtmlRenderer.RenderWikiIndexPage(board, pages);

        Assert.Contains("class=\"wiki-list\"", html);
        Assert.Contains("href=\"/wiki/new\"", html);
        Assert.Contains("href=\"/wiki/architecture/rendering\"", html);
        Assert.Contains("Render &lt;wiki&gt;", html);
        Assert.Contains("architecture/rendering", html);
        Assert.Contains("2026-01-02 03:04", html);
        Assert.DoesNotContain("Render <wiki>", html);
    }

    [Fact]
    public async Task WikiSidebarRendersTreeEscapesValuesAndMarksActiveBranch()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var modifiedAt = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc);
        var pages = new List<WikiPageSummary>
        {
            new("reference/zeta", "Zeta", modifiedAt, "/wiki/reference/zeta.md"),
            new("architecture/rendering", "Render <wiki>", modifiedAt, "/wiki/architecture/rendering.md"),
            new("architecture/api", "API", modifiedAt, "/wiki/architecture/api.md"),
            new("docs <x>/guide & notes/start", "Start <here>", modifiedAt, "/wiki/docs/start.md"),
        };

        var indexHtml = BoardHtmlRenderer.RenderWikiIndexPage(board, pages);
        var detailHtml = BoardHtmlRenderer.RenderWikiPage(board, new WikiPageData(
            "architecture/rendering",
            "Render <wiki>",
            modifiedAt,
            modifiedAt,
            "/wiki/architecture/rendering.md",
            "Body",
            "Body"), pages);
        var folderHtml = BoardHtmlRenderer.RenderWikiFolderPage(board, "architecture",
            pages.Where(page => page.Path.StartsWith("architecture/", StringComparison.Ordinal)).ToList(), pages);
        var createHtml = BoardHtmlRenderer.RenderWikiCreatePage(board, pages);
        var editHtml = BoardHtmlRenderer.RenderWikiEditPage(board, new WikiPageData(
            "architecture/api",
            "API",
            modifiedAt,
            modifiedAt,
            "/wiki/architecture/api.md",
            "Body",
            "Body"), pages);
        var taskHtml = BoardHtmlRenderer.RenderPage(board);
        var validation = new ProjectValidationService(projectRoot).ValidateProject().Payload!;
        var settingsHtml = BoardHtmlRenderer.RenderSettingsPage(board,
            new ProjectConfigService(projectRoot).GetSettings().Payload!,
            validation: validation);

        Assert.Contains("class=\"nav-section wiki-tree\"", indexHtml);
        Assert.Contains("href=\"/wiki/architecture\"", indexHtml);
        Assert.Contains("href=\"/wiki/architecture/rendering\"", indexHtml);
        Assert.Contains("Render &lt;wiki&gt;", indexHtml);
        Assert.Contains("docs &lt;x&gt;", indexHtml);
        Assert.Contains("guide &amp; notes", indexHtml);
        Assert.Contains("href=\"/wiki/docs%20%3Cx%3E/guide%20%26%20notes/start\"", indexHtml);
        Assert.DoesNotContain("wiki-tree-page-link active", indexHtml);
        Assert.DoesNotContain("wiki-tree-folder-link active", indexHtml);
        AssertBefore(indexHtml, "href=\"/wiki/architecture\"", "href=\"/wiki/reference\"");
        AssertBefore(indexHtml, "href=\"/wiki/architecture/api\"", "href=\"/wiki/architecture/rendering\"");

        Assert.Contains("<details class=\"wiki-tree-folder\" style=\"--tree-depth: 0\" open>", detailHtml);
        Assert.Contains("class=\"wiki-tree-link wiki-tree-page-link active\" style=\"--tree-depth: 1\" href=\"/wiki/architecture/rendering\" aria-current=\"page\">Render &lt;wiki&gt;</a>", detailHtml);
        Assert.DoesNotContain("href=\"/wiki/architecture\" aria-current=\"page\"", detailHtml);

        Assert.Contains("class=\"wiki-tree-link wiki-tree-folder-link active\" href=\"/wiki/architecture\" aria-current=\"page\">architecture</a>", folderHtml);
        Assert.DoesNotContain("wiki-tree-page-link active", folderHtml);

        Assert.DoesNotContain("wiki-tree-page-link active", createHtml);
        Assert.DoesNotContain("wiki-tree-folder-link active", createHtml);
        Assert.Contains("class=\"wiki-tree-link wiki-tree-page-link active\" style=\"--tree-depth: 1\" href=\"/wiki/architecture/api\" aria-current=\"page\">API</a>", editHtml);

        Assert.DoesNotContain("class=\"nav-section wiki-tree\"", taskHtml);
        Assert.DoesNotContain("class=\"nav-section wiki-tree\"", settingsHtml);
    }

    [Fact]
    public async Task WikiPageRendersMetadataFallbackAndClientRenderTarget()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var page = new WikiPage
        {
            Path = "notes",
            Title = "Notes <wiki>",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc),
            Body = "# Heading <unsafe>\n\n<script>alert(1)</script>",
        };
        projectRoot.WriteWikiPage(page);
        var data = new WikiService(projectRoot).ReadPage("notes").Payload!;

        var html = BoardHtmlRenderer.RenderWikiPage(board, data);

        Assert.Contains("Notes &lt;wiki&gt;", html);
        Assert.Contains("notes", html);
        Assert.Contains("aria-label=\"Wiki breadcrumbs\"", html);
        Assert.Contains("href=\"/wiki\">Wiki</a>", html);
        Assert.Contains("<span aria-current=\"page\">notes</span>", html);
        Assert.Contains(projectRoot.WikiPath, html);
        Assert.Contains("2026-01-02 03:04", html);
        Assert.Contains("id=\"wiki-content\"", html);
        Assert.Contains("id=\"wiki-markdown-source\" readonly hidden", html);
        Assert.Contains("id=\"wiki-markdown-fallback\"", html);
        Assert.Contains("href=\"/wiki/meta/notes\"", html);
        Assert.Contains("href=\"/wiki/edit/notes\"", html);
        Assert.Contains("# Heading &lt;unsafe&gt;", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("https://unpkg.com/marked@18.0.5/lib/marked.umd.js", html);
        Assert.Contains("https://unpkg.com/dompurify@3.4.11/dist/purify.min.js", html);
        Assert.Contains("DOMPurify.sanitize", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task WikiCreateAndEditFormsRenderEasyMdeAssetsAndEscapeValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var page = new WikiPage
        {
            Path = "notes/path <x>",
            Title = "Notes <wiki>",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc),
            Body = "# Body <unsafe>",
        };
        projectRoot.WriteWikiPage(page);
        var data = new WikiService(projectRoot).ReadPage("notes/path <x>").Payload!;

        var createHtml = BoardHtmlRenderer.RenderWikiCreatePage(board, "notes/<x>", "Title <x>", "Body <x>", "Nope <x>");
        var editHtml = BoardHtmlRenderer.RenderWikiEditPage(board, data, "Bad <x>");
        var metaHtml = BoardHtmlRenderer.RenderWikiMetadataPage(board, data, [new(data.Path, data.Title, data.ModifiedAt, data.FilePath)], "Bad <x>");

        foreach (var html in new[] { createHtml, editHtml })
        {
            Assert.Contains("data-markdown-editor", html);
            Assert.Contains("https://unpkg.com/easymde@2.20.0/dist/easymde.min.css", html);
            Assert.Contains("https://unpkg.com/easymde@2.20.0/dist/easymde.min.js", html);
            Assert.Contains("https://unpkg.com/marked@18.0.5/lib/marked.umd.js", html);
            Assert.Contains("https://unpkg.com/dompurify@3.4.11/dist/purify.min.js", html);
            Assert.Contains("DOMPurify.sanitize", html);
            Assert.DoesNotContain("Body <x>", html);
            Assert.DoesNotContain("Bad <x>", html);
        }

        Assert.Contains("notes/&lt;x&gt;", createHtml);
        Assert.Contains("Title &lt;x&gt;", createHtml);
        Assert.Contains("Bad &lt;x&gt;", editHtml);
        Assert.Contains("Notes &lt;wiki&gt;", editHtml);
        Assert.Contains("href=\"/wiki/notes\">notes</a>", editHtml);
        Assert.Contains("<span aria-current=\"page\">path &lt;x&gt;</span>", editHtml);
        Assert.Contains("# Body &lt;unsafe&gt;", editHtml);

        Assert.Contains("action=\"/wiki/meta/notes/path%20%3Cx%3E\"", metaHtml);
        Assert.Contains("action=\"/wiki/delete/notes/path%20%3Cx%3E\"", metaHtml);
        Assert.Contains("value=\"notes/path &lt;x&gt;\"", metaHtml);
        Assert.Contains("value=\"Notes &lt;wiki&gt;\"", metaHtml);
        Assert.Contains("Bad &lt;x&gt;", metaHtml);
        Assert.DoesNotContain("Bad <x>", metaHtml);
    }

    [Fact]
    public async Task WikiEndpointsRenderIndexNestedPageMissingAndInvalidResponses()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        projectRoot.WriteWikiPage(new WikiPage
        {
            Path = "architecture/rendering",
            Title = "Rendering",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Body = "# Rendering",
        });
        var web = await CreateWebClient(projectRoot);
        await using var app = web.App;
        using var client = web.Client;

        var index = await client.GetAsync("/wiki");
        var indexHtml = await index.Content.ReadAsStringAsync();
        var page = await client.GetAsync("/wiki/architecture/rendering");
        var pageHtml = await page.Content.ReadAsStringAsync();
        var folder = await client.GetAsync("/wiki/architecture");
        var folderHtml = await folder.Content.ReadAsStringAsync();
        var newForm = await client.GetAsync("/wiki/new");
        var newFormHtml = await newForm.Content.ReadAsStringAsync();
        var editForm = await client.GetAsync("/wiki/edit/architecture/rendering");
        var editFormHtml = await editForm.Content.ReadAsStringAsync();
        var metaForm = await client.GetAsync("/wiki/meta/architecture/rendering");
        var metaFormHtml = await metaForm.Content.ReadAsStringAsync();
        var missing = await client.GetAsync("/wiki/missing");
        var invalid = await client.GetAsync("/wiki/notes.txt");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Rendering", indexHtml);
        Assert.Contains("class=\"nav-section wiki-tree\"", indexHtml);
        Assert.DoesNotContain("wiki-tree-page-link active", indexHtml);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("# Rendering", pageHtml);
        Assert.Contains("class=\"wiki-tree-link wiki-tree-page-link active\" style=\"--tree-depth: 1\" href=\"/wiki/architecture/rendering\" aria-current=\"page\">Rendering</a>", pageHtml);
        Assert.Equal(HttpStatusCode.OK, folder.StatusCode);
        Assert.Contains("aria-label=\"Wiki breadcrumbs\"", folderHtml);
        Assert.Contains("<span aria-current=\"page\">architecture</span>", folderHtml);
        Assert.Contains("href=\"/wiki/architecture/rendering\"", folderHtml);
        Assert.Contains("class=\"wiki-tree-link wiki-tree-folder-link active\" href=\"/wiki/architecture\" aria-current=\"page\">architecture</a>", folderHtml);
        Assert.Contains("Rendering", folderHtml);
        Assert.Equal(HttpStatusCode.OK, newForm.StatusCode);
        Assert.Contains("class=\"nav-section wiki-tree\"", newFormHtml);
        Assert.DoesNotContain("wiki-tree-page-link active", newFormHtml);
        Assert.Equal(HttpStatusCode.OK, editForm.StatusCode);
        Assert.Contains("class=\"wiki-tree-link wiki-tree-page-link active\" style=\"--tree-depth: 1\" href=\"/wiki/architecture/rendering\" aria-current=\"page\">Rendering</a>", editFormHtml);
        Assert.Equal(HttpStatusCode.OK, metaForm.StatusCode);
        Assert.Contains("action=\"/wiki/meta/architecture/rendering\"", metaFormHtml);
        Assert.Contains("action=\"/wiki/delete/architecture/rendering\"", metaFormHtml);
        Assert.Contains("class=\"wiki-tree-link wiki-tree-page-link active\" style=\"--tree-depth: 1\" href=\"/wiki/architecture/rendering\" aria-current=\"page\">Rendering</a>", metaFormHtml);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task WikiMetadataAndDeleteEndpointsMutatePagesAndRenderValidationErrors()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        projectRoot.WriteWikiPage(new WikiPage
        {
            Path = "architecture/rendering",
            Title = "Rendering",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Body = "# Rendering",
        });
        projectRoot.WriteWikiPage(new WikiPage
        {
            Path = "reference/existing",
            Title = "Existing",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Body = "",
        });
        var web = await CreateWebClient(projectRoot);
        await using var app = web.App;
        using var client = web.Client;

        var duplicate = await client.PostAsync("/wiki/meta/architecture/rendering",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["path"] = "reference/existing",
                ["title"] = "Duplicate <x>",
            }));
        var duplicateHtml = await duplicate.Content.ReadAsStringAsync();
        var missingTitle = await client.PostAsync("/wiki/meta/architecture/rendering",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["path"] = "architecture/renamed",
                ["title"] = "",
            }));
        var missingTitleHtml = await missingTitle.Content.ReadAsStringAsync();
        var renamed = await client.PostAsync("/wiki/meta/architecture/rendering",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["path"] = "architecture/pipeline",
                ["title"] = "Render Pipeline",
            }));
        var renamedHtml = await renamed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("already exists", duplicateHtml);
        Assert.Contains("value=\"reference/existing\"", duplicateHtml);
        Assert.Contains("value=\"Duplicate &lt;x&gt;\"", duplicateHtml);
        Assert.Contains("class=\"nav-section wiki-tree\"", duplicateHtml);
        Assert.Equal(HttpStatusCode.BadRequest, missingTitle.StatusCode);
        Assert.Contains("title is required", missingTitleHtml);
        Assert.Contains("value=\"architecture/renamed\"", missingTitleHtml);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("/wiki/architecture/pipeline", renamed.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Render Pipeline", renamedHtml);
        Assert.Contains("# Rendering", renamedHtml);
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "pipeline.md")));
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));

        var deleteWithoutConfirmation = await client.PostAsync("/wiki/delete/architecture/pipeline",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["confirm"] = "" }));
        var deleteWithoutConfirmationHtml = await deleteWithoutConfirmation.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, deleteWithoutConfirmation.StatusCode);
        Assert.Contains("Type delete to confirm", deleteWithoutConfirmationHtml);
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "pipeline.md")));

        var deleted = await client.PostAsync("/wiki/delete/architecture/pipeline",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["confirm"] = "delete" }));
        var deletedHtml = await deleted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("/wiki", deleted.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("Render Pipeline", deletedHtml);
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "pipeline.md")));
    }

    [Fact]
    public async Task WikiCreateAndEditEndpointsMutatePagesAndRenderValidationErrors()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        projectRoot.WriteWikiPage(new WikiPage
        {
            Path = "architecture/rendering",
            Title = "Rendering",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Body = "# Rendering",
        });
        var web = await CreateWebClient(projectRoot);
        await using var app = web.App;
        using var client = web.Client;

        var newForm = await client.GetAsync("/wiki/new");
        var newFormHtml = await newForm.Content.ReadAsStringAsync();
        var created = await client.PostAsync("/wiki/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["path"] = "notes/new-page",
            ["title"] = "New Page",
            ["markdown"] = "# Hello",
        }));
        var createdHtml = await created.Content.ReadAsStringAsync();
        var duplicate = await client.PostAsync("/wiki/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["path"] = "notes/new-page",
            ["title"] = "Duplicate",
            ["markdown"] = "Nope",
        }));
        var invalid = await client.PostAsync("/wiki/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["path"] = "notes.txt",
            ["title"] = "Invalid",
            ["markdown"] = "Nope",
        }));
        var missingTitle = await client.PostAsync("/wiki/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["path"] = "notes/missing-title",
            ["title"] = "",
            ["markdown"] = "Nope",
        }));

        var editForm = await client.GetAsync("/wiki/edit/architecture/rendering");
        var editFormHtml = await editForm.Content.ReadAsStringAsync();
        var edited = await client.PostAsync("/wiki/edit/architecture/rendering",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["markdown"] = "# Updated" }));
        var editedHtml = await edited.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, newForm.StatusCode);
        Assert.Contains("New page", newFormHtml);
        Assert.Contains("data-markdown-editor", newFormHtml);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("New Page", createdHtml);
        Assert.Contains("# Hello", createdHtml);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingTitle.StatusCode);

        Assert.Equal(HttpStatusCode.OK, editForm.StatusCode);
        Assert.Contains("Edit Rendering", editFormHtml);
        Assert.Contains("data-markdown-editor", editFormHtml);
        Assert.Contains("# Rendering", editFormHtml);
        Assert.DoesNotContain("title: Rendering", editFormHtml);
        Assert.DoesNotContain("createdAt:", editFormHtml);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Contains("Rendering", editedHtml);
        Assert.DoesNotContain("Rendering Updated", editedHtml);
        Assert.Contains("# Updated", editedHtml);
    }

    [Fact]
    public async Task BoardRendersTasksGroupedByReversedStatusOrder()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var todo = TestData.Task("PM-0001", "Todo task");
        var review = TestData.Task("PM-0002", "Review task");
        var done = TestData.Task("PM-0003", "Done task");
        foreach (var task in new[] { todo, review, done }) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(todo, "todo");
        projectRoot.UpdateTaskState(review, "review");
        projectRoot.UpdateTaskState(done, "done");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var html = BoardHtmlRenderer.RenderBoard(board);

        AssertBefore(html, "id=\"state-done\"", "Done task");
        AssertBefore(html, "Done task", "id=\"state-review\"");
        AssertBefore(html, "id=\"state-review\"", "Review task");
        AssertBefore(html, "Review task", "id=\"state-todo\"");
        AssertBefore(html, "id=\"state-todo\"", "Todo task");
        Assert.DoesNotContain("state-section", html);
        Assert.DoesNotContain("state-tasks", html);
    }

    [Fact]
    public async Task BoardRowsContainEscapedTaskMetadataAndDialogTarget()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["BUILD"] = "Build <track>" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" }));
        var task = TestData.Task(
            "BUILD-0001",
            "Render <task>",
            "# Preview <body>\n\nDetails",
            "BUILD",
            "m1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "review");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var html = BoardHtmlRenderer.RenderBoard(board);

        Assert.Contains("BUILD-0001", html);
        Assert.Contains("Render &lt;task&gt;", html);
        Assert.Contains("Review", html);
        Assert.Contains(">BUILD<", html);
        Assert.Contains("Milestone &lt;one&gt;", html);
        Assert.Contains("2026-01-01 00:00", html);
        Assert.Contains("Preview &lt;body&gt;", html);
        Assert.Contains("hx-target=\"#task-dialog\"", html);
        Assert.DoesNotContain(projectRoot.GetTaskFilePath("BUILD-0001"), html);
    }

    [Fact]
    public async Task BoardTasksAreSortedByModifiedDescendingThenId()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var older = TestData.Task("PM-0001", "Older") with
        {
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var sameTimeFirst = TestData.Task("PM-0002", "First by ID") with
        {
            ModifiedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        var sameTimeSecond = TestData.Task("PM-0003", "Second by ID") with
        {
            ModifiedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        foreach (var task in new[] { older, sameTimeSecond, sameTimeFirst })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;

        Assert.Equal(["PM-0002", "PM-0003", "PM-0001"], board.Tasks.Select(task => task.Task.Id));
    }

    [Fact]
    public async Task TaskDetailContainsStateAndRemoveControlsWithEscapedFields()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Render <task>", "Description <body>");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        var html = BoardHtmlRenderer.RenderTaskDetail(boardTask, board.States);

        Assert.Contains("Render &lt;task&gt;", html);
        Assert.Contains("id=\"task-content\"", html);
        Assert.Contains("id=\"task-markdown-source\" readonly hidden", html);
        Assert.Contains("Description &lt;body&gt;", html);
        Assert.Contains("DOMPurify.sanitize", html);
        Assert.Contains("PM-0001", html);
        Assert.Contains("class=\"task-meta\"", html);
        Assert.Contains("class=\"task-state-compact\"", html);
        Assert.Contains("hx-post=\"/task/PM-0001/state\"", html);
        Assert.Contains("hx-get=\"/task/PM-0001/edit\"", html);
        Assert.Contains("name=\"targetState\"", html);
        Assert.Contains("<option value=\"todo\" selected>", html);
        Assert.Contains("class=\"task-file-meta\"", html);
        Assert.Contains("<summary>File</summary>", html);
        Assert.Contains("hx-post=\"/task/PM-0001/remove\"", html);
        Assert.Contains("data-confirm-remove", html);
        Assert.DoesNotContain("task-dependencies", html);
        var pageHtml = BoardHtmlRenderer.RenderPage(board);
        Assert.Contains(".remove-confirmation[hidden]", pageHtml);
        Assert.Contains("display: none;", pageHtml);
        Assert.Contains(projectRoot.GetTaskFilePath("PM-0001"), html);
    }

    [Fact]
    public async Task TaskDetailDisplaysEscapedDependenciesWhenPresent()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Render task", dependsOn: ["PM-<0002>"]);
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var html = BoardHtmlRenderer.RenderTaskDetail(Assert.Single(board.Tasks), board.States);

        Assert.Contains("class=\"task-dependencies\"", html);
        Assert.Contains("Dependencies", html);
        Assert.Contains("PM-&lt;0002&gt;", html);
        Assert.Contains("missing PM-&lt;0002&gt;", html);
    }

    [Fact]
    public async Task TaskMutationHtmlEscapesTaskFields()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project <track>" }));
        var task = TestData.Task("PM-0001", "Title <script>", "Body & notes", track: "PM");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        var html = BoardHtmlRenderer.RenderTaskUpdate(board, boardTask);

        Assert.Contains("Title &lt;script&gt;", html);
        Assert.Contains("Body &amp; notes", html);
        Assert.Contains(">PM<", html);
        Assert.Contains("hx-swap-oob=\"innerHTML\"", html);
    }

    [Fact]
    public async Task TaskCreateFormContainsFieldsAndPreservedFilters()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery("BUILD", "m1", "review")).Payload!;
        var html = BoardHtmlRenderer.RenderTaskCreateForm(board);

        Assert.Contains("hx-post=\"/task/new\"", html);
        Assert.Contains("name=\"title\"", html);
        Assert.Contains("name=\"track\"", html);
        Assert.Contains("<option value=\"BUILD\" selected>", html);
        Assert.Contains("name=\"milestone\"", html);
        Assert.Contains("<option value=\"m1\" selected>", html);
        Assert.Contains("name=\"description\"", html);
        Assert.Contains("name=\"filterTrack\" value=\"BUILD\"", html);
        Assert.Contains("name=\"filterMilestone\" value=\"m1\"", html);
        Assert.Contains("name=\"filterState\" value=\"review\"", html);
    }

    [Fact]
    public async Task TaskEditFormContainsStructuredFieldsEasyMdeAndPreservedFilters()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Render <task>", "Body <unsafe>");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery("PM", null, "todo")).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        var html = BoardHtmlRenderer.RenderTaskEditForm(boardTask, board.States, board.Query);

        Assert.Contains("hx-post=\"/task/PM-0001/edit\"", html);
        Assert.Contains("name=\"title\"", html);
        Assert.Contains("name=\"targetState\"", html);
        Assert.Contains("<option value=\"todo\" selected>", html);
        Assert.Contains("name=\"priority\"", html);
        Assert.Contains("<option value=\"\" selected>Inherit</option>", html);
        Assert.Contains("<option value=\"none\">none</option>", html);
        Assert.Contains("name=\"description\"", html);
        Assert.Contains("data-markdown-editor", html);
        Assert.Contains("data-markdown-editor-min-height=\"260px\"", html);
        Assert.Contains("form=\"task-edit-form\"", html);
        Assert.Contains("hx-get=\"/task/PM-0001\"", html);
        Assert.Contains("https://unpkg.com/easymde@2.20.0/dist/easymde.min.js", html);
        Assert.Contains("Render &lt;task&gt;", html);
        Assert.Contains("Body &lt;unsafe&gt;", html);
        Assert.Contains("name=\"filterTrack\" value=\"PM\"", html);
        Assert.Contains("name=\"filterState\" value=\"todo\"", html);
    }

    [Fact]
    public async Task TaskEditEndpointsRenderStructuredEditorAndSaveTitleStatusAndBody()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Original", "Old body");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var web = await CreateWebClient(projectRoot);
        await using var app = web.App;
        using var client = web.Client;

        var editForm = await client.GetAsync("/task/PM-0001/edit?track=PM&state=todo");
        var editFormHtml = await editForm.Content.ReadAsStringAsync();
        var saved = await client.PostAsync("/task/PM-0001/edit", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["title"] = "Updated",
                ["targetState"] = "review",
                ["priority"] = "high",
                ["description"] = "# New body",
                ["filterTrack"] = "",
                ["filterMilestone"] = "",
                ["filterState"] = "",
            }));
        var savedHtml = await saved.Content.ReadAsStringAsync();
        var invalid = await client.PostAsync("/task/PM-0001/edit", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["title"] = "",
                ["targetState"] = "review",
                ["priority"] = "later",
                ["description"] = "Nope",
                ["filterTrack"] = "",
                ["filterMilestone"] = "",
                ["filterState"] = "",
            }));
        var invalidHtml = await invalid.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, editForm.StatusCode);
        Assert.Contains("data-markdown-editor", editFormHtml);
        Assert.Contains("name=\"title\"", editFormHtml);
        Assert.Contains("name=\"targetState\"", editFormHtml);
        Assert.Contains("name=\"description\"", editFormHtml);
        Assert.DoesNotContain("createdAt:", editFormHtml);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("Updated", savedHtml);
        Assert.Contains("priority-pill\">high</span>", savedHtml);
        Assert.Contains("# New body", savedHtml);
        Assert.Contains("hx-swap-oob=\"innerHTML\"", savedHtml);
        Assert.Contains("priority: high", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "review", "PM-0001.ref")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("Task title is required.", invalidHtml);
        Assert.Contains("data-markdown-editor", invalidHtml);
    }

    [Fact]
    public async Task CreatingTaskWritesMarkdownStateRefAndRendersFragments()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var taskService = new TaskService(projectRoot, new RecordingNextIdService());

        var result = await taskService.CreateTask("Build task", "BUILD", "m1", "Body", false);

        Assert.True(result.Success);
        Assert.Equal("BUILD-0001", result.Payload!.Id);
        Assert.True(File.Exists(projectRoot.GetTaskFilePath("BUILD-0001")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "BUILD-0001.ref")));

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        var html = BoardHtmlRenderer.RenderTaskCreated(board, boardTask);

        Assert.Contains("Build task", html);
        Assert.Contains("hx-swap-oob=\"innerHTML\"", html);
    }

    [Fact]
    public async Task CreatingTaskFailuresRenderDialogErrors()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var invalidTrack = await new TaskService(projectRoot, new RecordingNextIdService()).CreateTask(
            "Bad",
            "NOPE",
            null,
            "",
            false);
        var unavailableNextId = await new TaskService(projectRoot, new RecordingNextIdService(healthy: false)).CreateTask(
            "Bad",
            "PM",
            null,
            "",
            false);

        Assert.False(invalidTrack.Success);
        Assert.Equal("invalid_track", invalidTrack.ErrorCode);
        Assert.False(unavailableNextId.Success);
        Assert.Equal("next_id_unavailable", unavailableNextId.ErrorCode);

        var errorHtml = BoardHtmlRenderer.RenderDialogError(invalidTrack.Message!, "Unable to create task");
        Assert.Contains("Unable to create task", errorHtml);
        Assert.Contains("Track NOPE not found.", errorHtml);
    }

    [Fact]
    public async Task EditingTaskUpdatesStructuredFieldsAndRendersFragments()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Original", "Old body");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var taskService = new TaskService(projectRoot, new RecordingNextIdService());

        var result = taskService.UpdateTaskDetails("PM-0001", "Updated", "review", "New body", "urgent");

        Assert.True(result.Success);
        Assert.Contains("Updated", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));
        Assert.Contains("priority: urgent", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "review", "PM-0001.ref")));

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        var html = BoardHtmlRenderer.RenderTaskUpdate(board, boardTask);

        Assert.Contains("Updated", html);
        Assert.Contains("priority-pill\">urgent</span>", html);
        Assert.Contains("New body", html);
        Assert.Contains("hx-swap-oob=\"innerHTML\"", html);
    }

    [Fact]
    public async Task InvalidEditMarkdownPreservesOriginalFileAndRendersError()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Original", "Old body");
        projectRoot.WriteTask(task);
        var taskService = new TaskService(projectRoot, new RecordingNextIdService());
        var original = File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001"));

        var invalidMarkdown = taskService.SaveEditedTaskContent("PM-0001", "not markdown");
        var changedId = taskService.SaveEditedTaskContent(
            "PM-0001",
            TestData.Task("PM-0002", "Changed").ToMarkdown());

        Assert.False(invalidMarkdown.Success);
        Assert.Equal("invalid_edited_markdown", invalidMarkdown.ErrorCode);
        Assert.False(changedId.Success);
        Assert.Equal("changed_task_id", changedId.ErrorCode);
        Assert.Equal(original, File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        var errorHtml = BoardHtmlRenderer.RenderDialogError(changedId.Message!, "Unable to edit task");
        Assert.Contains("Unable to edit task", errorHtml);
        Assert.Contains("Task ID cannot be changed.", errorHtml);
    }

    [Fact]
    public async Task FilteredBoardHtmlContainsOnlyMatchingTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" }));
        var match = TestData.Task("BUILD-0001", "Matching task", track: "BUILD");
        var other = TestData.Task("PM-0001", "Other task", track: "PM");
        projectRoot.WriteTask(match);
        projectRoot.WriteTask(other);
        projectRoot.UpdateTaskState(match, "todo");
        projectRoot.UpdateTaskState(other, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery(Track: "BUILD")).Payload!;
        var html = BoardHtmlRenderer.RenderBoard(board);

        Assert.Contains("Matching task", html);
        Assert.DoesNotContain("Other task", html);
    }

    [Fact]
    public async Task MilestoneFilteredBoardHtmlContainsOnlyMatchingTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var match = TestData.Task("PM-0001", "Matching milestone", milestone: "m1");
        var other = TestData.Task("PM-0002", "Other milestone", milestone: "m2");
        projectRoot.WriteTask(match);
        projectRoot.WriteTask(other);
        projectRoot.UpdateTaskState(match, "todo");
        projectRoot.UpdateTaskState(other, "todo");

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery(Milestone: "m1")).Payload!;
        var html = BoardHtmlRenderer.RenderBoard(board);

        Assert.Contains("Matching milestone", html);
        Assert.DoesNotContain("Other milestone", html);
    }

    [Fact]
    public async Task MovingTaskUpdatesStateRefsAndRendersUpdatedFragments()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Move me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var taskService = new TaskService(projectRoot, new RecordingNextIdService());

        var result = taskService.MoveTask("PM-0001", "review");

        Assert.True(result.Success);
        Assert.True(projectRoot.TryGetById("PM-0001", out var moved));
        Assert.True(projectRoot.TryGetState(moved, out var state));
        Assert.Equal("review", state);

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery(State: "review")).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        var html = BoardHtmlRenderer.RenderTaskUpdate(board, boardTask);

        Assert.Contains("Move me", html);
        Assert.Contains("<option value=\"review\" selected>", html);
        Assert.Contains("hx-swap-oob=\"innerHTML\"", html);
    }

    [Fact]
    public async Task RemovingTaskDeletesFilesAndRendersCloseDialogFragment()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Remove me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var taskService = new TaskService(projectRoot, new RecordingNextIdService());

        var result = taskService.RemoveTask("PM-0001");

        Assert.True(result.Success);
        Assert.False(File.Exists(projectRoot.GetTaskFilePath("PM-0001")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var html = BoardHtmlRenderer.RenderTaskRemoval(board);

        Assert.Contains("hx-swap-oob=\"innerHTML\"", html);
        Assert.Contains("task-dialog", html);
        Assert.Contains("close()", html);
    }

    [Fact]
    public async Task InvalidStateAndMissingTaskReturnErrorsWithoutMutatingFiles()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Stay put");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var taskService = new TaskService(projectRoot, new RecordingNextIdService());

        var invalidState = taskService.MoveTask("PM-0001", "missing");
        var missingTask = taskService.MoveTask("PM-9999", "review");

        Assert.False(invalidState.Success);
        Assert.Equal("invalid_state", invalidState.ErrorCode);
        Assert.False(missingTask.Success);
        Assert.Equal("missing_task", missingTask.ErrorCode);
        Assert.True(projectRoot.TryGetById("PM-0001", out var unchanged));
        Assert.True(projectRoot.TryGetState(unchanged, out var state));
        Assert.Equal("todo", state);

        var errorHtml = BoardHtmlRenderer.RenderDialogError(invalidState.Message!);
        Assert.Contains("State missing not found.", errorHtml);
    }

    private static async Task<(int ExitCode, string Output)> ExecuteWebCommand(
        WebCommand command,
        WebCommand.Settings settings,
        CancellationToken cancellationToken = default)
    {
        var originalConsole = AnsiConsole.Console;
        using var writer = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new FixedWidthConsoleOutput(writer),
        });

        try
        {
            var exitCode = await command.ExecuteAsync(null!, settings, cancellationToken);
            return (exitCode, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateWebClient(ProjectRoot projectRoot)
    {
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);

        var app = builder.Build();
        WebCommand.MapEndpoints(
            app,
            new BoardService(projectRoot),
            new TaskService(projectRoot, new RecordingNextIdService()),
            new ProjectConfigService(projectRoot),
            new WikiService(projectRoot),
            new ProjectValidationService(projectRoot));

        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(url) });
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void AssertBefore(string content, string first, string second)
    {
        var firstIndex = content.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = content.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Expected to find '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected to find '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' to appear before '{second}'.");
    }

    private sealed class FixedWidthConsoleOutput(TextWriter writer) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => false;
        public int Width => 240;
        public int Height => 80;

        public void SetEncoding(System.Text.Encoding encoding)
        {
        }
    }

    private sealed class RecordingOpenWebCommand(
        ProjectRoot projectRoot,
        BoardService boardService,
        TaskService taskService,
        ProjectConfigService configService,
        WikiService wikiService,
        ProjectValidationService validationService,
        Action<string> onOpen) : WebCommand(projectRoot, boardService, taskService, configService, wikiService,
        validationService)
    {
        protected override void OpenBrowser(string url)
        {
            onOpen(url);
        }
    }

    private sealed class RecordingNextIdService(bool healthy = true) : INextIdService
    {
        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(1);
        }

        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProjectRegistration("project-test", "recovery-test"));
        }

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(healthy);
        }
    }
}
