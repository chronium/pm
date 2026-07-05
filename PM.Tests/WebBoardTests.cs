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
            new WikiService(projectRoot));

        var (exitCode, output) = await ExecuteWebCommand(command, new WebCommand.Settings());

        Assert.Equal(1, exitCode);
        Assert.Contains("Project not found", output);
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
    public async Task BoardPageRendersLeftNavLinksAndActiveFilter()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build <track>" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" }));

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
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" }));
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var settings = new ProjectConfigService(projectRoot).GetSettings().Payload!;

        var boardHtml = BoardHtmlRenderer.RenderPage(board);
        var settingsHtml = BoardHtmlRenderer.RenderSettingsPage(board, settings);

        Assert.Contains("href=\"/settings\"", boardHtml);
        Assert.Contains("Project settings", settingsHtml);
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
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone <one>" }));
        var settings = new ProjectConfigService(projectRoot).GetSettings().Payload!;

        var html = BoardHtmlRenderer.RenderSettings(settings);

        Assert.Contains("hx-post=\"/settings/statuses\"", html);
        Assert.Contains("hx-post=\"/settings/statuses/todo/rename\"", html);
        Assert.Contains("hx-post=\"/settings/statuses/todo/remove\"", html);
        Assert.Contains("hx-post=\"/settings/tracks/BUILD/rename\"", html);
        Assert.Contains("value=\"Build &lt;track&gt;\"", html);
        Assert.Contains("hx-post=\"/settings/milestones/m1/rename\"", html);
        Assert.Contains("value=\"Milestone &lt;one&gt;\"", html);
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

        var rename = service.RenameStatus("todo", "Ready");
        var refreshed = BoardHtmlRenderer.RenderSettings(service.GetSettings().Payload!);
        var blocked = service.RemoveStatus("todo");
        var error = BoardHtmlRenderer.RenderSettings(service.GetSettings().Payload!, blocked.Message);

        Assert.True(rename.Success);
        Assert.Contains("value=\"Ready\"", refreshed);
        Assert.Equal("status_in_use", blocked.ErrorCode);
        Assert.Contains("role=\"alert\"", error);
        Assert.Contains("Status todo is referenced by one or more tasks.", error);
        Assert.Contains("value=\"Ready\"", error);
    }

    [Fact]
    public async Task SidebarIncludesWikiLinkAndMarksWikiPagesActive()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;

        var boardHtml = BoardHtmlRenderer.RenderPage(board);
        var wikiHtml = BoardHtmlRenderer.RenderWikiIndexPage(board, []);

        Assert.Contains("href=\"/wiki\"", boardHtml);
        Assert.Contains("class=\"nav-item active\" href=\"/\" aria-current=\"page\"", boardHtml);
        Assert.DoesNotContain("class=\"nav-item active\" href=\"/wiki\"", boardHtml);
        Assert.Contains("class=\"nav-item active\" href=\"/wiki\" aria-current=\"page\"", wikiHtml);
        Assert.DoesNotContain("class=\"nav-item active\" href=\"/\" aria-current=\"page\"", wikiHtml);
        AssertBefore(wikiHtml, "href=\"/\"", "href=\"/wiki\"");
        AssertBefore(wikiHtml, "href=\"/wiki\"", "href=\"/settings\"");
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
        var missing = await client.GetAsync("/wiki/missing");
        var invalid = await client.GetAsync("/wiki/notes.txt");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Rendering", indexHtml);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("# Rendering", pageHtml);
        Assert.Equal(HttpStatusCode.OK, folder.StatusCode);
        Assert.Contains("aria-label=\"Wiki breadcrumbs\"", folderHtml);
        Assert.Contains("<span aria-current=\"page\">architecture</span>", folderHtml);
        Assert.Contains("href=\"/wiki/architecture/rendering\"", folderHtml);
        Assert.Contains("Rendering", folderHtml);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
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
        var pageHtml = BoardHtmlRenderer.RenderPage(board);
        Assert.Contains(".remove-confirmation[hidden]", pageHtml);
        Assert.Contains("display: none;", pageHtml);
        Assert.Contains(projectRoot.GetTaskFilePath("PM-0001"), html);
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
        Assert.Contains("# New body", savedHtml);
        Assert.Contains("hx-swap-oob=\"innerHTML\"", savedHtml);
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

        var result = taskService.UpdateTaskDetails("PM-0001", "Updated", "review", "New body");

        Assert.True(result.Success);
        Assert.Contains("Updated", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "review", "PM-0001.ref")));

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var boardTask = Assert.Single(board.Tasks);
        var html = BoardHtmlRenderer.RenderTaskUpdate(board, boardTask);

        Assert.Contains("Updated", html);
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
        WebCommand.Settings settings)
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
            var exitCode = await command.ExecuteAsync(null!, settings, CancellationToken.None);
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
            new WikiService(projectRoot));

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
