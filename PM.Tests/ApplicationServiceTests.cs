using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public class ApplicationServiceTests
{
    [Fact]
    public async Task ProjectCreationUsesDefaultsAndHealthChecksBeforeWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var nextIds = new RecordingNextIdService();
        var service = new ProjectCreationService(projectRoot, nextIds);

        var result = await service.CreateProject(new ProjectCreationRequest("New Project"));

        Assert.True(result.Success);
        Assert.Equal("New Project", result.Payload!.Name);
        Assert.Equal(projectRoot.RootPath, result.Payload.RootPath);
        Assert.Equal(1, nextIds.HealthyCalls);
        Assert.Equal("TASK", projectRoot.Config!.IdPrefix);
        Assert.Equal(4, projectRoot.Config.IdWidth);
        Assert.Equal(ProjectConfig.DefaultNextIdServiceUrl, projectRoot.Config.NextIdServiceUrl);
        Assert.Equal(GlobalConfig.DefaultTaskStates, projectRoot.Config.TaskStates);
        Assert.Equal("TASK", projectRoot.Config.Tracks.Single().Key);
        Assert.Empty(projectRoot.Config.Milestones);
    }

    [Fact]
    public async Task ProjectCreationRejectsExistingProjectInCurrentDirectoryOrParent()
    {
        using var workspace = new TempWorkingDirectory();
        await workspace.CreateProject();
        var child = Path.Combine(workspace.Path, "child");
        Directory.CreateDirectory(child);
        Environment.CurrentDirectory = child;
        var projectRoot = new ProjectRoot();
        var service = new ProjectCreationService(projectRoot, new RecordingNextIdService());

        var result = await service.CreateProject(new ProjectCreationRequest("Nested"));

        Assert.False(result.Success);
        Assert.Equal("project_exists", result.ErrorCode);
    }

    [Fact]
    public async Task ProjectCreationRejectsNextIdHealthFailureBeforeWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var service = new ProjectCreationService(projectRoot, new RecordingNextIdService(healthy: false));

        var result = await service.CreateProject(new ProjectCreationRequest("Offline"));

        Assert.False(result.Success);
        Assert.Equal("next_id_unavailable", result.ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, GlobalConfig.PmDirName)));
    }

    [Fact]
    public async Task ProjectCreationAcceptsCustomConfiguration()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var service = new ProjectCreationService(projectRoot, new RecordingNextIdService());

        var result = await service.CreateProject(new ProjectCreationRequest(
            "Custom",
            3,
            "BUG",
            "http://ids.local",
            new Dictionary<string, string?> { [" new "] = " New ", ["closed"] = "Closed" },
            new Dictionary<string, string?> { ["BUG"] = "Bugs", [" OPS "] = " Ops " },
            new Dictionary<string, string?> { [" v1 "] = " Version 1 " }));

        Assert.True(result.Success);
        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal(3, config.IdWidth);
        Assert.Equal("BUG", config.IdPrefix);
        Assert.Equal("http://ids.local", config.NextIdServiceUrl);
        Assert.Equal("Closed", config.TaskStates["closed"]);
        Assert.True(Directory.Exists(Path.Combine(projectRoot.StatesPath, "new")));
        Assert.Equal("Ops", config.Tracks["OPS"]);
        Assert.Equal("Version 1", config.Milestones["v1"]);
    }

    [Fact]
    public async Task ProjectCreationRejectsBlankCustomOptionEntriesBeforeWriting()
    {
        var cases = new[]
        {
            new
            {
                ErrorCode = "invalid_states",
                Request = new ProjectCreationRequest("Bad states",
                    States: new Dictionary<string, string?> { ["todo"] = "Todo", [" "] = "Missing" })
            },
            new
            {
                ErrorCode = "invalid_states",
                Request = new ProjectCreationRequest("Bad states",
                    States: new Dictionary<string, string?> { ["todo"] = " " })
            },
            new
            {
                ErrorCode = "invalid_states",
                Request = new ProjectCreationRequest("Bad states",
                    States: new Dictionary<string, string?> { ["todo"] = null })
            },
            new
            {
                ErrorCode = "invalid_tracks",
                Request = new ProjectCreationRequest("Bad tracks",
                    Tracks: new Dictionary<string, string?> { ["PM"] = "Project", [" "] = "Missing" })
            },
            new
            {
                ErrorCode = "invalid_tracks",
                Request = new ProjectCreationRequest("Bad tracks",
                    Tracks: new Dictionary<string, string?> { ["PM"] = " " })
            },
            new
            {
                ErrorCode = "invalid_tracks",
                Request = new ProjectCreationRequest("Bad tracks",
                    Tracks: new Dictionary<string, string?> { ["PM"] = null })
            },
            new
            {
                ErrorCode = "invalid_milestones",
                Request = new ProjectCreationRequest("Bad milestones",
                    Milestones: new Dictionary<string, string?> { [" "] = "Missing" })
            },
            new
            {
                ErrorCode = "invalid_milestones",
                Request = new ProjectCreationRequest("Bad milestones",
                    Milestones: new Dictionary<string, string?> { ["m1"] = " " })
            },
            new
            {
                ErrorCode = "invalid_milestones",
                Request = new ProjectCreationRequest("Bad milestones",
                    Milestones: new Dictionary<string, string?> { ["m1"] = null })
            },
        };

        foreach (var testCase in cases)
        {
            using var workspace = new TempWorkingDirectory();
            var projectRoot = new ProjectRoot();
            var nextIds = new RecordingNextIdService();
            var service = new ProjectCreationService(projectRoot, nextIds);

            var result = await service.CreateProject(testCase.Request);

            Assert.False(result.Success);
            Assert.Equal(testCase.ErrorCode, result.ErrorCode);
            Assert.Equal(0, nextIds.HealthyCalls);
            Assert.False(Directory.Exists(Path.Combine(workspace.Path, GlobalConfig.PmDirName)));
        }
    }

    [Fact]
    public async Task CreateTaskValidatesTrackMilestoneAndUsesTrackScopedId()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var nextIds = new RecordingNextIdService();
        var service = new TaskService(projectRoot, nextIds);

        var invalidTrack = await service.CreateTask("Bad", "NOPE", null, "", false);
        var invalidMilestone = await service.CreateTask("Bad", "BUILD", "missing", "", false);
        var created = await service.CreateTask("Build task", "BUILD", "m1", "Body", false);

        Assert.Equal("invalid_track", invalidTrack.ErrorCode);
        Assert.Equal("invalid_milestone", invalidMilestone.ErrorCode);
        Assert.True(created.Success);
        Assert.Equal("BUILD-0001", created.Payload!.Id);
        Assert.Equal(["BUILD"], nextIds.GetNextIdTracks);
        Assert.True(File.Exists(Path.Combine(projectRoot.TasksPath, "BUILD-0001.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "BUILD-0001.ref")));
    }

    [Fact]
    public async Task DryRunCreateReturnsPlaceholderWithoutWritingOrAllocating()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var nextIds = new RecordingNextIdService();
        var service = new TaskService(projectRoot, nextIds);

        var result = await service.CreateTask("Preview", "PM", null, "Body", true);

        Assert.True(result.Success);
        Assert.Equal("PM-????", result.Payload!.Id);
        Assert.Equal(0, nextIds.GetNextIdCalls);
        Assert.Equal(0, nextIds.HealthyCalls);
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-????.md")));
    }

    [Fact]
    public async Task BulkCreateTasksUsesOrderedTrackScopedIds()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" }));
        var nextIds = new RecordingNextIdService(ids: [1, 2, 3]);
        var service = new TaskService(projectRoot, nextIds);

        var result = await service.BulkCreateTasksForTrack("BUILD",
        [
            new BulkTaskCreateInput("First", "Body 1"),
            new BulkTaskCreateInput("Second"),
            new BulkTaskCreateInput("Third"),
        ]);

        Assert.True(result.Success);
        Assert.Null(result.Payload!.Failure);
        Assert.Equal(["BUILD-0001", "BUILD-0002", "BUILD-0003"], result.Payload.Tasks.Select(task => task.Id));
        Assert.Equal(["BUILD", "BUILD", "BUILD"], nextIds.GetNextIdTracks);
        Assert.Contains("Body 1", File.ReadAllText(Path.Combine(projectRoot.TasksPath, "BUILD-0001.md")));
    }

    [Fact]
    public async Task BulkCreateTasksValidatesBeforeAllocatingIds()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var nextIds = new RecordingNextIdService();
        var service = new TaskService(projectRoot, nextIds);

        var empty = await service.BulkCreateTasksForTrack("PM", []);
        var oversized = await service.BulkCreateTasksForTrack("PM",
            Enumerable.Range(1, 101).Select(index => new BulkTaskCreateInput($"Task {index}")).ToList());
        var invalidTitle = await service.BulkCreateTasksForTrack("PM",
            [new BulkTaskCreateInput("Good"), new BulkTaskCreateInput(" ")]);
        var invalidTrack = await service.BulkCreateTasksForTrack("NOPE", [new BulkTaskCreateInput("Good")]);

        Assert.Equal("invalid_batch_size", empty.ErrorCode);
        Assert.Equal("invalid_batch_size", oversized.ErrorCode);
        Assert.Equal("invalid_title", invalidTitle.ErrorCode);
        Assert.Equal("invalid_track", invalidTrack.ErrorCode);
        Assert.Equal(0, nextIds.GetNextIdCalls);
    }

    [Fact]
    public async Task BulkCreateTasksReportsMidBatchNextIdFailureWithoutRollback()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var nextIds = new RecordingNextIdService(ids: [1], failWhenIdsExhausted: true);
        var service = new TaskService(projectRoot, nextIds);

        var result = await service.BulkCreateTasksForTrack("PM",
            [new BulkTaskCreateInput("First"), new BulkTaskCreateInput("Second")]);

        Assert.True(result.Success);
        Assert.Equal(1, result.Payload!.CreatedCount);
        Assert.Equal("next_id_unavailable", result.Payload.Failure!.ErrorCode);
        Assert.True(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0002.md")));
    }

    [Fact]
    public async Task BulkAssignTasksToMilestoneValidatesBeforeWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var missingMilestone = service.BulkAssignTasksToMilestone("missing", ["PM-0001"]);
        var empty = service.BulkAssignTasksToMilestone("m1", []);
        var originalContent = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        var duplicateTask = service.BulkAssignTasksToMilestone("m1", ["PM-0001", " PM-0001 "]);
        var missingTask = service.BulkAssignTasksToMilestone("m1", ["PM-9999"]);

        Assert.Equal("missing_milestone", missingMilestone.ErrorCode);
        Assert.Equal("invalid_batch_size", empty.ErrorCode);
        Assert.Equal("duplicate_task_id", duplicateTask.ErrorCode);
        Assert.Equal("missing_task", missingTask.ErrorCode);
        var currentContent = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        Assert.Equal(originalContent, currentContent);
        Assert.Null(TaskItem.Parse(currentContent)!.Milestone);
    }

    [Fact]
    public async Task BulkAssignTasksToMilestoneReassignsAndPreservesStateAndDescription()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var first = TestData.Task("PM-0001", "First", "Body", milestone: "m1");
        var second = TestData.Task("PM-0002", "Second", "Already", milestone: "m2");
        projectRoot.WriteTask(first);
        projectRoot.WriteTask(second);
        projectRoot.UpdateTaskState(first, "review");
        projectRoot.UpdateTaskState(second, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var result = service.BulkAssignTasksToMilestone("m2", ["PM-0001", "PM-0002"]);

        Assert.True(result.Success);
        Assert.Equal(2, result.Payload!.RequestedCount);
        Assert.Equal(1, result.Payload.UpdatedCount);
        var updated = TaskItem.Parse(File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")))!;
        var unchanged = TaskItem.Parse(File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0002.md")))!;
        Assert.Equal("m2", updated.Milestone);
        Assert.Equal("Body", updated.Description);
        Assert.True(updated.ModifiedAt > first.ModifiedAt);
        Assert.Equal(second.ModifiedAt, unchanged.ModifiedAt);
        Assert.True(projectRoot.TryGetState(updated, out var state));
        Assert.Equal("review", state);
    }

    [Fact]
    public async Task MoveTaskValidatesMissingTaskCurrentStateAndTargetState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Move me");
        projectRoot.WriteTask(task);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        Assert.Equal("invalid_state", service.MoveTask("PM-0001", "missing").ErrorCode);
        Assert.Equal("missing_task", service.MoveTask("PM-9999", "done").ErrorCode);
        Assert.Equal("missing_current_state", service.MoveTask("PM-0001", "done").ErrorCode);

        projectRoot.UpdateTaskState(task, "todo");
        var moved = service.MoveTask("PM-0001", "done");

        Assert.True(moved.Success);
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "done", "PM-0001.ref")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task RemoveTaskDeletesMarkdownAndStateRefs()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Remove me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var missing = service.RemoveTask("PM-9999");
        var removed = service.RemoveTask("PM-0001");

        Assert.Equal("missing_task", missing.ErrorCode);
        Assert.True(removed.Success);
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task EditValidationRejectsInvalidMarkdownAndChangedId()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        Assert.Equal("invalid_edited_markdown", service.SaveEditedTaskContent("PM-0001", "not markdown").ErrorCode);
        Assert.Equal("changed_task_id",
            service.SaveEditedTaskContent("PM-0001", TestData.Task("PM-0002", "Changed").ToMarkdown()).ErrorCode);

        var updated = task with { Title = "Updated" };
        var result = service.SaveEditedTaskContent("PM-0001", updated.ToMarkdown());

        Assert.True(result.Success);
        Assert.Contains("title: Updated", File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task TrackAndMilestoneAddRejectDuplicatesAndEmptyValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new ProjectConfigService(projectRoot);

        Assert.True(service.AddTrack("BUILD", "Build").Success);
        Assert.Equal("duplicate_track", service.AddTrack("BUILD", "Duplicate").ErrorCode);
        Assert.Equal("invalid_track", service.AddTrack(" ", "Missing").ErrorCode);

        Assert.True(service.AddMilestone("m1", "Milestone 1").Success);
        Assert.Equal("duplicate_milestone", service.AddMilestone("m1", "Duplicate").ErrorCode);
        Assert.Equal("invalid_milestone", service.AddMilestone("m2", " ").ErrorCode);
    }

    [Fact]
    public async Task StatusAddCreatesConfigEntryAndStateDirectory()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new ProjectConfigService(projectRoot);

        var result = service.AddStatus("blocked", "Blocked");

        Assert.True(result.Success);
        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("Blocked", config.TaskStates["blocked"]);
        Assert.True(Directory.Exists(Path.Combine(projectRoot.StatesPath, "blocked")));
        Assert.Equal("duplicate_status", service.AddStatus("blocked", "Duplicate").ErrorCode);
        Assert.Equal("invalid_status", service.AddStatus(" ", "Missing").ErrorCode);
    }

    [Fact]
    public async Task StatusRenameWorksWhileReferencedAndKeepsStateKey()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Todo task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new ProjectConfigService(projectRoot);

        var result = service.RenameStatus("todo", "Ready");

        Assert.True(result.Success);
        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("Ready", config.TaskStates["todo"]);
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
        Assert.Equal("missing_status", service.RenameStatus("missing", "Missing").ErrorCode);
    }

    [Fact]
    public async Task StatusRemoveRejectsReferencedMissingAndLastStatuses()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Todo task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new ProjectConfigService(projectRoot);

        Assert.Equal("status_in_use", service.RemoveStatus("todo").ErrorCode);
        Assert.True(Directory.Exists(Path.Combine(projectRoot.StatesPath, "todo")));
        Assert.Equal("missing_status", service.RemoveStatus("missing").ErrorCode);
        Assert.True(service.RemoveStatus("review").Success);
        Assert.False(Directory.Exists(Path.Combine(projectRoot.StatesPath, "review")));

        var singleStatusRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project" }));
        singleStatusRoot.Config!.TaskStates = new Dictionary<string, string> { ["todo"] = "To Do" };
        singleStatusRoot.Config.WriteConfig(singleStatusRoot);
        var singleStatusService = new ProjectConfigService(singleStatusRoot);

        Assert.Equal("last_status", singleStatusService.RemoveStatus("todo").ErrorCode);
        Assert.True(Directory.Exists(Path.Combine(singleStatusRoot.StatesPath, "todo")));
    }

    [Fact]
    public async Task StatusRemoveRejectsDirectoriesWithNonTaskFilesBeforeMutatingConfig()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var statePath = Path.Combine(projectRoot.StatesPath, "review");
        File.WriteAllText(Path.Combine(statePath, ".DS_Store"), "");
        var service = new ProjectConfigService(projectRoot);

        var result = service.RemoveStatus("review");

        Assert.False(result.Success);
        Assert.Equal("status_directory_not_empty", result.ErrorCode);
        Assert.True(Directory.Exists(statePath));
        Assert.True(ProjectConfig.ReadConfig(projectRoot).TaskStates.ContainsKey("review"));
    }

    [Fact]
    public async Task TrackAndMilestoneRenameWorkWhileReferenced()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        projectRoot.WriteTask(TestData.Task("BUILD-0001", "Build task", track: "BUILD", milestone: "m1"));
        var service = new ProjectConfigService(projectRoot);

        Assert.True(service.RenameTrack("BUILD", "Build Work").Success);
        Assert.True(service.RenameMilestone("m1", "Launch").Success);

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("Build Work", config.Tracks["BUILD"]);
        Assert.Equal("Launch", config.Milestones["m1"]);
        Assert.Equal("missing_track", service.RenameTrack("missing", "Missing").ErrorCode);
        Assert.Equal("missing_milestone", service.RenameMilestone("missing", "Missing").ErrorCode);
    }

    [Fact]
    public async Task TrackAndMilestoneRemoveRejectReferencedItemsAndPersistUnusedRemovals()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build", ["UI"] = "UI" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var task = TestData.Task("BUILD-0001", "Build task", track: "BUILD", milestone: "m1");
        projectRoot.WriteTask(task);
        var service = new ProjectConfigService(projectRoot);

        Assert.Equal("track_in_use", service.RemoveTrack("BUILD").ErrorCode);
        Assert.Equal("milestone_in_use", service.RemoveMilestone("m1").ErrorCode);
        Assert.Equal("missing_track", service.RemoveTrack("NOPE").ErrorCode);
        Assert.Equal("missing_milestone", service.RemoveMilestone("missing").ErrorCode);

        Assert.True(service.RemoveTrack("UI").Success);
        Assert.True(service.RemoveMilestone("m2").Success);

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.False(config.Tracks.ContainsKey("UI"));
        Assert.False(config.Milestones.ContainsKey("m2"));
    }

    [Fact]
    public async Task RemoveTrackRejectsLastTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new ProjectConfigService(projectRoot);

        var result = service.RemoveTrack("PM");

        Assert.Equal("last_track", result.ErrorCode);
    }

    [Fact]
    public async Task BoardServiceGroupsAndFiltersForListAndWeb()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var match = TestData.Task("BUILD-0001", "Matching task", "- Preview line", "BUILD", "m1");
        var wrong = TestData.Task("PM-0001", "Other task", track: "PM", milestone: "m2");
        projectRoot.WriteTask(match);
        projectRoot.WriteTask(wrong);
        projectRoot.UpdateTaskState(match, "review");
        projectRoot.UpdateTaskState(wrong, "todo");
        var service = new BoardService(projectRoot);

        var board = service.GetBoard(new BoardQuery("BUILD", "m1", "review"),
            BoardService.CliDescriptionPreviewLength).Payload!;
        var task = Assert.Single(board.MilestoneGroups.SelectMany(group => group.States).SelectMany(state => state.Tasks));

        Assert.Equal("Matching task", task.Task.Title);
        Assert.Equal("Preview line", task.DescriptionPreview);
        Assert.Equal("Milestone 1", Assert.Single(board.MilestoneGroups).Name);
        Assert.Equal("review", Assert.Single(board.MilestoneGroups.Single().States).Key);
    }

    [Fact]
    public async Task WikiServiceCreatesReadsListsAndUpdatesNestedPages()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);

        var created = service.CreatePage("architecture/rendering", "Rendering", "# Rendering");

        Assert.True(created.Success);
        Assert.Equal("architecture/rendering", created.Payload!.Path);
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));

        var read = service.ReadPage("architecture/rendering");
        Assert.True(read.Success);
        Assert.Equal("# Rendering", read.Payload!.Body);
        Assert.Contains("title: Rendering", read.Payload.Markdown);

        service.CreatePage("getting-started", "Getting Started", "Start here");
        var list = service.ListPages();
        Assert.True(list.Success);
        Assert.Equal(["architecture/rendering", "getting-started"], list.Payload!.Select(page => page.Path));

        var folder = service.ListPagesUnder("architecture");
        Assert.True(folder.Success);
        Assert.Equal(["architecture/rendering"], folder.Payload!.Select(page => page.Path));
        Assert.Empty(service.ListPagesUnder("missing").Payload!);
        Assert.Equal("invalid_wiki_path", service.ListPagesUnder("notes.txt").ErrorCode);

        var oldModifiedAt = read.Payload.ModifiedAt;
        var updatedMarkdown = read.Payload.Markdown.Replace("title: Rendering", "title: Render Pipeline")
            .Replace("# Rendering", "# Updated");
        var updated = service.UpdatePageMarkdown("architecture/rendering", updatedMarkdown);

        Assert.True(updated.Success);
        Assert.Equal("Render Pipeline", updated.Payload!.Title);
        Assert.Equal("# Updated", updated.Payload.Body);
        Assert.True(updated.Payload.ModifiedAt > oldModifiedAt);
    }

    [Fact]
    public async Task WikiServiceReturnsStableFailuresAndDoesNotEscapeWikiRoot()
    {
        using var workspace = new TempWorkingDirectory();
        var missingProject = new WikiService(new ProjectRoot());
        Assert.Equal("missing_project", missingProject.ListPages().ErrorCode);

        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);

        Assert.Equal("invalid_wiki_path", service.CreatePage("../escape", "Escape", "").ErrorCode);
        Assert.Equal("invalid_wiki_path", service.CreatePage("notes.txt", "Notes", "").ErrorCode);
        Assert.False(File.Exists(Path.Combine(projectRoot.RootPath, "escape.md")));

        Assert.Equal("missing_wiki_page", service.ReadPage("missing").ErrorCode);
        Assert.Equal("missing_wiki_page", service.UpdatePageMarkdown("missing", "bad").ErrorCode);

        Assert.True(service.CreatePage("notes", "Notes", "").Success);
        Assert.Equal("duplicate_wiki_page", service.CreatePage("notes", "Duplicate", "").ErrorCode);
        Assert.Equal("invalid_wiki_markdown", service.UpdatePageMarkdown("notes", "not markdown").ErrorCode);
    }

    private sealed class RecordingNextIdService(
        bool healthy = true,
        IReadOnlyList<int>? ids = null,
        bool failWhenIdsExhausted = false) : INextIdService
    {
        public int GetNextIdCalls { get; private set; }
        public int HealthyCalls { get; private set; }
        public List<string> GetNextIdTracks { get; } = [];
        private int _idIndex;

        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            GetNextIdCalls++;
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
            throw new NotSupportedException();
        }

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
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
}
