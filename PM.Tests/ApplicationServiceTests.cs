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
    public async Task StructuredTaskUpdateChangesTitleStateAndBodyOnly()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing", "Old body", track: "PM", milestone: "m1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var updated = service.UpdateTaskDetails("PM-0001", "Updated", "done", "New body");

        Assert.True(updated.Success);
        Assert.Equal("Updated", updated.Payload!.Title);
        Assert.Equal("PM-0001", updated.Payload.Id);
        Assert.Equal("PM", updated.Payload.Track);
        Assert.Equal("m1", updated.Payload.Milestone);
        Assert.Equal(task.CreatedAt, updated.Payload.CreatedAt);
        Assert.Equal("New body", updated.Payload.Description);
        Assert.True(updated.Payload.ModifiedAt > task.ModifiedAt);
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "done", "PM-0001.ref")));
    }

    [Fact]
    public async Task StructuredTaskUpdateValidatesTitleStateTaskAndCurrentState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing");
        projectRoot.WriteTask(task);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        Assert.Equal("invalid_title", service.UpdateTaskDetails("PM-0001", " ", "todo", "").ErrorCode);
        Assert.Equal("invalid_state", service.UpdateTaskDetails("PM-0001", "Title", "missing", "").ErrorCode);
        Assert.Equal("missing_task", service.UpdateTaskDetails("PM-9999", "Title", "todo", "").ErrorCode);
        Assert.Equal("missing_current_state", service.UpdateTaskDetails("PM-0001", "Title", "todo", "").ErrorCode);
    }

    [Fact]
    public async Task StructuredTaskUpdateChangesPlacementAndMovesFinalOrderScope()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing", "Old body", track: "PM");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        projectRoot.SetTaskOrder(new TaskOrderScope("PM", "todo", null), [task.Id]);
        projectRoot.SetTaskOrder(new TaskOrderScope("BUILD", "done", "m1"), []);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var result = service.UpdateTaskDetails(task.Id, "Updated", "done", "New body", "urgent",
            new TaskPlacementUpdate("BUILD", "m1"));

        Assert.True(result.Success);
        var updated = result.Payload!;
        Assert.Equal(task.Id, updated.Id);
        Assert.Equal(task.CreatedAt, updated.CreatedAt);
        Assert.Equal("BUILD", updated.Track);
        Assert.Equal("m1", updated.Milestone);
        Assert.Equal("urgent", updated.Priority);
        Assert.True(updated.ModifiedAt > task.ModifiedAt);
        Assert.Empty(projectRoot.GetTaskOrder(new TaskOrderScope("PM", "todo", null)));
        Assert.Equal([task.Id], projectRoot.GetTaskOrder(new TaskOrderScope("BUILD", "done", "m1")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "done", $"{task.Id}.ref")));
    }

    [Fact]
    public async Task StructuredTaskUpdatePreservesOrUnassignsPlacementExplicitly()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing", track: "PM", milestone: "m1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var preserved = service.UpdateTaskDetails(task.Id, task.Title, "todo", task.Description);
        Assert.Equal("PM", preserved.Payload!.Track);
        Assert.Equal("m1", preserved.Payload.Milestone);

        var unassigned = service.UpdateTaskDetails(task.Id, task.Title, "todo", task.Description,
            placement: new TaskPlacementUpdate("PM", null));
        Assert.True(unassigned.Success);
        Assert.Null(unassigned.Payload!.Milestone);
    }

    [Theory]
    [InlineData("", null, "invalid_track")]
    [InlineData("missing", null, "invalid_track")]
    [InlineData("PM", " ", "invalid_milestone")]
    [InlineData("PM", "missing", "invalid_milestone")]
    public async Task StructuredTaskUpdateValidatesPlacementBeforeMutation(
        string track, string? milestone, string errorCode)
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing", "Original");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var original = File.ReadAllText(projectRoot.GetTaskFilePath(task.Id));
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var result = service.UpdateTaskDetails(task.Id, "Changed", "done", "Changed", placement:
            new TaskPlacementUpdate(track, milestone));

        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal(original, File.ReadAllText(projectRoot.GetTaskFilePath(task.Id)));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", $"{task.Id}.ref")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "done", $"{task.Id}.ref")));
    }

    [Fact]
    public async Task PatchTaskMetadataUpdatesIndependentFieldsAndMaintainsOrderScopes()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var first = TestData.Task("PM-0001", "First", track: "PM");
        var second = TestData.Task("PM-0002", "Second", track: "PM");
        projectRoot.WriteTask(first);
        projectRoot.WriteTask(second);
        projectRoot.UpdateTaskState(first, "todo");
        projectRoot.UpdateTaskState(second, "todo");
        projectRoot.SetTaskOrder(new TaskOrderScope("PM", "todo", null), ["PM-0001", "PM-0002"]);
        projectRoot.SetTaskOrder(new TaskOrderScope("BUILD", "todo", "m1"), []);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var updated = service.PatchTaskMetadata("PM-0001", title: " Updated ", track: "BUILD", milestone: "m1",
            description: "New body");

        Assert.True(updated.Success);
        Assert.True(updated.Payload!.Changed);
        Assert.Equal("Updated", updated.Payload.Task.Title);
        Assert.Equal("BUILD", updated.Payload.Task.Track);
        Assert.Equal("m1", updated.Payload.Task.Milestone);
        Assert.Equal("New body", updated.Payload.Task.Description);
        Assert.Equal(["PM-0002"], projectRoot.GetTaskOrder(new TaskOrderScope("PM", "todo", null)));
        Assert.Equal(["PM-0001"], projectRoot.GetTaskOrder(new TaskOrderScope("BUILD", "todo", "m1")));

        var unchanged = service.PatchTaskMetadata("PM-0001", title: "Updated", track: "BUILD", milestone: "m1",
            description: "New body");
        Assert.True(unchanged.Success);
        Assert.False(unchanged.Payload!.Changed);

        Assert.Equal("invalid_title", service.PatchTaskMetadata("PM-0001", title: " ").ErrorCode);
        Assert.Equal("invalid_track", service.PatchTaskMetadata("PM-0001", track: "missing").ErrorCode);
        Assert.Equal("invalid_milestone", service.PatchTaskMetadata("PM-0001", milestone: "missing").ErrorCode);
    }

    [Fact]
    public async Task PatchTaskMetadataSetsClearsAndSuppressesPriority()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "high" }));
        var task = TestData.Task("PM-0001", "Existing", milestone: "m1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var set = service.PatchTaskMetadata("PM-0001", priority: "Urgent");
        var changed = service.PatchTaskMetadata("PM-0001", priority: "low");
        var inherit = service.PatchTaskMetadata("PM-0001", priority: "inherit");
        var none = service.PatchTaskMetadata("PM-0001", priority: "none");
        var invalid = service.PatchTaskMetadata("PM-0001", priority: "later");

        Assert.True(set.Success);
        Assert.Equal("urgent", set.Payload!.Task.Priority);
        Assert.True(changed.Success);
        Assert.Equal("low", changed.Payload!.Task.Priority);
        Assert.True(inherit.Success);
        Assert.Null(inherit.Payload!.Task.Priority);
        Assert.True(none.Success);
        Assert.Equal("none", none.Payload!.Task.Priority);
        Assert.Equal("invalid_priority", invalid.ErrorCode);
        Assert.Contains("priority: none", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        var boardTask = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!.Tasks.Single();
        Assert.Equal("none", boardTask.Priority);
        Assert.Equal("task", boardTask.PrioritySource);
    }

    [Fact]
    public async Task PatchTaskMetadataSetsNormalizesClearsAndRejectsDependencies()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var first = TestData.Task("PM-0001", "Existing");
        var second = TestData.Task("PM-0002", "Dependency");
        projectRoot.WriteTask(first);
        projectRoot.WriteTask(second);
        projectRoot.UpdateTaskState(first, "todo");
        projectRoot.UpdateTaskState(second, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var set = service.PatchTaskMetadata("PM-0001", dependsOn: [" PM-0002 ", "", "PM-0002", "BUILD-0001"]);

        Assert.True(set.Success);
        Assert.Equal(["PM-0002", "BUILD-0001"], set.Payload!.Task.DependencyIds);
        Assert.Contains("dependsOn:", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        var cleared = service.PatchTaskMetadata("PM-0001", dependsOn: []);
        Assert.True(cleared.Success);
        Assert.Empty(cleared.Payload!.Task.DependencyIds);
        Assert.DoesNotContain("dependsOn:", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        var self = service.PatchTaskMetadata("PM-0001", dependsOn: ["PM-0001"]);
        Assert.Equal("invalid_dependency", self.ErrorCode);
    }

    [Fact]
    public async Task AppendTaskNoteCreatesNotesSectionAndFormatsMultilineNotes()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing", "Body");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var result = service.AppendTaskNote("PM-0001", "First line\nSecond line");

        Assert.True(result.Success);
        Assert.Contains("Body\n\n## Notes\n\n- ", result.Payload!.Task.Description);
        Assert.Contains(" UTC - First line\n  Second line", result.Payload.Task.Description);
        Assert.Equal("invalid_note", service.AppendTaskNote("PM-0001", " ").ErrorCode);
    }

    [Fact]
    public async Task ReorderTasksPersistsExactScopeAndBoardUsesStoredOrder()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var first = TestData.Task("PM-0001", "First") with { ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var second = TestData.Task("PM-0002", "Second") with { ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) };
        var third = TestData.Task("PM-0003", "Third") with { ModifiedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc) };
        foreach (var task in new[] { first, second, third })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }

        var service = new TaskService(projectRoot, new RecordingNextIdService());
        var reordered = service.ReorderTasks("PM", "todo", ["PM-0002", "PM-0001", "PM-0003"]);
        var invalidDuplicate = service.ReorderTasks("PM", "todo", ["PM-0001", "PM-0001", "PM-0003"]);
        var invalidMissing = service.ReorderTasks("PM", "todo", ["PM-0001", "PM-0002"]);
        var board = new BoardService(projectRoot).GetBoard(new BoardQuery("PM", null, "todo")).Payload!;

        Assert.True(reordered.Success);
        Assert.True(reordered.Payload!.Changed);
        Assert.Equal("invalid_task_order", invalidDuplicate.ErrorCode);
        Assert.Equal("invalid_task_order", invalidMissing.ErrorCode);
        Assert.Equal(["PM-0002", "PM-0001", "PM-0003"], board.Tasks.Select(task => task.Task.Id));
    }

    [Fact]
    public async Task TaskServiceSearchesTaskMetadataMarkdownAndDependencies()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["NEEDLE"] = "Needle Track" },
            milestones: new Dictionary<string, string> { ["mneedle"] = "Needle Milestone" }));
        var richMatch = TestData.Task("PM-0001", "Needle rich task",
            "Body has needle twice for snippet context.\nSecond needle line.",
            milestone: "mneedle",
            priority: "urgent",
            dependsOn: ["DEP-0001"]);
        var titleMatch = TestData.Task("PM-0002", "Needle title");
        var trackIdMatch = TestData.Task("NEEDLE-0001", "Track ID match", track: "NEEDLE");
        var stateMatch = TestData.Task("PM-0003", "Review state match");

        foreach (var task in new[] { richMatch, titleMatch, trackIdMatch, stateMatch })
            projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(richMatch, "todo");
        projectRoot.UpdateTaskState(titleMatch, "todo");
        projectRoot.UpdateTaskState(trackIdMatch, "todo");
        projectRoot.UpdateTaskState(stateMatch, "review");

        var service = new TaskService(projectRoot, new RecordingNextIdService());
        var needle = service.SearchTasks("needle");
        var priority = service.SearchTasks("URGENT");
        var dependencySearch = service.SearchTasks("DEP-0001");
        var state = service.SearchTasks("review");
        var frontmatter = service.SearchTasks("dependsOn:");

        Assert.True(needle.Success);
        var needleResults = needle.Payload!;
        Assert.Equal("PM-0001", needleResults.First().Task.Id);
        Assert.Equal(
            needleResults.OrderByDescending(result => result.MatchCount).ThenBy(result => result.Task.Id).Select(result => result.Task.Id),
            needleResults.Select(result => result.Task.Id));
        Assert.Contains("needle", needleResults.First().Snippet, StringComparison.OrdinalIgnoreCase);

        var priorityResult = Assert.Single(priority.Payload!);
        Assert.Equal("PM-0001", priorityResult.Task.Id);
        Assert.Equal("urgent", priorityResult.Priority);
        Assert.Equal("task", priorityResult.PrioritySource);

        var dependencyResult = Assert.Single(dependencySearch.Payload!);
        Assert.Equal("PM-0001", dependencyResult.Task.Id);
        Assert.Equal(["DEP-0001"], dependencyResult.Dependencies.DependsOn);
        Assert.False(dependencyResult.Dependencies.Ready);
        Assert.Equal(["DEP-0001"], dependencyResult.Dependencies.Missing);

        Assert.Equal("PM-0003", Assert.Single(state.Payload!).Task.Id);
        Assert.Equal("PM-0001", Assert.Single(frontmatter.Payload!).Task.Id);
    }

    [Fact]
    public async Task TaskServiceSearchClampsLimitAndReturnsStableFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new TaskService(projectRoot, new RecordingNextIdService());
        for (var index = 1; index <= 105; index++)
        {
            var task = TestData.Task($"PM-{index:0000}", $"Common task {index}");
            projectRoot.WriteTask(task);
        }

        var lowerClamp = service.SearchTasks("common", 0);
        var upperClamp = service.SearchTasks("common", 200);
        var blank = service.SearchTasks(" ");

        Assert.True(lowerClamp.Success);
        Assert.Single(lowerClamp.Payload!);
        Assert.True(upperClamp.Success);
        Assert.Equal(100, upperClamp.Payload!.Count);
        Assert.Equal("invalid_task_query", blank.ErrorCode);

        File.WriteAllText(Path.Combine(projectRoot.TasksPath, "bad.md"), "not markdown");
        var invalid = service.SearchTasks("common");

        Assert.False(invalid.Success);
        Assert.Equal("invalid_task_markdown", invalid.ErrorCode);
        Assert.Contains("bad.md", invalid.Message);
    }

    [Fact]
    public async Task TaskServiceSearchCombinesStructuredFiltersAndFreeText()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["M1"] = "First", ["M2"] = "Second" }));
        var matches = new[]
        {
            TestData.Task("BUILD-0002", "Render search", "First description", "BUILD", "M1"),
            TestData.Task("BUILD-0001", "Render search", "Second description", "BUILD", "M2"),
            TestData.Task("PM-0003", "Render search", "Wrong track", "PM", "M1"),
            TestData.Task("BUILD-0004", "Other", "Wrong text", "BUILD", "M1"),
        };
        foreach (var task in matches) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(matches[0], "todo");
        projectRoot.UpdateTaskState(matches[1], "review");
        projectRoot.UpdateTaskState(matches[2], "todo");
        projectRoot.UpdateTaskState(matches[3], "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        var compact = service.SearchTasks("render track:build milestone:M1 milestone:m2 state: todo state:review");
        var prefix = service.SearchTasks("id:build-000");
        var numeric = service.SearchTasks("id: 2");
        var context = service.SearchTasks("track:BUILD", 20, new TaskSearchContext(Milestone: "M1", State: "todo"));
        var filtersOnly = service.SearchTasks("state:todo track:build");

        Assert.Equal(["BUILD-0001", "BUILD-0002"], compact.Payload!.Select(item => item.Task.Id).Order());
        Assert.Equal(["BUILD-0001", "BUILD-0002", "BUILD-0004"], prefix.Payload!.Select(item => item.Task.Id));
        Assert.Equal("BUILD-0002", Assert.Single(numeric.Payload!).Task.Id);
        Assert.Equal(["BUILD-0002", "BUILD-0004"], context.Payload!.Select(item => item.Task.Id));
        Assert.Equal(["BUILD-0002", "BUILD-0004"], filtersOnly.Payload!.Select(item => item.Task.Id));
        Assert.All(filtersOnly.Payload!, item => Assert.Equal(item.DescriptionPreview, item.Snippet));
        Assert.All(filtersOnly.Payload!, item => Assert.Equal(0, item.MatchCount));
    }

    [Fact]
    public async Task TaskServiceSearchKeepsUnknownPrefixesAndRejectsMissingRecognizedValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Compatibility", "owner:alex");
        projectRoot.WriteTask(task);
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        Assert.Equal("PM-0001", Assert.Single(service.SearchTasks("owner:alex").Payload!).Task.Id);
        Assert.Equal("invalid_task_query", service.SearchTasks("state:").ErrorCode);
        Assert.Equal("invalid_task_query", service.SearchTasks("state: track:PM").ErrorCode);
        Assert.Equal("invalid_track", service.SearchTasks("id:PM", context: new TaskSearchContext("missing")).ErrorCode);
    }

    [Fact]
    public async Task TaskServiceSearchScopesToSelectionAndSupportsProjectWideOverride()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new() { ["PM"] = "Product", ["BUILD"] = "Build" },
            milestones: new() { ["M1"] = "First", ["M2"] = "Second" }));
        var selected = TestData.Task("BUILD-0001", "Needle selected", track: "BUILD", milestone: "M1");
        var otherMilestone = TestData.Task("BUILD-0002", "Needle other milestone", track: "BUILD", milestone: "M2");
        var otherTrack = TestData.Task("PM-0003", "Needle other track", track: "PM", milestone: "M1");
        foreach (var task in new[] { selected, otherMilestone, otherTrack }) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(selected, "todo");
        projectRoot.UpdateTaskState(otherMilestone, "review");
        projectRoot.UpdateTaskState(otherTrack, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());
        var context = new TaskSearchContext("BUILD", "M1", "todo");

        Assert.Equal("BUILD-0001", Assert.Single(service.SearchTasks("needle", context: context).Payload!).Task.Id);
        Assert.Equal("BUILD-0001", Assert.Single(service.SearchTasks("in: SELECTION", context: context).Payload!).Task.Id);
        Assert.Equal(["BUILD-0001", "PM-0003"], service.SearchTasks("in:all state:todo", context: context)
            .Payload!.Select(item => item.Task.Id));
        Assert.Empty(service.SearchTasks("in:selection track:PM", context: context).Payload!);
        Assert.Equal("PM-0003", Assert.Single(service.SearchTasks("in:all track:PM", context: context).Payload!).Task.Id);
        Assert.Equal(3, service.SearchTasks("In:ALL", context: new TaskSearchContext("missing", "missing"))
            .Payload!.Count);
    }

    [Theory]
    [InlineData("in:")]
    [InlineData("in: project")]
    [InlineData("in:all in:selection")]
    public async Task TaskServiceSearchRejectsInvalidScopePredicates(string query)
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new TaskService(projectRoot, new RecordingNextIdService());

        Assert.Equal("invalid_task_query", service.SearchTasks(query).ErrorCode);
    }

    [Fact]
    public async Task NextTaskSelectsConfiguredStateOrderBeforeNewerLaterStatesAndFiltersByTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" }));
        var todo = TestData.Task("PM-0001", "Todo", track: "PM") with
        {
            ModifiedAt = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc),
        };
        var review = TestData.Task("PM-0002", "Review", track: "PM") with
        {
            ModifiedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        };
        var build = TestData.Task("BUILD-0001", "Build", track: "BUILD") with
        {
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        foreach (var task in new[] { todo, review, build }) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(todo, "todo");
        projectRoot.UpdateTaskState(review, "review");
        projectRoot.UpdateTaskState(build, "todo");
        var service = new BoardService(projectRoot);

        var next = service.GetNextTask(new NextTaskQuery()).Payload!;
        var filtered = service.GetNextTask(new NextTaskQuery("BUILD")).Payload!;
        var invalid = service.GetNextTask(new NextTaskQuery("NOPE"));
        var invalidReadyOnly = service.GetNextTask(new NextTaskQuery("NOPE", ReadyOnly: true));

        Assert.Equal("PM-0001", next.Task!.Task.Id);
        Assert.Equal("BUILD-0001", filtered.Task!.Task.Id);
        Assert.Equal("invalid_track", invalid.ErrorCode);
        Assert.Equal("invalid_track", invalidReadyOnly.ErrorCode);
    }

    [Fact]
    public async Task NextTaskRespectsConfiguredMilestoneOrderWithUnassignedAfterConfiguredMilestones()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m2"] = "Milestone 2", ["m1"] = "Milestone 1" }));
        var unassigned = TestData.Task("PM-0001", "Unassigned") with
        {
            ModifiedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        var secondMilestone = TestData.Task("PM-0002", "Second milestone", milestone: "m1") with
        {
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        var firstMilestone = TestData.Task("PM-0003", "First milestone", milestone: "m2") with
        {
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        foreach (var task in new[] { unassigned, secondMilestone, firstMilestone })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }

        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery()).Payload!;

        Assert.Equal("PM-0003", next.Task!.Task.Id);
        Assert.Equal("m2", next.Task.Milestone);
    }

    [Fact]
    public async Task NextTaskSelectsHigherPriorityMilestoneBeforeEarlierState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["low"] = "Low priority",
                ["urgent"] = "Urgent priority",
            },
            milestonePriorities: new Dictionary<string, string>
            {
                ["low"] = "low",
                ["urgent"] = "urgent",
            }));
        var todo = TestData.Task("PM-0001", "Todo low", milestone: "low") with
        {
            ModifiedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        };
        var review = TestData.Task("PM-0002", "Review urgent", milestone: "urgent") with
        {
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        projectRoot.WriteTask(todo);
        projectRoot.WriteTask(review);
        projectRoot.UpdateTaskState(todo, "todo");
        projectRoot.UpdateTaskState(review, "review");

        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery()).Payload!;

        Assert.Equal("PM-0002", next.Task!.Task.Id);
        Assert.Equal("urgent", next.Task.Priority);
        Assert.Contains("urgent priority", next.Reason);
    }

    [Fact]
    public async Task TaskPriorityOverrideControlsEffectivePriorityAndNextTaskRanking()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string>
            {
                ["low"] = "Low priority",
                ["urgent"] = "Urgent priority",
            },
            milestonePriorities: new Dictionary<string, string>
            {
                ["low"] = "low",
                ["urgent"] = "urgent",
            }));
        var taskOverride = TestData.Task("PM-0001", "Task override", milestone: "low", priority: "high");
        var inheritedUrgent = TestData.Task("PM-0002", "Inherited urgent", milestone: "urgent", priority: "none");
        var inheritedLow = TestData.Task("PM-0003", "Inherited low", milestone: "low");
        foreach (var task in new[] { taskOverride, inheritedUrgent, inheritedLow })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }

        var board = new BoardService(projectRoot).GetBoard(new BoardQuery()).Payload!;
        var byId = board.Tasks.ToDictionary(task => task.Task.Id);
        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery()).Payload!;

        Assert.Equal("high", byId["PM-0001"].Priority);
        Assert.Equal("task", byId["PM-0001"].PrioritySource);
        Assert.Equal("none", byId["PM-0002"].Priority);
        Assert.Equal("task", byId["PM-0002"].PrioritySource);
        Assert.Equal("low", byId["PM-0003"].Priority);
        Assert.Equal("milestone", byId["PM-0003"].PrioritySource);
        Assert.Equal("PM-0001", next.Task!.Task.Id);
        Assert.Contains("task override", next.Reason);
    }

    [Fact]
    public async Task NextTaskRanksReadyTasksBeforeHigherPriorityBlockedTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var waiting = TestData.Task("PM-0001", "Urgent waiting", priority: "urgent", dependsOn: ["PM-0003"]);
        var ready = TestData.Task("PM-0002", "Ready lower priority", priority: "low");
        var dependency = TestData.Task("PM-0003", "Dependency");
        foreach (var task in new[] { waiting, ready, dependency })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }

        var service = new BoardService(projectRoot);
        var first = service.GetNextTask(new NextTaskQuery()).Payload!;
        var readyOnly = service.GetNextTask(new NextTaskQuery(ReadyOnly: true)).Payload!;
        projectRoot.UpdateTaskState(dependency, "done");
        var afterDependencyDone = service.GetNextTask(new NextTaskQuery()).Payload!;

        Assert.Equal("PM-0002", first.Task!.Task.Id);
        Assert.True(first.Task.Dependencies.Ready);
        Assert.Contains("no dependencies", first.Reason);
        Assert.Equal("PM-0002", readyOnly.Task!.Task.Id);
        Assert.True(readyOnly.Task.Dependencies.Ready);
        Assert.Equal("PM-0001", afterDependencyDone.Task!.Task.Id);
        Assert.True(afterDependencyDone.Task.Dependencies.Ready);
        Assert.Contains("all dependencies complete", afterDependencyDone.Reason);
    }

    [Fact]
    public async Task NextTaskDefaultCanReturnBlockedTaskWhenNoReadyTaskExists()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var blocked = TestData.Task("PM-0001", "Blocked", priority: "urgent", dependsOn: ["PM-9999"]);
        projectRoot.WriteTask(blocked);
        projectRoot.UpdateTaskState(blocked, "todo");

        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery()).Payload!;

        Assert.True(next.Found);
        Assert.Equal("PM-0001", next.Task!.Task.Id);
        Assert.False(next.Task.Dependencies.Ready);
        Assert.Contains("missing PM-9999", next.Reason);
    }

    [Fact]
    public async Task NextTaskReadyOnlyReturnsEmptyResultWhenAllCandidatesAreBlocked()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var blocked = TestData.Task("PM-0001", "Blocked", dependsOn: ["PM-9999"]);
        projectRoot.WriteTask(blocked);
        projectRoot.UpdateTaskState(blocked, "todo");

        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery(ReadyOnly: true)).Payload!;

        Assert.False(next.Found);
        Assert.Null(next.Task);
        Assert.Equal("No dependency-ready actionable task found.", next.Reason);
    }

    [Fact]
    public async Task NextTaskReadyOnlyRespectsTrackFilter()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" }));
        var projectReady = TestData.Task("PM-0001", "Project ready", track: "PM", priority: "urgent");
        var buildBlocked = TestData.Task("BUILD-0001", "Build blocked", track: "BUILD", dependsOn: ["BUILD-9999"]);
        projectRoot.WriteTask(projectReady);
        projectRoot.WriteTask(buildBlocked);
        projectRoot.UpdateTaskState(projectReady, "todo");
        projectRoot.UpdateTaskState(buildBlocked, "todo");

        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery("BUILD", ReadyOnly: true)).Payload!;

        Assert.False(next.Found);
        Assert.Null(next.Task);
        Assert.Equal("No dependency-ready actionable task found for track BUILD.", next.Reason);
    }

    [Fact]
    public async Task NextTaskTreatsMissingDependencyAsNotReady()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var missingDependency = TestData.Task("PM-0001", "Missing dependency", priority: "urgent",
            dependsOn: ["PM-9999"]);
        var ready = TestData.Task("PM-0002", "Ready", priority: "low");
        foreach (var task in new[] { missingDependency, ready })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }

        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery()).Payload!;

        Assert.Equal("PM-0002", next.Task!.Task.Id);
    }

    [Fact]
    public async Task NextTaskStoredTaskOrderWinsWithinSelectedStateAndMilestone()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var older = TestData.Task("PM-0001", "Older", milestone: "m1") with
        {
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var newer = TestData.Task("PM-0002", "Newer", milestone: "m1") with
        {
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        foreach (var task in new[] { older, newer })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }
        projectRoot.SetTaskOrder(new TaskOrderScope("PM", "todo", "m1"), ["PM-0001", "PM-0002"]);

        var next = new BoardService(projectRoot).GetNextTask(new NextTaskQuery()).Payload!;

        Assert.Equal("PM-0001", next.Task!.Task.Id);
    }

    [Fact]
    public async Task NextTaskUsesModifiedTimeAndIdFallbacksDeterministically()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var older = TestData.Task("PM-0001", "Older") with
        {
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var newer = TestData.Task("PM-0002", "Newer") with
        {
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        projectRoot.WriteTask(older);
        projectRoot.WriteTask(newer);
        projectRoot.UpdateTaskState(older, "todo");
        projectRoot.UpdateTaskState(newer, "todo");
        var service = new BoardService(projectRoot);

        var newest = service.GetNextTask(new NextTaskQuery()).Payload!;
        projectRoot.WriteTask(older with { ModifiedAt = newer.ModifiedAt });
        var lowestId = service.GetNextTask(new NextTaskQuery()).Payload!;

        Assert.Equal("PM-0002", newest.Task!.Task.Id);
        Assert.Equal("PM-0001", lowestId.Task!.Task.Id);
    }

    [Fact]
    public async Task NextTaskReturnsEmptyResultForEmptyOrDoneOnlyProjects()
    {
        using var emptyWorkspace = new TempWorkingDirectory();
        var emptyRoot = await emptyWorkspace.CreateProject();
        var empty = new BoardService(emptyRoot).GetNextTask(new NextTaskQuery());
        var emptyReadyOnly = new BoardService(emptyRoot).GetNextTask(new NextTaskQuery(ReadyOnly: true));

        Assert.True(empty.Success);
        Assert.False(empty.Payload!.Found);
        Assert.Null(empty.Payload.Task);
        Assert.True(emptyReadyOnly.Success);
        Assert.False(emptyReadyOnly.Payload!.Found);
        Assert.Null(emptyReadyOnly.Payload.Task);

        using var doneWorkspace = new TempWorkingDirectory();
        var doneRoot = await doneWorkspace.CreateProject();
        var doneTask = TestData.Task("PM-0001", "Done");
        doneRoot.WriteTask(doneTask);
        doneRoot.UpdateTaskState(doneTask, "done");

        var doneOnly = new BoardService(doneRoot).GetNextTask(new NextTaskQuery()).Payload!;
        var doneOnlyReadyOnly = new BoardService(doneRoot).GetNextTask(new NextTaskQuery(ReadyOnly: true)).Payload!;

        Assert.False(doneOnly.Found);
        Assert.Null(doneOnly.Task);
        Assert.False(doneOnlyReadyOnly.Found);
        Assert.Null(doneOnlyReadyOnly.Task);
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
    public async Task MilestonePriorityAddSetAndRemovePersist()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new ProjectConfigService(projectRoot);

        Assert.True(service.AddMilestone("m1", "Milestone 1", "HIGH").Success);
        Assert.Equal("high", ProjectConfig.ReadConfig(projectRoot).MilestonePriorities["m1"]);

        Assert.True(service.SetMilestonePriority("m1", "urgent").Success);
        Assert.Equal("urgent", ProjectConfig.ReadConfig(projectRoot).MilestonePriorities["m1"]);

        Assert.True(service.SetMilestonePriority("m1", "none").Success);
        Assert.False(ProjectConfig.ReadConfig(projectRoot).MilestonePriorities.ContainsKey("m1"));

        Assert.True(service.RemoveMilestone("m1").Success);
        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.False(config.Milestones.ContainsKey("m1"));
        Assert.False(config.MilestonePriorities.ContainsKey("m1"));
    }

    [Fact]
    public async Task MilestonePriorityRejectsInvalidPriorityAndMissingMilestoneBeforeWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" },
            milestonePriorities: new Dictionary<string, string> { ["m1"] = "low" }));
        var service = new ProjectConfigService(projectRoot);

        Assert.Equal("invalid_priority", service.AddMilestone("m2", "Milestone 2", "later").ErrorCode);
        Assert.Equal("invalid_priority", service.SetMilestonePriority("m1", "later").ErrorCode);
        Assert.Equal("missing_milestone", service.SetMilestonePriority("missing", "high").ErrorCode);

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.False(config.Milestones.ContainsKey("m2"));
        Assert.Equal("low", config.MilestonePriorities["m1"]);
        Assert.False(config.MilestonePriorities.ContainsKey("missing"));
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
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" },
            milestonePriorities: new Dictionary<string, string> { ["m2"] = "high" }));
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
        Assert.False(config.MilestonePriorities.ContainsKey("m2"));
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

        var bodyOnly = service.UpdatePageBody("architecture/rendering", "# Body only");
        Assert.True(bodyOnly.Success);
        Assert.Equal("Render Pipeline", bodyOnly.Payload!.Title);
        Assert.Equal(created.Payload.CreatedAt, bodyOnly.Payload.CreatedAt);
        Assert.Equal("# Body only", bodyOnly.Payload.Body);
        Assert.True(bodyOnly.Payload.ModifiedAt > updated.Payload.ModifiedAt);
    }

    [Fact]
    public async Task WikiServiceSearchesTitlePathAndBody()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        service.CreatePage("architecture/rendering", "Rendering", "Canvas rendering pipeline");
        service.CreatePage("operations/runbook", "Runbook", "Deploy checklist");

        var results = service.SearchPages("render", 10);
        var blank = service.SearchPages(" ");

        Assert.True(results.Success);
        var result = Assert.Single(results.Payload!);
        Assert.Equal("architecture/rendering", result.Path);
        Assert.True(result.MatchCount >= 2);
        Assert.Contains("render", result.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("invalid_wiki_query", blank.ErrorCode);
    }

    [Fact]
    public async Task WikiServiceOutlinesAtxHeadingsWithBreadcrumbsVersionsAndPreviews()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        service.CreatePage("guide", "Guide", """
                                             # Root
                                             Intro text.

                                             ```
                                             # Ignored
                                             const value = 1;
                                             ```

                                             ## Sub
                                             Nested text.

                                             ## Duplicate
                                             Duplicate one.

                                             ## Duplicate
                                             Duplicate two.
                                             """);

        var outline = service.OutlinePage("guide");

        Assert.True(outline.Success);
        Assert.False(string.IsNullOrWhiteSpace(outline.Payload!.Version));
        Assert.Equal("guide", outline.Payload.Path);
        Assert.Equal(["Root", "Sub", "Duplicate", "Duplicate"], outline.Payload.Headings.Select(heading => heading.Title));
        Assert.DoesNotContain(outline.Payload.Headings, heading => heading.Title == "Ignored");
        Assert.Equal("h1-root-1", outline.Payload.Headings[0].Id);
        Assert.Equal("h2-sub-1", outline.Payload.Headings[1].Id);
        Assert.Equal("h2-duplicate-1", outline.Payload.Headings[2].Id);
        Assert.Equal("h2-duplicate-2", outline.Payload.Headings[3].Id);
        Assert.Equal(["Root", "Sub"], outline.Payload.Headings[1].Breadcrumb);
        Assert.DoesNotContain("```", outline.Payload.Headings[0].Preview);
        Assert.Contains("# Ignored", outline.Payload.Headings[0].Preview);
        Assert.Contains("const value = 1;", outline.Payload.Headings[0].Preview);
        Assert.DoesNotContain("## Sub", outline.Payload.Headings[0].Preview);
        Assert.Contains("Nested text.", outline.Payload.Headings[1].Preview);
    }

    [Fact]
    public async Task WikiServicePatchOperationsMutateBodyOnlyUnderVersionAndHeadingGuards()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);

        AppResult<(WikiPageData Page, string Version)> PatchFresh(string path, string operation, string markdown)
        {
            service.CreatePage(path, "Guide", """
                                              # Guide
                                              Intro

                                              ## Target
                                              Existing body.

                                              ### Child
                                              Child body.

                                              ## Next
                                              Next body.
                                              """);
            var outline = service.OutlinePage(path).Payload!;
            return service.PatchPageSection(path, outline.Version, "h2-target-1", operation, markdown);
        }

        var appended = PatchFresh("append", "append_to_section", "Appended.");
        var prepended = PatchFresh("prepend", "prepend_to_section", "Prepended.");
        var replaced = PatchFresh("replace", "replace_section_body", "Replacement.");
        var before = PatchFresh("before", "insert_before_heading", "Inserted before.");
        var after = PatchFresh("after", "insert_after_section", "Inserted after.");

        Assert.True(appended.Success);
        Assert.Contains("Existing body.\n\nAppended.\n\n### Child", appended.Payload.Page.Body);
        Assert.DoesNotContain("Child body.\n\nAppended.", appended.Payload.Page.Body);
        Assert.NotEqual(service.OutlinePage("append").Payload!.Version, service.OutlinePage("replace").Payload!.Version);

        Assert.True(prepended.Success);
        Assert.Contains("## Target\n\nPrepended.\n\nExisting body.", prepended.Payload.Page.Body);

        Assert.True(replaced.Success);
        Assert.Contains("## Target\n\nReplacement.\n\n## Next", replaced.Payload.Page.Body);
        Assert.DoesNotContain("Existing body.", replaced.Payload.Page.Body);
        Assert.DoesNotContain("Child body.", replaced.Payload.Page.Body);

        Assert.True(before.Success);
        Assert.Contains("Inserted before.\n\n## Target", before.Payload.Page.Body);

        Assert.True(after.Success);
        Assert.Contains("Child body.\n\nInserted after.\n\n## Next", after.Payload.Page.Body);

        var original = service.ReadPage("append").Payload!;
        Assert.Equal("Guide", appended.Payload.Page.Title);
        Assert.Equal(original.CreatedAt, appended.Payload.Page.CreatedAt);
        Assert.True(appended.Payload.Page.ModifiedAt > original.CreatedAt);
    }

    [Fact]
    public async Task WikiServicePatchRejectsStaleMissingInvalidAndMalformedInputsWithoutMutation()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        service.CreatePage("guide", "Guide", """
                                             # Guide
                                             Body.
                                             """);
        var outline = service.OutlinePage("guide").Payload!;
        var originalMarkdown = File.ReadAllText(Path.Combine(projectRoot.WikiPath, "guide.md"));

        Assert.Equal("stale_wiki_page",
            service.PatchPageSection("guide", "stale", "h1-guide-1", "append_to_section", "Text").ErrorCode);
        Assert.Equal("missing_wiki_heading",
            service.PatchPageSection("guide", outline.Version, "h2-missing-1", "append_to_section", "Text").ErrorCode);
        Assert.Equal("invalid_wiki_patch_operation",
            service.PatchPageSection("guide", outline.Version, "h1-guide-1", "bad", "Text").ErrorCode);
        Assert.Equal("invalid_wiki_patch_markdown",
            service.PatchPageSection("guide", outline.Version, "h1-guide-1", "append_to_section", " ").ErrorCode);
        Assert.Equal("missing_wiki_page",
            service.PatchPageSection("missing", outline.Version, "h1-guide-1", "append_to_section", "Text").ErrorCode);
        Assert.Equal("invalid_wiki_path",
            service.PatchPageSection("../escape", outline.Version, "h1-guide-1", "append_to_section", "Text").ErrorCode);

        File.WriteAllText(Path.Combine(projectRoot.WikiPath, "bad.md"), "not markdown");
        Assert.Equal("invalid_wiki_markdown",
            service.OutlinePage("bad").ErrorCode);
        Assert.Equal("invalid_wiki_markdown",
            service.PatchPageSection("bad", "version", "h1-guide-1", "append_to_section", "Text").ErrorCode);

        Assert.Equal(originalMarkdown, File.ReadAllText(Path.Combine(projectRoot.WikiPath, "guide.md")));
    }

    [Fact]
    public async Task ProjectValidationReportsProjectHealthIssuesAsData()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var task = TestData.Task("PM-0001", "Existing", track: "missing", milestone: "missing");
        projectRoot.WriteTask(task);
        File.WriteAllText(Path.Combine(projectRoot.TasksPath, "copy.md"), task.ToMarkdown());
        File.WriteAllText(Path.Combine(projectRoot.TasksPath, "bad.md"), "not markdown");
        File.WriteAllText(Path.Combine(projectRoot.TasksPath, "bad-priority.md"),
            TestData.Task("PM-0002", "Bad priority", priority: "later").ToMarkdown());
        Directory.CreateDirectory(Path.Combine(projectRoot.StatesPath, "unknown"));
        File.WriteAllText(Path.Combine(projectRoot.StatesPath, "unknown", "PM-0001.ref"), "../../tasks/PM-0001.md");
        File.WriteAllText(Path.Combine(projectRoot.StatesPath, "todo", "PM-9999.ref"), "../../tasks/PM-9999.md");
        File.WriteAllText(Path.Combine(projectRoot.WikiPath, "bad.md"), "not markdown");
        projectRoot.SetTaskOrder(new TaskOrderScope("PM", "todo", null), ["PM-9999"]);
        projectRoot.Config!.MilestonePriorities["m1"] = "later";
        projectRoot.Config.MilestonePriorities["missing"] = "urgent";
        projectRoot.Config.WriteConfig(projectRoot);
        var service = new ProjectValidationService(projectRoot);

        var result = service.ValidateProject();

        Assert.True(result.Success);
        Assert.False(result.Payload!.Valid);
        var codes = result.Payload.Issues.Select(issue => issue.Code).ToHashSet();
        Assert.Contains("invalid_task_markdown", codes);
        Assert.Contains("invalid_task_priority", codes);
        Assert.Contains("duplicate_task_id", codes);
        Assert.Contains("task_filename_mismatch", codes);
        Assert.Contains("missing_current_state", codes);
        Assert.Contains("unknown_state_directory", codes);
        Assert.Contains("broken_ref_target", codes);
        Assert.Contains("unknown_task_track", codes);
        Assert.Contains("unknown_task_milestone", codes);
        Assert.Contains("invalid_wiki_markdown", codes);
        Assert.Contains("stale_task_order_task", codes);
        Assert.Contains("invalid_milestone_priority", codes);
        Assert.Contains("unknown_milestone_priority", codes);
    }

    [Fact]
    public async Task ProjectValidationReportsDependencyIssues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var self = TestData.Task("PM-0001", "Self", dependsOn: ["PM-0001"]);
        var missing = TestData.Task("PM-0002", "Missing", dependsOn: ["PM-9999"]);
        var cycleA = TestData.Task("PM-0003", "Cycle A", dependsOn: ["PM-0004"]);
        var cycleB = TestData.Task("PM-0004", "Cycle B", dependsOn: ["PM-0003"]);
        foreach (var task in new[] { self, missing, cycleA, cycleB })
        {
            projectRoot.WriteTask(task);
            projectRoot.UpdateTaskState(task, "todo");
        }

        var result = new ProjectValidationService(projectRoot).ValidateProject();

        Assert.True(result.Success);
        Assert.False(result.Payload!.Valid);
        var codes = result.Payload.Issues.Select(issue => issue.Code).ToList();
        Assert.Contains("self_dependency", codes);
        Assert.Contains("missing_dependency", codes);
        Assert.Contains("dependency_cycle", codes);
    }

    [Fact]
    public async Task WikiServiceRenamesPathTitleOrBothAndPreservesPageContent()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        var created = service.CreatePage("architecture/rendering", "Rendering", "# Rendering");
        Assert.True(created.Success);

        var titleOnly = service.RenamePage("architecture/rendering", "architecture/rendering", "Render Pipeline");
        Assert.True(titleOnly.Success);
        Assert.Equal("architecture/rendering", titleOnly.Payload!.Path);
        Assert.Equal("Render Pipeline", titleOnly.Payload.Title);
        Assert.Equal("# Rendering", titleOnly.Payload.Body);
        Assert.Equal(created.Payload!.CreatedAt, titleOnly.Payload.CreatedAt);
        Assert.True(titleOnly.Payload.ModifiedAt > created.Payload.ModifiedAt);

        var pathOnly = service.RenamePage("architecture/rendering", "architecture/pipeline", "Render Pipeline");
        Assert.True(pathOnly.Success);
        Assert.Equal("architecture/pipeline", pathOnly.Payload!.Path);
        Assert.Equal("Render Pipeline", pathOnly.Payload.Title);
        Assert.Equal("# Rendering", pathOnly.Payload.Body);
        Assert.Equal(created.Payload.CreatedAt, pathOnly.Payload.CreatedAt);
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "pipeline.md")));

        var both = service.RenamePage("architecture/pipeline", "reference/rendering", "Rendering Reference");
        Assert.True(both.Success);
        Assert.Equal("reference/rendering", both.Payload!.Path);
        Assert.Equal("Rendering Reference", both.Payload.Title);
        Assert.Equal("# Rendering", both.Payload.Body);
        Assert.Equal(created.Payload.CreatedAt, both.Payload.CreatedAt);
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "reference", "rendering.md")));
        Assert.False(Directory.Exists(Path.Combine(projectRoot.WikiPath, "architecture")));
    }

    [Fact]
    public async Task WikiServiceRenameAndRemoveReturnStableFailuresAndCleanEmptyDirectories()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);

        Assert.Equal("missing_wiki_page", service.RenamePage("missing", "renamed", "Renamed").ErrorCode);
        Assert.Equal("invalid_wiki_path", service.RenamePage("../escape", "renamed", "Renamed").ErrorCode);
        Assert.Equal("invalid_wiki_path", service.RenamePage("missing", "notes.txt", "Renamed").ErrorCode);
        Assert.Equal("invalid_wiki_page", service.RenamePage("missing", "renamed", "").ErrorCode);

        Assert.True(service.CreatePage("docs/keep", "Keep", "").Success);
        Assert.True(service.CreatePage("docs/nested/remove-me", "Remove Me", "").Success);
        Assert.True(service.CreatePage("target", "Target", "").Success);
        Assert.Equal("duplicate_wiki_page", service.RenamePage("docs/keep", "target", "Target").ErrorCode);

        var removed = service.RemovePage("docs/nested/remove-me");
        Assert.True(removed.Success);
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "docs", "nested", "remove-me.md")));
        Assert.False(Directory.Exists(Path.Combine(projectRoot.WikiPath, "docs", "nested")));
        Assert.True(Directory.Exists(Path.Combine(projectRoot.WikiPath, "docs")));
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "docs", "keep.md")));
        Assert.True(Directory.Exists(projectRoot.WikiPath));

        Assert.Equal("missing_wiki_page", service.RemovePage("missing").ErrorCode);
        Assert.Equal("invalid_wiki_path", service.RemovePage("../escape").ErrorCode);
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
