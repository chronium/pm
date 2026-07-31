using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using PM.Application;
using PM.Mcp;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public class McpToolTests
{
    [Fact]
    public void MissingProjectReturnsStructuredFailure()
    {
        using var workspace = new TempWorkingDirectory();
        var tools = CreateTools(new ProjectRoot());

        var result = tools.GetProject();

        Assert.False(result.Success);
        Assert.Equal("missing_project", result.ErrorCode);
        Assert.Equal("Project not found. Run pm init first.", result.Message);
    }

    [Fact]
    public async Task GetProjectReturnsConfigData()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            name: "MCP Test",
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "high" }));
        var tools = CreateTools(projectRoot);

        var result = tools.GetProject();

        Assert.True(result.Success);
        Assert.Equal("MCP Test", result.Data!.Name);
        Assert.Equal(projectRoot.RootPath, result.Data.RootPath);
        Assert.Contains(result.Data.Tracks, track => track.Key == "BUILD" && track.Name == "Build");
        Assert.Contains(result.Data.Milestones,
            milestone => milestone.Key == "m1" && milestone.Priority == "high");
        Assert.Contains(result.Data.States, state => state.Key == "todo" && state.Name == "Queued");
    }

    [Fact]
    public async Task CreateProjectInitializesAndReturnsStructuredPayload()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var tools = CreateTools(projectRoot);

        var result = await tools.CreateProject(
            "MCP Project",
            idWidth: 3,
            idPrefix: "BUG",
            nextIdServiceUrl: "http://ids.local",
            states: new Dictionary<string, string?> { ["todo"] = "Todo" },
            tracks: new Dictionary<string, string?> { ["BUG"] = "Bugs" },
            milestones: new Dictionary<string, string?> { ["v1"] = "Version 1" });

        Assert.True(result.Success);
        Assert.Equal("MCP Project", result.Data!.Name);
        Assert.Equal(projectRoot.RootPath, result.Data.RootPath);
        Assert.Contains(result.Data.Tracks, track => track.Key == "BUG" && track.Name == "Bugs");
        Assert.Contains(result.Data.Milestones, milestone => milestone.Key == "v1" && milestone.Priority == "none");
        Assert.Contains(result.Data.States, state => state.Key == "todo" && state.Name == "Todo");
    }

    [Fact]
    public async Task CreateProjectReturnsValidationFailuresForBlankOptions()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var nextIds = new RecordingNextIdService();
        var tools = CreateTools(projectRoot, nextIds);

        var result = await tools.CreateProject(
            "MCP Project",
            milestones: new Dictionary<string, string?> { ["m1"] = null });

        Assert.False(result.Success);
        Assert.Equal("invalid_milestones", result.ErrorCode);
        Assert.Equal(0, nextIds.HealthyCalls);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, GlobalConfig.PmDirName)));
    }

    [Fact]
    public async Task ListTasksFiltersByTrackMilestoneAndState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "medium" }));
        var match = TestData.Task("BUILD-0001", "Matching task", "- Preview line", "BUILD", "m1",
            dependsOn: ["PM-0001"]);
        var wrongTrack = TestData.Task("PM-0001", "Wrong track", track: "PM", milestone: "m1");
        var wrongMilestone = TestData.Task("BUILD-0002", "Wrong milestone", track: "BUILD", milestone: "m2");
        projectRoot.WriteTask(match);
        projectRoot.WriteTask(wrongTrack);
        projectRoot.WriteTask(wrongMilestone);
        projectRoot.UpdateTaskState(match, "review");
        projectRoot.UpdateTaskState(wrongTrack, "review");
        projectRoot.UpdateTaskState(wrongMilestone, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.ListTasks("BUILD", "m1", "review");

        Assert.True(result.Success);
        var task = Assert.Single(result.Data!.Tasks);
        Assert.Equal("BUILD-0001", task.Id);
        Assert.Equal("Preview line", task.DescriptionPreview);
        Assert.Equal("review", task.State);
        Assert.Equal("medium", task.Priority);
        Assert.Equal("milestone", task.PrioritySource);
        Assert.Equal(["PM-0001"], task.DependsOn);
        Assert.False(task.DependenciesReady);
        Assert.Equal(["PM-0001"], task.WaitingOnDependencies);
    }

    [Fact]
    public async Task GetNextTaskReturnsStructuredTaskAndReason()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "urgent" }));
        var task = TestData.Task("PM-0001", "Next task", "- Preview line", milestone: "m1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask();

        Assert.True(result.Success);
        Assert.True(result.Data!.Found);
        Assert.Equal("PM-0001", result.Data.Task!.Id);
        Assert.Equal("Next task", result.Data.Task.Title);
        Assert.Equal("todo", result.Data.Task.State);
        Assert.Equal("urgent", result.Data.Task.Priority);
        Assert.Equal("milestone", result.Data.Task.PrioritySource);
        Assert.True(result.Data.Task.DependenciesReady);
        Assert.Equal("no dependencies", result.Data.Task.DependencySummary);
        Assert.Equal("Preview line", result.Data.Task.DescriptionPreview);
        Assert.Contains("urgent priority", result.Data.Reason);
        Assert.Contains("state todo", result.Data.Reason);
        Assert.Contains("no dependencies", result.Data.Reason);
        Assert.Equal(result.Data.Reason, result.Summary);
    }

    [Fact]
    public async Task GetNextTaskFiltersByTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" }));
        var projectTask = TestData.Task("PM-0001", "Project task", track: "PM") with
        {
            ModifiedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        var buildTask = TestData.Task("BUILD-0001", "Build task", track: "BUILD") with
        {
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        projectRoot.WriteTask(projectTask);
        projectRoot.WriteTask(buildTask);
        projectRoot.UpdateTaskState(projectTask, "todo");
        projectRoot.UpdateTaskState(buildTask, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask(" BUILD ");

        Assert.True(result.Success);
        Assert.Equal("BUILD-0001", result.Data!.Task!.Id);
        Assert.Equal("BUILD", result.Data.Task.Track);
    }

    [Fact]
    public async Task GetNextTaskFiltersByMilestone()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "First", ["m2"] = "Second" }));
        var first = TestData.Task("PM-0001", "First", milestone: "m1");
        var second = TestData.Task("PM-0002", "Second", milestone: "m2");
        projectRoot.WriteTask(first);
        projectRoot.WriteTask(second);
        projectRoot.UpdateTaskState(first, "todo");
        projectRoot.UpdateTaskState(second, "todo");

        var result = CreateTools(projectRoot).GetNextTask(milestone: " m2 ");

        Assert.True(result.Success);
        Assert.Equal("PM-0002", result.Data!.Task!.Id);
        Assert.Equal("m2", result.Data.Task.Milestone);
    }

    [Fact]
    public async Task GetNextTaskDefaultCanReturnBlockedTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var blocked = TestData.Task("PM-0001", "Blocked", dependsOn: ["PM-9999"]);
        projectRoot.WriteTask(blocked);
        projectRoot.UpdateTaskState(blocked, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask();

        Assert.True(result.Success);
        Assert.True(result.Data!.Found);
        Assert.Equal("PM-0001", result.Data.Task!.Id);
        Assert.False(result.Data.Task.DependenciesReady);
        Assert.Equal(["PM-9999"], result.Data.Task.MissingDependencies);
    }

    [Fact]
    public async Task GetNextTaskReadyOnlyReturnsNoTaskWhenAllCandidatesAreBlocked()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var blocked = TestData.Task("PM-0001", "Blocked", dependsOn: ["PM-9999"]);
        projectRoot.WriteTask(blocked);
        projectRoot.UpdateTaskState(blocked, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask(readyOnly: true);

        Assert.True(result.Success);
        Assert.False(result.Data!.Found);
        Assert.Null(result.Data.Task);
        Assert.Equal("No dependency-ready actionable task found.", result.Data.Reason);
        Assert.Equal(result.Data.Reason, result.Summary);
    }

    [Fact]
    public async Task GetNextTaskReadyOnlyReturnsBestReadyTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var blocked = TestData.Task("PM-0001", "Blocked urgent", priority: "urgent", dependsOn: ["PM-9999"]);
        var ready = TestData.Task("PM-0002", "Ready low", priority: "low");
        projectRoot.WriteTask(blocked);
        projectRoot.WriteTask(ready);
        projectRoot.UpdateTaskState(blocked, "todo");
        projectRoot.UpdateTaskState(ready, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask(readyOnly: true);

        Assert.True(result.Success);
        Assert.True(result.Data!.Found);
        Assert.Equal("PM-0002", result.Data.Task!.Id);
        Assert.True(result.Data.Task.DependenciesReady);
    }

    [Fact]
    public async Task GetNextTaskReadyOnlyRespectsTrackFilter()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" }));
        var projectReady = TestData.Task("PM-0001", "Project ready", track: "PM");
        var buildBlocked = TestData.Task("BUILD-0001", "Build blocked", track: "BUILD", dependsOn: ["BUILD-9999"]);
        projectRoot.WriteTask(projectReady);
        projectRoot.WriteTask(buildBlocked);
        projectRoot.UpdateTaskState(projectReady, "todo");
        projectRoot.UpdateTaskState(buildBlocked, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask("BUILD", readyOnly: true);

        Assert.True(result.Success);
        Assert.False(result.Data!.Found);
        Assert.Null(result.Data.Task);
        Assert.Equal("No dependency-ready actionable task found for track BUILD.", result.Data.Reason);
    }

    [Fact]
    public async Task GetNextTaskReturnsSuccessWhenNoActionableTaskExists()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Done task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "done");
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask();

        Assert.True(result.Success);
        Assert.False(result.Data!.Found);
        Assert.Null(result.Data.Task);
        Assert.Equal("No actionable task found.", result.Data.Reason);
    }

    [Fact]
    public async Task GetNextTaskReturnsStructuredFailureForInvalidTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        var result = tools.GetNextTask("NOPE");
        var readyOnly = tools.GetNextTask("NOPE", readyOnly: true);

        Assert.False(result.Success);
        Assert.Equal("invalid_track", result.ErrorCode);
        Assert.Equal("Track NOPE not found.", result.Message);
        Assert.False(readyOnly.Success);
        Assert.Equal("invalid_track", readyOnly.ErrorCode);
        Assert.Equal("Track NOPE not found.", readyOnly.Message);
        var invalidMilestone = tools.GetNextTask(milestone: "missing");
        Assert.False(invalidMilestone.Success);
        Assert.Equal("invalid_milestone", invalidMilestone.ErrorCode);
    }

    [Fact]
    public async Task GetTaskReturnsMarkdownAndState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "low" }));
        var task = TestData.Task("PM-0001", "Existing", "Body text", milestone: "m1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.GetTask("PM-0001");

        Assert.True(result.Success);
        Assert.Equal("PM-0001", result.Data!.Id);
        Assert.Equal("todo", result.Data.State);
        Assert.Equal("low", result.Data.Priority);
        Assert.Equal("milestone", result.Data.PrioritySource);
        Assert.Empty(result.Data.DependsOn);
        Assert.True(result.Data.DependenciesReady);
        Assert.Equal("Body text", result.Data.Description);
        Assert.Contains("title: Existing", result.Data.Markdown);
        Assert.Equal(projectRoot.GetTaskFilePath("PM-0001"), result.Data.FilePath);
    }

    [Fact]
    public async Task SearchTasksReturnsStructuredPayloadAndHonorsLimit()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var blocked = TestData.Task("PM-0001", "Needle render task",
            "Needle body context for agents.",
            milestone: "m1",
            priority: "urgent",
            dependsOn: ["PM-0002"]);
        var dependency = TestData.Task("PM-0002", "Dependency task");
        var limitedOut = TestData.Task("PM-0003", "Needle later task");
        projectRoot.WriteTask(blocked);
        projectRoot.WriteTask(dependency);
        projectRoot.WriteTask(limitedOut);
        projectRoot.UpdateTaskState(blocked, "review");
        projectRoot.UpdateTaskState(dependency, "todo");
        projectRoot.UpdateTaskState(limitedOut, "todo");
        var tools = CreateTools(projectRoot);

        var search = tools.SearchTasks("NEEDLE", 1);
        var blank = tools.SearchTasks(" ");

        Assert.True(search.Success);
        Assert.Equal("Returned 1 task search result(s).", search.Summary);
        var result = Assert.Single(search.Data!.Tasks);
        Assert.Equal("PM-0001", result.Id);
        Assert.Equal("Needle render task", result.Title);
        Assert.Equal("PM", result.Track);
        Assert.Equal("m1", result.Milestone);
        Assert.Equal("urgent", result.Priority);
        Assert.Equal("task", result.PrioritySource);
        Assert.Equal("review", result.State);
        Assert.Equal(["PM-0002"], result.DependsOn);
        Assert.False(result.DependenciesReady);
        Assert.Equal(["PM-0002"], result.WaitingOnDependencies);
        Assert.Empty(result.MissingDependencies);
        Assert.Equal(projectRoot.GetTaskFilePath("PM-0001"), result.FilePath);
        Assert.True(result.MatchCount > 0);
        Assert.Contains("needle", result.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("invalid_task_query", blank.ErrorCode);

        var structured = tools.SearchTasks("state:review milestone:m1");
        Assert.Equal("PM-0001", Assert.Single(structured.Data!.Tasks).Id);
        Assert.Equal(3, tools.SearchTasks("in:all").Data!.Tasks.Count);
    }

    [Fact]
    public async Task CreateTaskCreatesTrackScopedTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var nextIds = new RecordingNextIdService();
        var tools = CreateTools(projectRoot, nextIds);

        var result = await tools.CreateTask("Build thing", "BUILD", "m1", "Details");

        Assert.True(result.Success);
        Assert.Equal("BUILD-0001", result.Data!.Id);
        Assert.Equal(["BUILD"], nextIds.GetNextIdTracks);
        Assert.True(File.Exists(Path.Combine(projectRoot.TasksPath, "BUILD-0001.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "BUILD-0001.ref")));
    }

    [Fact]
    public async Task BulkCreateTasksForTrackReturnsCreatedTasksAndPartialFailure()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var nextIds = new RecordingNextIdService(ids: [1], failWhenIdsExhausted: true);
        var tools = CreateTools(projectRoot, nextIds);

        var result = await tools.BulkCreateTasksForTrack("PM",
        [
            new BulkTaskInputPayload("First", "Body"),
            new BulkTaskInputPayload("Second"),
        ]);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.RequestedCount);
        Assert.Equal(1, result.Data.CreatedCount);
        var created = Assert.Single(result.Data.Tasks);
        Assert.Equal("PM-0001", created.Id);
        Assert.Equal("Body", TaskItem.Parse(File.ReadAllText(created.FilePath))!.Description);
        Assert.Equal("next_id_unavailable", result.Data.Failure!.ErrorCode);
    }

    [Fact]
    public async Task BulkAssignTasksToMilestoneReturnsUpdatedTaskPayload()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var tools = CreateTools(projectRoot);

        var result = tools.BulkAssignTasksToMilestone("m1", ["PM-0001"]);

        Assert.True(result.Success);
        Assert.Equal("m1", result.Data!.Milestone);
        Assert.Equal(["PM-0001"], result.Data.TaskIds);
        Assert.Equal([projectRoot.GetTaskFilePath("PM-0001")], result.Data.FilePaths);
        Assert.Equal(1, result.Data.UpdatedCount);
        Assert.Equal("m1", TaskItem.Parse(File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")))!.Milestone);
    }

    [Fact]
    public async Task BulkAssignTasksToMilestoneRejectsDuplicateIdsBeforeWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var originalContent = File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001"));
        var tools = CreateTools(projectRoot);

        var result = tools.BulkAssignTasksToMilestone("m1", ["PM-0001", " PM-0001 "]);

        Assert.False(result.Success);
        Assert.Equal("duplicate_task_id", result.ErrorCode);
        Assert.Equal(originalContent, File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));
    }

    [Fact]
    public async Task MoveTaskUpdatesStateRefs()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Move me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        var result = tools.MoveTask("PM-0001", "done");

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "done", "PM-0001.ref")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task RemoveTaskDeletesFilesAndReportsMissingTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Remove me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        var missing = tools.RemoveTask("PM-9999");
        var removed = tools.RemoveTask("PM-0001");

        Assert.False(missing.Success);
        Assert.Equal("missing_task", missing.ErrorCode);
        Assert.True(removed.Success);
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task UpdateTaskMarkdownRejectsInvalidMarkdownAndChangedIds()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var tools = CreateTools(projectRoot);

        var invalid = tools.UpdateTaskMarkdown("PM-0001", "not markdown");
        var changedId = tools.UpdateTaskMarkdown("PM-0001", TestData.Task("PM-0002", "Changed").ToMarkdown());
        var updated = tools.UpdateTaskMarkdown("PM-0001", (task with { Title = "Updated" }).ToMarkdown());

        Assert.False(invalid.Success);
        Assert.Equal("invalid_edited_markdown", invalid.ErrorCode);
        Assert.False(changedId.Success);
        Assert.Equal("changed_task_id", changedId.ErrorCode);
        Assert.True(updated.Success);
        Assert.Contains("title: Updated", File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task WikiToolsCreateReadListAndUpdatePages()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        var created = tools.CreateWikiPage("architecture/rendering", "Rendering", "# Rendering");
        var list = tools.ListWikiPages();
        var read = tools.GetWikiPage("architecture/rendering");
        var updatedMarkdown = read.Data!.Markdown.Replace("title: Rendering", "title: Render Pipeline")
            .Replace("# Rendering", "# Updated");
        var updated = tools.UpdateWikiPageMarkdown("architecture/rendering", updatedMarkdown);

        Assert.True(created.Success);
        Assert.Equal("architecture/rendering", created.Data!.Path);
        Assert.Equal(projectRoot.TryResolveWikiPath("architecture/rendering", out _, out var filePath) ? filePath : "",
            created.Data.FilePath);
        var page = Assert.Single(list.Data!.Pages);
        Assert.Equal("architecture/rendering", page.Path);
        Assert.Equal("Rendering", read.Data.Title);
        Assert.Equal("# Rendering", read.Data.Body);
        Assert.True(updated.Success);
        Assert.Equal("Render Pipeline", updated.Data!.Title);
        Assert.Equal("# Updated", updated.Data.Body);
    }

    [Fact]
    public async Task WikiOutlineAndPatchToolsReturnStructuredPayloadsAndStableFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);
        tools.CreateWikiPage("architecture/rendering", "Rendering", """
                                                                    # Rendering
                                                                    Overview.

                                                                    ## Pipeline
                                                                    Existing details.

                                                                    ### Child
                                                                    Child details.
                                                                    """);

        var outline = tools.OutlineWikiPage("architecture/rendering");
        var heading = Assert.Single(outline.Data!.Headings, item => item.Title == "Pipeline");
        var patched = tools.PatchWikiPage("architecture/rendering", outline.Data.Version, heading.Id,
            WikiPatchOperation.AppendToSection, "New details.");
        var stale = tools.PatchWikiPage("architecture/rendering", outline.Data.Version, heading.Id,
            WikiPatchOperation.AppendToSection, "Too late.");
        var missingHeading = tools.PatchWikiPage("architecture/rendering", patched.Data!.Version, "h2-missing-1",
            WikiPatchOperation.AppendToSection, "No target.");

        Assert.True(outline.Success);
        Assert.Equal("architecture/rendering", outline.Data.Path);
        Assert.False(string.IsNullOrWhiteSpace(outline.Data.Version));
        Assert.Equal("h2-pipeline-1", heading.Id);
        Assert.Equal(["Rendering", "Pipeline"], heading.Breadcrumb);
        Assert.Contains("Existing details.", heading.Preview);

        Assert.True(patched.Success);
        Assert.Equal("architecture/rendering", patched.Data.Page.Path);
        Assert.Contains("Existing details.\n\nNew details.\n\n### Child", patched.Data.Page.Body);
        Assert.DoesNotContain("Child details.\n\nNew details.", patched.Data.Page.Body);
        Assert.NotEqual(outline.Data.Version, patched.Data.Version);

        Assert.Equal("stale_wiki_page", stale.ErrorCode);
        Assert.Equal("missing_wiki_heading", missingHeading.ErrorCode);
    }

    [Fact]
    public void PatchWikiPageOperationSchemaAdvertisesAcceptedEnumValues()
    {
        var tools = CreateTools(new ProjectRoot());
        var method = typeof(PmMcpTools).GetMethod(nameof(PmMcpTools.PatchWikiPage))!;
        var tool = McpServerTool.Create(method, tools);
        var schemaJson = JsonSerializer.Serialize(tool.ProtocolTool.InputSchema);
        using var document = JsonDocument.Parse(schemaJson);
        var root = document.RootElement;
        var operation = root.GetProperty("properties").GetProperty("operation");

        Assert.Equal("patch_wiki_page", tool.ProtocolTool.Name);
        Assert.Contains("schema enum", tool.ProtocolTool.Description);
        Assert.Equal(WikiPatchOperation.AppendToSection,
            JsonSerializer.Deserialize<WikiPatchOperation>("\"append_to_section\""));
        Assert.Equal([
                "append_to_section",
                "prepend_to_section",
                "replace_section_body",
                "insert_before_heading",
                "insert_after_section",
            ],
            ResolveSchemaEnumValues(root, operation));
    }

    [Fact]
    public async Task WikiToolsReturnStableFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var missingTools = CreateTools(new ProjectRoot());

        Assert.Equal("missing_project", missingTools.ListWikiPages().ErrorCode);

        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        Assert.Equal("invalid_wiki_path", tools.CreateWikiPage("../escape", "Escape").ErrorCode);
        Assert.Equal("missing_wiki_page", tools.GetWikiPage("missing").ErrorCode);
        Assert.Equal("missing_wiki_page", tools.OutlineWikiPage("missing").ErrorCode);
        Assert.True(tools.CreateWikiPage("notes", "Notes").Success);
        Assert.Equal("duplicate_wiki_page", tools.CreateWikiPage("notes", "Duplicate").ErrorCode);
        Assert.Equal("invalid_wiki_markdown", tools.UpdateWikiPageMarkdown("notes", "not markdown").ErrorCode);
    }

    [Fact]
    public async Task WikiRenameAndRemoveToolsMutatePagesAndReturnStableFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        var created = tools.CreateWikiPage("architecture/rendering", "Rendering", "# Rendering");
        Assert.True(created.Success);

        var renamed = tools.RenameWikiPage("architecture/rendering", "reference/rendering", "Rendering Reference");
        Assert.True(renamed.Success);
        Assert.Equal("reference/rendering", renamed.Data!.Path);
        Assert.Equal("Rendering Reference", renamed.Data.Title);
        Assert.Equal("# Rendering", renamed.Data.Body);
        Assert.Equal(created.Data!.CreatedAt, renamed.Data.CreatedAt);
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));

        Assert.Equal("missing_wiki_page", tools.RenameWikiPage("missing", "reference/missing", "Missing").ErrorCode);
        Assert.Equal("invalid_wiki_path", tools.RenameWikiPage("reference/rendering", "../escape", "Escape").ErrorCode);
        Assert.Equal("invalid_wiki_page", tools.RenameWikiPage("reference/rendering", "reference/rendering", "").ErrorCode);

        var removed = tools.RemoveWikiPage("reference/rendering");
        Assert.True(removed.Success);
        Assert.True(removed.Data!.Changed);
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "reference", "rendering.md")));
        Assert.Equal("missing_wiki_page", tools.RemoveWikiPage("reference/rendering").ErrorCode);
        Assert.Equal("invalid_wiki_path", tools.RemoveWikiPage("../escape").ErrorCode);
    }

    [Fact]
    public async Task TaskMetadataNoteAndReorderToolsReturnStructuredPayloads()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var first = TestData.Task("PM-0001", "First");
        var second = TestData.Task("PM-0002", "Second");
        projectRoot.WriteTask(first);
        projectRoot.WriteTask(second);
        projectRoot.UpdateTaskState(first, "todo");
        projectRoot.UpdateTaskState(second, "todo");
        var tools = CreateTools(projectRoot);

        var metadata = tools.UpdateTaskMetadata("PM-0001", title: "Updated", track: "BUILD", milestone: "m1",
            description: "Body", priority: "urgent", dependsOn: [" PM-0002 ", "PM-0002", "BUILD-0001"]);
        var cleared = tools.UpdateTaskMetadata("PM-0001", priority: "inherit");
        var none = tools.UpdateTaskMetadata("PM-0001", priority: "none");
        var invalidPriority = tools.UpdateTaskMetadata("PM-0001", priority: "later");
        var invalidDependency = tools.UpdateTaskMetadata("PM-0001", dependsOn: ["PM-0001"]);
        const string qualifiedReference = "pm://project/prj_other/task/OTHER-0001";
        var qualifiedDependency = tools.UpdateTaskMetadata("PM-0001", dependsOn: [qualifiedReference]);
        var malformedReference = tools.UpdateTaskMetadata("PM-0001", dependsOn: ["pm:not-a-reference"]);
        var note = tools.AppendTaskNote("PM-0001", "MCP note");
        var reorder = tools.ReorderTasks("PM", "todo", ["PM-0002"]);

        Assert.True(metadata.Success);
        Assert.True(metadata.Data!.Changed);
        Assert.Equal("Updated", metadata.Data.Task.Title);
        Assert.Equal("BUILD", metadata.Data.Task.Track);
        Assert.Equal("m1", metadata.Data.Task.Milestone);
        Assert.Equal("urgent", metadata.Data.Task.Priority);
        Assert.Equal("task", metadata.Data.Task.PrioritySource);
        Assert.Equal(["PM-0002", "BUILD-0001"], metadata.Data.Task.DependsOn);
        Assert.False(metadata.Data.Task.DependenciesReady);
        Assert.Equal(["PM-0002"], metadata.Data.Task.WaitingOnDependencies);
        Assert.Equal(["BUILD-0001"], metadata.Data.Task.MissingDependencies);
        Assert.Contains("title: Updated", metadata.Data.Task.Markdown);
        Assert.True(cleared.Success);
        Assert.DoesNotContain("priority:", cleared.Data!.Task.Markdown);
        Assert.Equal("none", cleared.Data.Task.Priority);
        Assert.Equal("none", cleared.Data.Task.PrioritySource);
        Assert.True(none.Success);
        Assert.Equal("none", none.Data!.Task.Priority);
        Assert.Equal("task", none.Data.Task.PrioritySource);
        Assert.Equal("invalid_priority", invalidPriority.ErrorCode);
        Assert.Equal("invalid_dependency", invalidDependency.ErrorCode);
        Assert.True(qualifiedDependency.Success);
        Assert.Equal([qualifiedReference], qualifiedDependency.Data!.Task.DependsOn);
        Assert.Equal([qualifiedReference], qualifiedDependency.Data.Task.MissingDependencies);
        Assert.Equal("invalid_dependency_reference", malformedReference.ErrorCode);
        Assert.True(note.Success);
        Assert.Contains("MCP note", note.Data!.Task.Description);
        Assert.True(reorder.Success);
        Assert.Equal(["PM-0002"], reorder.Data!.TaskIds);
        Assert.Equal("invalid_task_order", tools.ReorderTasks("PM", "todo", ["PM-0002", "PM-9999"]).ErrorCode);
    }

    [Fact]
    public async Task SearchWikiAndValidateProjectToolsReturnStructuredPayloads()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);
        tools.CreateWikiPage("architecture/rendering", "Rendering", "Canvas rendering details");

        var search = tools.SearchWikiPages("render", 5);
        var invalidSearch = tools.SearchWikiPages(" ");
        var validation = tools.ValidateProject();

        Assert.True(search.Success);
        var page = Assert.Single(search.Data!.Pages);
        Assert.Equal("architecture/rendering", page.Path);
        Assert.True(page.MatchCount >= 2);
        Assert.Equal("invalid_wiki_query", invalidSearch.ErrorCode);
        Assert.True(validation.Success);
        Assert.True(validation.Data!.Valid);
        Assert.Empty(validation.Data.Issues);
    }

    [Fact]
    public async Task LinkedProjectToolsReturnPartialFamilyAndValidationWarnings()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile), "prj_active\n");
        projectRoot.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                new LinkedProjectDeclaration
                {
                    ProjectId = "prj_missing",
                    Alias = "missing",
                    RepositoryUrl = "https://example.test/missing.git",
                    PathHint = "missing",
                },
            ],
        });
        var tools = CreateTools(projectRoot);

        var family = await tools.ListLinkedProjects();
        var validation = tools.ValidateProject();

        Assert.True(family.Success);
        Assert.Equal(2, family.Data!.Members.Count);
        var missing = Assert.Single(family.Data.Members, member => member.ProjectId == "prj_missing");
        Assert.Equal("missing", missing.Status);
        Assert.False(missing.Readable);
        var warning = Assert.Single(family.Data.Warnings, item => item.Code == "linked_project_missing");
        Assert.Equal("prj_missing", warning.TargetProjectId);
        Assert.DoesNotContain(projectRoot.RepositoryPath, JsonSerializer.Serialize(family.Data));
        Assert.True(validation.Success);
        Assert.True(validation.Data!.Valid);
        Assert.Contains(validation.Data.Issues, issue =>
            issue.Severity == "warning" && issue.ProjectId == "prj_missing");
    }

    [Fact]
    public async Task AddTrackAndMilestoneReturnDuplicateAndInvalidErrors()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var tools = CreateTools(projectRoot);

        Assert.True(tools.AddTrack("BUILD", "Build").Success);
        Assert.Equal("duplicate_track", tools.AddTrack("BUILD", "Duplicate").ErrorCode);
        Assert.Equal("invalid_track", tools.AddTrack(" ", "Missing").ErrorCode);

        Assert.True(tools.AddMilestone("m1", "Milestone 1", "HIGH").Success);
        Assert.Equal("duplicate_milestone", tools.AddMilestone("m1", "Duplicate").ErrorCode);
        Assert.Equal("invalid_milestone", tools.AddMilestone("m2", " ").ErrorCode);
        Assert.Equal("invalid_priority", tools.AddMilestone("m2", "Milestone 2", "later").ErrorCode);

        var project = tools.GetProject();
        Assert.Contains(project.Data!.Milestones,
            milestone => milestone.Key == "m1" && milestone.Priority == "high");
    }

    [Fact]
    public async Task SetMilestonePriorityReturnsStructuredResultAndUpdatesPayloads()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var tools = CreateTools(projectRoot);

        var update = tools.SetMilestonePriority("m1", "Urgent");
        var invalid = tools.SetMilestonePriority("m1", "later");
        var missing = tools.SetMilestonePriority("missing", "high");

        Assert.True(update.Success);
        Assert.Equal("invalid_priority", invalid.ErrorCode);
        Assert.Equal("missing_milestone", missing.ErrorCode);

        var milestone = Assert.Single(tools.ListMilestones().Data!);
        Assert.Equal("urgent", milestone.Priority);
    }

    [Fact]
    public async Task StatusToolsAddRenameRemoveAndReturnStableFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Todo task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var tools = CreateTools(projectRoot);

        Assert.True(tools.AddStatus("blocked", "Blocked").Success);
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "blocked")) ||
                    Directory.Exists(Path.Combine(projectRoot.StatesPath, "blocked")));
        Assert.Equal("duplicate_status", tools.AddStatus("blocked", "Duplicate").ErrorCode);
        Assert.True(tools.RenameStatus("todo", "Ready").Success);
        Assert.Equal("status_in_use", tools.RemoveStatus("todo").ErrorCode);
        Assert.True(tools.RemoveStatus("blocked").Success);
        Assert.False(Directory.Exists(Path.Combine(projectRoot.StatesPath, "blocked")));
        Assert.Equal("missing_status", tools.RemoveStatus("missing").ErrorCode);

        var project = tools.GetProject();
        Assert.Contains(project.Data!.States, state => state.Key == "todo" && state.Name == "Ready");
        Assert.DoesNotContain(project.Data.States, state => state.Key == "blocked");
    }

    [Fact]
    public async Task RenameTrackAndMilestoneToolsWorkWhileReferenced()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        projectRoot.WriteTask(TestData.Task("BUILD-0001", "Build task", track: "BUILD", milestone: "m1"));
        var tools = CreateTools(projectRoot);

        Assert.True(tools.RenameTrack("BUILD", "Build Work").Success);
        Assert.True(tools.RenameMilestone("m1", "Launch").Success);
        Assert.Equal("missing_track", tools.RenameTrack("missing", "Missing").ErrorCode);
        Assert.Equal("missing_milestone", tools.RenameMilestone("missing", "Missing").ErrorCode);

        var project = tools.GetProject();
        Assert.Contains(project.Data!.Tracks, track => track.Key == "BUILD" && track.Name == "Build Work");
        Assert.Contains(project.Data.Milestones, milestone => milestone.Key == "m1" && milestone.Name == "Launch");
    }

    [Fact]
    public async Task RemoveTrackAndMilestoneRejectReferencedItemsAndRemoveUnusedItems()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build", ["UI"] = "UI" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" },
            milestonePriorities: new Dictionary<string, string> { ["m2"] = "medium" }));
        projectRoot.WriteTask(TestData.Task("BUILD-0001", "Build task", track: "BUILD", milestone: "m1"));
        var tools = CreateTools(projectRoot);

        Assert.Equal("track_in_use", tools.RemoveTrack("BUILD").ErrorCode);
        Assert.Equal("milestone_in_use", tools.RemoveMilestone("m1").ErrorCode);
        Assert.True(tools.RemoveTrack("UI").Success);
        Assert.True(tools.RemoveMilestone("m2").Success);

        var project = tools.GetProject();
        Assert.DoesNotContain(project.Data!.Tracks, track => track.Key == "UI");
        Assert.DoesNotContain(project.Data.Milestones, milestone => milestone.Key == "m2");
    }

    [Fact]
    public void McpHostBuildsWithoutWritingToStdout()
    {
        using var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using var host = McpServerHost.CreateBuilder([]).Build();

            Assert.NotNull(host.Services.GetRequiredService<ProjectRoot>());
            var advertisedNames = host.Services.GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);
            var expectedNames = typeof(PmMcpTools)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => name != null)
                .Select(name => name!)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(advertisedNames.SetEquals(expectedNames));
            Assert.Equal(string.Empty, stdout.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void McpStartupOptionsParseNormalAndRunWorkerProfiles()
    {
        var defaultProfile = McpServerStartupOptions.Parse([]);
        var explicitNormal = McpServerStartupOptions.Parse(["--profile=normal"]);
        var runWorker = McpServerStartupOptions.Parse(
            ["--profile", "run-worker", "--task-id", "AGENT-0002"]);

        Assert.True(defaultProfile.Success);
        Assert.Equal(McpCapabilityProfile.Normal, defaultProfile.Payload!.Profile);
        Assert.Null(defaultProfile.Payload.AssignedTaskId);
        Assert.True(explicitNormal.Success);
        Assert.Equal(McpCapabilityProfile.Normal, explicitNormal.Payload!.Profile);
        Assert.True(runWorker.Success);
        Assert.Equal(McpCapabilityProfile.RunWorker, runWorker.Payload!.Profile);
        Assert.Equal("AGENT-0002", runWorker.Payload.AssignedTaskId);
    }

    [Fact]
    public void McpStartupOptionsRejectInvalidOrAmbiguousArguments()
    {
        string[][] invalidArguments =
        [
            ["--profile"],
            ["--profile", "unknown"],
            ["--profile", "run-worker"],
            ["--task-id", "AGENT-0002"],
            ["--profile", "run-worker", "--task-id", "../AGENT-0002"],
            ["--profile", "run-worker", "--task-id", "AGENT-0002", "--task-id", "AGENT-0003"],
            ["--unknown", "value"],
        ];

        foreach (var arguments in invalidArguments)
        {
            var result = McpServerStartupOptions.Parse(arguments);

            Assert.False(result.Success);
            Assert.Equal("invalid_mcp_options", result.ErrorCode);
        }
    }

    [Fact]
    public void RunWorkerProfileAdvertisesOnlyTheRestrictedToolSet()
    {
        using var host = McpServerHost.CreateBuilder(new McpServerStartupOptions(
            McpCapabilityProfile.RunWorker, "AGENT-0002")).Build();
        var tools = host.Services.GetServices<McpServerTool>();
        var names = tools.Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.True(names.SetEquals(McpToolCatalog.RunWorkerToolNames));
        Assert.Contains("get_task", names);
        Assert.Contains("append_task_note", names);
        Assert.DoesNotContain("move_task", names);
        Assert.DoesNotContain("update_task_markdown", names);
        Assert.DoesNotContain("patch_wiki_page", names);
        Assert.DoesNotContain("create_project_invitation", names);
    }

    [Fact]
    public async Task RunWorkerProfileScopesNotesToTheAssignedTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var assigned = TestData.Task("AGENT-0002", "Assigned task");
        var other = TestData.Task("AGENT-0003", "Other task");
        projectRoot.WriteTask(assigned);
        projectRoot.WriteTask(other);
        projectRoot.UpdateTaskState(assigned, "todo");
        projectRoot.UpdateTaskState(other, "todo");
        var tools = CreateTools(projectRoot,
            capabilityContext: new McpCapabilityContext(McpCapabilityProfile.RunWorker, "AGENT-0002"));

        var task = tools.GetTask("AGENT-0002");
        var allowed = tools.AppendTaskNote("AGENT-0002", "Allowed note");
        var denied = tools.AppendTaskNote("AGENT-0003", "Denied note");

        Assert.True(task.Success);
        Assert.True(allowed.Success);
        Assert.False(denied.Success);
        Assert.Equal("mcp_task_scope_denied", denied.ErrorCode);
        Assert.True(projectRoot.TryGetById("AGENT-0002", out var assignedTask));
        Assert.Contains("Allowed note", assignedTask.Description);
        Assert.True(projectRoot.TryGetById("AGENT-0003", out var otherTask));
        Assert.DoesNotContain("Denied note", otherTask.Description);
    }

    [Fact]
    public async Task RunWorkerStartupRequiresAnExistingAssignedTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        projectRoot.WriteTask(TestData.Task("AGENT-0002", "Assigned task"));

        using var validHost = McpServerHost.CreateBuilder(new McpServerStartupOptions(
            McpCapabilityProfile.RunWorker, "AGENT-0002")).Build();
        using var missingHost = McpServerHost.CreateBuilder(new McpServerStartupOptions(
            McpCapabilityProfile.RunWorker, "AGENT-9999")).Build();

        Assert.True(McpServerHost.ValidateStartup(validHost.Services).Success);
        var missing = McpServerHost.ValidateStartup(missingHost.Services);
        Assert.False(missing.Success);
        Assert.Equal("missing_task", missing.ErrorCode);
    }

    [Fact]
    public async Task MembershipToolsPreserveSecretBoundaryAndStructuredFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var membership = new RecordingMembershipService();
        var tools = CreateTools(projectRoot, membershipService: membership);

        var identity = tools.GetLocalIdentity();
        var members = await tools.ListProjectMembers();
        var invitations = await tools.ListProjectInvitations();
        var created = await tools.CreateProjectInvitation();
        var denied = await tools.UpdateProjectMemberRole("usr_2", "admin");

        Assert.True(identity.Success);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(identity.Data), StringComparison.OrdinalIgnoreCase);
        Assert.True(members.Success);
        Assert.True(invitations.Success);
        Assert.DoesNotContain("pmi_secret", JsonSerializer.Serialize(invitations.Data));
        Assert.Equal("pmi_secret", created.Data!.Token);
        Assert.False(denied.Success);
        Assert.Equal("admin_required", denied.ErrorCode);
    }

    private static PmMcpTools CreateTools(ProjectRoot projectRoot, INextIdService? nextIdService = null,
        IProjectMembershipService? membershipService = null,
        McpCapabilityContext? capabilityContext = null)
    {
        nextIdService ??= new RecordingNextIdService();
        var linkedProjects = new LinkedProjectService(projectRoot);
        var registryBasePath = projectRoot.Exists
            ? projectRoot.RepositoryPath
            : Environment.CurrentDirectory;
        var linkedProjectFamily = new LinkedProjectFamilyService(
            projectRoot,
            linkedProjects,
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
                {
                    RootPath = Path.Combine(registryBasePath, ".test-project-registry"),
                }),
                new NullSubmoduleInspector()));
        return new PmMcpTools(
            projectRoot,
            new TaskService(projectRoot, nextIdService),
            new ProjectCreationService(projectRoot, nextIdService),
            new ProjectConfigService(projectRoot),
            new BoardService(projectRoot),
            new WikiService(projectRoot),
            new ProjectValidationService(projectRoot, linkedProjects, linkedProjectFamily),
            linkedProjectFamily,
            membershipService,
            capabilityContext ?? new McpCapabilityContext(McpCapabilityProfile.Normal));
    }

    private sealed class NullSubmoduleInspector : ILinkedProjectSubmoduleInspector
    {
        public Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
            string repositoryPath,
            string pathHint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<LinkedProjectRepairAction?>.Ok(null));
    }

    private static List<string> ResolveSchemaEnumValues(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out var enumValues))
            return enumValues.EnumerateArray().Select(value => value.GetString() ?? "").ToList();

        if (schema.TryGetProperty("$ref", out var reference))
        {
            const string definitionsPrefix = "#/$defs/";
            var referenceValue = reference.GetString();
            if (referenceValue?.StartsWith(definitionsPrefix, StringComparison.Ordinal) == true &&
                root.TryGetProperty("$defs", out var definitions) &&
                definitions.TryGetProperty(referenceValue[definitionsPrefix.Length..], out var definition))
            {
                return ResolveSchemaEnumValues(root, definition);
            }
        }

        foreach (var keyword in new[] { "anyOf", "oneOf" })
        {
            if (!schema.TryGetProperty(keyword, out var alternatives))
                continue;

            foreach (var alternative in alternatives.EnumerateArray())
            {
                var values = ResolveSchemaEnumValues(root, alternative);
                if (values.Count > 0)
                    return values;
            }
        }

        return [];
    }

    private sealed class RecordingNextIdService(
        bool healthy = true,
        IReadOnlyList<int>? ids = null,
        bool failWhenIdsExhausted = false) : INextIdService
    {
        public List<string> GetNextIdTracks { get; } = [];
        public int HealthyCalls { get; private set; }
        private int _idIndex;

        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            GetNextIdTracks.Add(track);
            if (ids == null)
                return Task.FromResult(1);

            if (_idIndex < ids.Count)
                return Task.FromResult(ids[_idIndex++]);

            if (failWhenIdsExhausted)
                throw new InvalidOperationException("No more IDs.");

            return Task.FromResult(ids[^1] + 1);
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
            HealthyCalls++;
            return Task.FromResult(healthy);
        }
    }

    private sealed class RecordingMembershipService : IProjectMembershipService
    {
        private static readonly ProjectMember Member = new(
            "usr_1", "Local", "public-key", new string('a', 64), "admin", true);
        private static readonly ProjectInvitation Invitation = new(
            "pminv_1", "user", "usr_1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24));

        public AppResult<LocalIdentity> GetLocalIdentity() => AppResult<LocalIdentity>.Ok(
            new LocalIdentity("usr_1", "Local", "public-key", new string('a', 64)));

        public Task<AppResult<ProjectMembers>> ListMembers(CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<ProjectMembers>.Ok(
                new ProjectMembers("project-1", "usr_1", "admin", true, [Member])));

        public Task<AppResult<ProjectInvitations>> ListInvitations(CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<ProjectInvitations>.Ok(new ProjectInvitations([Invitation])));

        public Task<AppResult<CreatedProjectInvitation>> CreateInvitation(string role,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<CreatedProjectInvitation>.Ok(
                new CreatedProjectInvitation(Invitation, "pmi_secret")));

        public Task<AppResult<ProjectMember>> AcceptInvitation(string token,
            CancellationToken cancellationToken = default) => Task.FromResult(AppResult<ProjectMember>.Ok(Member));

        public Task<AppResult> RevokeInvitation(string invitationId,
            CancellationToken cancellationToken = default) => Task.FromResult(AppResult.Ok());

        public Task<AppResult<ProjectMember>> UpdateMemberRole(string userId, string role,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<ProjectMember>.Fail("admin_required", "Admin access required."));

        public Task<AppResult> RemoveMember(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult.Ok());
    }
}
