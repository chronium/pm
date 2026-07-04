using PM.Application;
using PM.Project;
using PM.Tasks;
using PM.Web;
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
            new TaskService(projectRoot, new RecordingNextIdService()), new ProjectConfigService(projectRoot));

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
        Assert.DoesNotContain("task-detail", html);
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
        Assert.Contains("Description &lt;body&gt;", html);
        Assert.Contains("PM-0001", html);
        Assert.Contains("hx-post=\"/task/PM-0001/state\"", html);
        Assert.Contains("hx-get=\"/task/PM-0001/edit\"", html);
        Assert.Contains("name=\"targetState\"", html);
        Assert.Contains("<option value=\"todo\" selected>", html);
        Assert.Contains("hx-post=\"/task/PM-0001/remove\"", html);
        Assert.Contains("data-confirm-remove", html);
        Assert.Contains("remove-confirmation[hidden] { display: none; }", BoardHtmlRenderer.RenderPage(board));
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
    public async Task TaskEditFormContainsEscapedMarkdownAndPreservedFilters()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Render <task>", "Body <unsafe>");
        projectRoot.WriteTask(task);

        var markdown = new TaskService(projectRoot, new RecordingNextIdService()).ReadTaskMarkdown("PM-0001").Payload!;
        var html = BoardHtmlRenderer.RenderTaskEditForm("PM-0001", markdown, new BoardQuery("PM", null, "todo"));

        Assert.Contains("hx-post=\"/task/PM-0001/edit\"", html);
        Assert.Contains("name=\"markdown\"", html);
        Assert.Contains("Render &lt;task&gt;", html);
        Assert.Contains("Body &lt;unsafe&gt;", html);
        Assert.Contains("name=\"filterTrack\" value=\"PM\"", html);
        Assert.Contains("name=\"filterState\" value=\"todo\"", html);
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
    public async Task EditingTaskUpdatesMarkdownAndRendersFragments()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config());
        var task = TestData.Task("PM-0001", "Original", "Old body");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var taskService = new TaskService(projectRoot, new RecordingNextIdService());
        var edited = task with { Title = "Updated", Description = "New body" };

        var result = taskService.SaveEditedTaskContent("PM-0001", edited.ToMarkdown());

        Assert.True(result.Success);
        Assert.Contains("Updated", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

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

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(healthy);
        }
    }
}
