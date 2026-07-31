using CodePunk.Highlight.Core.SyntaxHighlighting;
using CodePunk.Highlight.Core.SyntaxHighlighting.Languages;
using PM.Application;
using PM.Project;
using PM.Tasks;
using PM.Wiki;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tests;

public class CommandBehaviorTests
{
    [Fact]
    public async Task ListOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, _) = await ExecuteListCommand(command, new ListCommand.Settings());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DoctorOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var command = new DoctorCommand(new ProjectValidationService(projectRoot));

        var (exitCode, output) = await CaptureConsole(() =>
            command.Execute(null!, new DoctorCommand.Settings(), CancellationToken.None));

        Assert.Equal(1, exitCode);
        Assert.Contains("Project not found. Run pm init first.", output);
    }

    [Fact]
    public async Task DoctorValidProjectReturnsZero()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var command = new DoctorCommand(new ProjectValidationService(projectRoot));

        var (exitCode, output) = await CaptureConsole(() =>
            command.Execute(null!, new DoctorCommand.Settings(), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("Project validation passed.", output);
    }

    [Fact]
    public async Task DoctorReturnsZeroAndPrintsLinkedProjectWarnings()
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
        var linkedProjects = new LinkedProjectService(projectRoot);
        var family = new LinkedProjectFamilyService(
            projectRoot,
            linkedProjects,
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
                {
                    RootPath = Path.Combine(workspace.Path, "registry"),
                }),
                new EmptyLinkedProjectSubmoduleInspector()));
        var command = new DoctorCommand(new ProjectValidationService(projectRoot, linkedProjects, family));

        var (exitCode, output) = await CaptureConsole(() =>
            command.Execute(null!, new DoctorCommand.Settings(), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("Project validation passed with 2 warning(s).", output);
        Assert.Contains("warning missing_project_path", output);
        Assert.Contains("warning linked_project_missing", output);
        Assert.Contains("project prj_missing", output);
        Assert.Contains("alias missing", output);
    }

    [Fact]
    public async Task ProjectLinksPrintsPartialFamilyAndNamedWarnings()
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
        var command = new ProjectLinksCommand(new LinkedProjectFamilyService(
            projectRoot,
            new LinkedProjectService(projectRoot),
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions
                {
                    RootPath = Path.Combine(workspace.Path, "registry"),
                }),
                new EmptyLinkedProjectSubmoduleInspector())));

        var (exitCode, output) = await CaptureConsole(() =>
            command.ExecuteAsync(null!, new ProjectLinksCommand.Settings(), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("current", output);
        Assert.Contains("prj_active", output);
        Assert.Contains("prj_missing", output);
        Assert.Contains("linked_project_missing", output);
    }

    [Fact]
    public async Task DoctorInvalidProjectReturnsOneAndPrintsIssueContext()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project" }));
        var task = TestData.Task("PM-0001", "Broken task", track: "missing<tr>");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var command = new DoctorCommand(new ProjectValidationService(projectRoot));

        var (exitCode, output) = await CaptureConsole(() =>
            command.Execute(null!, new DoctorCommand.Settings(), CancellationToken.None));

        Assert.Equal(1, exitCode);
        Assert.Contains("Project validation found 1 issue(s).", output);
        Assert.Contains("error unknown_task_track: Task PM-0001 references unknown track missing<tr>.", output);
        Assert.Contains("task PM-0001", output);
        Assert.Contains("path ", output);
    }

    [Fact]
    public async Task ListEmptyStatesRendersWithoutCrashing()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, output) = await ExecuteListCommand(command, new ListCommand.Settings());

        Assert.Equal(0, exitCode);
        Assert.Contains("Queued", output);
        Assert.Contains("Review", output);
        Assert.Contains("Done", output);
    }

    [Fact]
    public async Task ListSortsTasksByModifiedDescending()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var older = TestData.Task("PM-0001", "Older task") with
        {
            ModifiedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        var newer = TestData.Task("PM-0002", "Newer task") with
        {
            ModifiedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        projectRoot.WriteTask(older);
        projectRoot.WriteTask(newer);
        projectRoot.UpdateTaskState(older, "todo");
        projectRoot.UpdateTaskState(newer, "todo");
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, output) = await ExecuteListCommand(command, new ListCommand.Settings());

        Assert.Equal(0, exitCode);
        Assert.True(output.IndexOf("Newer task", StringComparison.Ordinal) <
                    output.IndexOf("Older task", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListStateOnlyRendersMatchingState()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var todo = TestData.Task("PM-0001", "Todo task");
        var review = TestData.Task("PM-0002", "Review task");
        projectRoot.WriteTask(todo);
        projectRoot.WriteTask(review);
        projectRoot.UpdateTaskState(todo, "todo");
        projectRoot.UpdateTaskState(review, "review");
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, output) = await ExecuteListCommand(command, new ListCommand.Settings { State = "todo" });

        Assert.Equal(0, exitCode);
        Assert.Contains("Todo task", output);
        Assert.DoesNotContain("Review task", output);
        Assert.Contains("Queued", output);
        Assert.DoesNotContain("Review (", output);
    }

    [Fact]
    public async Task ListDescriptionPreviewUsesFirstNonEmptyBodyLineAndTruncates()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task(
            "PM-0001",
            "Preview task",
            """

            - This preview is intentionally longer than terminal friendly output.

            Second line should not render.
            """);
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, output) = await ExecuteListCommand(command, new ListCommand.Settings { State = "todo" });

        Assert.Equal(0, exitCode);
        Assert.Contains("This preview is intentionally longer than ter...", output);
        Assert.DoesNotContain("- This preview", output);
        Assert.DoesNotContain("Second line", output);
    }

    [Fact]
    public async Task ListGroupsTasksByMilestoneThenStateAndShowsTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            idPrefix: "PM",
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var assigned = TestData.Task("BUILD-0001", "Assigned build task", track: "BUILD", milestone: "m1");
        var unassigned = TestData.Task("PM-0001", "Unassigned task");
        projectRoot.WriteTask(assigned);
        projectRoot.WriteTask(unassigned);
        projectRoot.UpdateTaskState(assigned, "review");
        projectRoot.UpdateTaskState(unassigned, "todo");
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, output) = await ExecuteListCommand(command, new ListCommand.Settings());

        Assert.Equal(0, exitCode);
        Assert.Contains("Milestone 1", output);
        Assert.Contains("Unassigned", output);
        Assert.Contains("BUILD", output);
        Assert.True(output.IndexOf("Milestone 1", StringComparison.Ordinal) <
                    output.IndexOf("Assigned build task", StringComparison.Ordinal));
        Assert.True(output.IndexOf("Unassigned", StringComparison.Ordinal) <
                    output.IndexOf("Unassigned task", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListFiltersTrackMilestoneAndStateTogether()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" }));
        var match = TestData.Task("BUILD-0001", "Matching task", track: "BUILD", milestone: "m1");
        var wrongTrack = TestData.Task("PM-0001", "Wrong track", track: "PM", milestone: "m1");
        var wrongMilestone = TestData.Task("BUILD-0002", "Wrong milestone", track: "BUILD", milestone: "m2");
        var wrongState = TestData.Task("BUILD-0003", "Wrong state", track: "BUILD", milestone: "m1");
        foreach (var task in new[] { match, wrongTrack, wrongMilestone, wrongState }) projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(match, "review");
        projectRoot.UpdateTaskState(wrongTrack, "review");
        projectRoot.UpdateTaskState(wrongMilestone, "review");
        projectRoot.UpdateTaskState(wrongState, "todo");
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, output) = await ExecuteListCommand(command,
            new ListCommand.Settings { Track = "BUILD", Milestone = "m1", State = "review" });

        Assert.Equal(0, exitCode);
        Assert.Contains("Matching task", output);
        Assert.DoesNotContain("Wrong track", output);
        Assert.DoesNotContain("Wrong milestone", output);
        Assert.DoesNotContain("Wrong state", output);
    }

    [Fact]
    public async Task DryRunAddWithNoProjectIdFileUsesPlaceholderAndDoesNotRegisterProject()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var nextIdService = new RecordingNextIdService();
        var highlighter = new SyntaxHighlighter([
            new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
        ]);
        var command = new TaskAddCommand(projectRoot, new TaskService(projectRoot, nextIdService), highlighter,
            new RecordingEditorService());
        GlobalConfig.DryRun = true;

        try
        {
            var exitCode = await command.ExecuteAsync(null!,
                new TaskAddCommand.Settings { DryRun = true, Title = "Preview task" }, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, nextIdService.GetNextIdCalls);
            Assert.Equal(1, nextIdService.PeekExistingNextIdCalls);
            Assert.Equal(0, nextIdService.HealthyCalls);
            Assert.False(File.Exists(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile)));
            Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-????.md")));
        }
        finally
        {
            GlobalConfig.DryRun = false;
        }
    }

    [Fact]
    public async Task AddWithDescriptionWritesMarkdownBody()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var nextIdService = new RecordingNextIdService();
        var command = new TaskAddCommand(projectRoot, new TaskService(projectRoot, nextIdService), CreateHighlighter(),
            new RecordingEditorService());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskAddCommand.Settings
            {
                Title = "Document task",
                Description = "# Context\n\nDetails here.",
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        var content = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        Assert.EndsWith("---\n\n# Context\n\nDetails here.", content);
    }

    [Fact]
    public async Task AddWritesTrackAndOptionalMilestone()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var nextIdService = new RecordingNextIdService();
        var command = new TaskAddCommand(projectRoot, new TaskService(projectRoot, nextIdService), CreateHighlighter(),
            new RecordingEditorService());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskAddCommand.Settings
            {
                Title = "Build task",
                Track = "BUILD",
                Milestone = "m1",
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["BUILD"], nextIdService.GetNextIdTracks);
        var content = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "BUILD-0001.md"));
        Assert.Contains("track: BUILD", content);
        Assert.Contains("milestone: m1", content);
    }

    [Fact]
    public async Task AddInvalidTrackExitsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var nextIdService = new RecordingNextIdService();
        var command = new TaskAddCommand(projectRoot, new TaskService(projectRoot, nextIdService), CreateHighlighter(),
            new RecordingEditorService());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskAddCommand.Settings { Title = "Bad track", Track = "NOPE" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task AddInvalidMilestoneExitsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var nextIdService = new RecordingNextIdService();
        var command = new TaskAddCommand(projectRoot, new TaskService(projectRoot, nextIdService), CreateHighlighter(),
            new RecordingEditorService());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskAddCommand.Settings { Title = "Bad milestone", Milestone = "missing" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DryRunAddWithDescriptionDoesNotWriteFiles()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var nextIdService = new RecordingNextIdService();
        var command = new TaskAddCommand(projectRoot, new TaskService(projectRoot, nextIdService), CreateHighlighter(),
            new RecordingEditorService());
        GlobalConfig.DryRun = true;

        try
        {
            var exitCode = await command.ExecuteAsync(null!,
                new TaskAddCommand.Settings
                {
                    DryRun = true,
                    Title = "Preview task",
                    Description = "Preview body",
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-????.md")));
        }
        finally
        {
            GlobalConfig.DryRun = false;
        }
    }

    [Fact]
    public async Task EditorFailureExitsOneAndDoesNotCreateTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var nextIdService = new RecordingNextIdService();
        var editor = new RecordingEditorService { ExitCode = 1 };
        var command = new TaskAddCommand(projectRoot, new TaskService(projectRoot, nextIdService), CreateHighlighter(),
            editor);

        var exitCode = await command.ExecuteAsync(null!,
            new TaskAddCommand.Settings
            {
                Title = "Edit body",
                Description = "Draft body",
                Edit = true,
            },
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, nextIdService.GetNextIdCalls);
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task TaskNoteCommandAppendsInlineAndEditedNotes()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Task", "Body");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var editor = new RecordingEditorService
        {
            EditAction = path => File.AppendAllText(path, "\nSecond line"),
        };
        var command = new TaskNoteCommand(new TaskService(projectRoot, new RecordingNextIdService()), editor);

        var inline = await command.ExecuteAsync(null!,
            new TaskNoteCommand.Settings { TaskId = task.Id, Note = "Inline note" }, CancellationToken.None);
        var edited = await command.ExecuteAsync(null!,
            new TaskNoteCommand.Settings { TaskId = task.Id, Note = "Edited note", Edit = true },
            CancellationToken.None);

        Assert.Equal(0, inline);
        Assert.Equal(0, edited);
        Assert.Equal(1, editor.EditCalls);
        var content = File.ReadAllText(projectRoot.GetTaskFilePath(task.Id));
        Assert.Contains("Inline note", content);
        Assert.Contains("Edited note\n  Second line", content);
    }

    [Fact]
    public async Task TaskNoteCommandUsesEditorWhenTextIsOmittedAndRejectsFailuresAndEmptyNotes()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Task", "Body");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var service = new TaskService(projectRoot, new RecordingNextIdService());
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, "Editor note"),
        };

        Assert.Equal(0, await new TaskNoteCommand(service, editor).ExecuteAsync(null!,
            new TaskNoteCommand.Settings { TaskId = task.Id }, CancellationToken.None));
        Assert.Equal(1, await new TaskNoteCommand(service, new RecordingEditorService { ExitCode = 1 })
            .ExecuteAsync(null!, new TaskNoteCommand.Settings { TaskId = task.Id }, CancellationToken.None));
        Assert.Equal(1, await new TaskNoteCommand(service, new RecordingEditorService())
            .ExecuteAsync(null!, new TaskNoteCommand.Settings { TaskId = task.Id }, CancellationToken.None));
        Assert.Equal(1, await new TaskNoteCommand(service, editor).ExecuteAsync(null!,
            new TaskNoteCommand.Settings { TaskId = task.Id, Note = " " }, CancellationToken.None));
        Assert.Contains("Editor note", File.ReadAllText(projectRoot.GetTaskFilePath(task.Id)));
    }

    [Fact]
    public async Task TaskNextCommandDefaultsToReadyTasksAndSupportsScopesAndBlockedFallback()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "First" }));
        var ready = TestData.Task("PM-0001", "Ready task", track: "PM");
        var blocked = TestData.Task("BUILD-0001", "Blocked task", track: "BUILD", milestone: "m1",
            dependsOn: ["BUILD-9999"]);
        projectRoot.WriteTask(ready);
        projectRoot.WriteTask(blocked);
        projectRoot.UpdateTaskState(ready, "todo");
        projectRoot.UpdateTaskState(blocked, "todo");
        var command = new TaskNextCommand(new BoardService(projectRoot));

        var (readyExit, readyOutput) = await CaptureConsole(() =>
            command.Execute(null!, new TaskNextCommand.Settings(), CancellationToken.None));
        var (emptyExit, emptyOutput) = await CaptureConsole(() => command.Execute(null!,
            new TaskNextCommand.Settings { Track = "BUILD", Milestone = "m1" }, CancellationToken.None));
        var (blockedExit, blockedOutput) = await CaptureConsole(() => command.Execute(null!,
            new TaskNextCommand.Settings { Track = "BUILD", Milestone = "m1", IncludeBlocked = true },
            CancellationToken.None));

        Assert.Equal(0, readyExit);
        Assert.Contains("PM-0001", readyOutput);
        Assert.Contains("Selected", readyOutput);
        Assert.Equal(0, emptyExit);
        Assert.Contains("No dependency-ready actionable task found for track BUILD and milestone m1", emptyOutput);
        Assert.Equal(0, blockedExit);
        Assert.Contains("BUILD-0001", blockedOutput);
        Assert.Contains("missing BUILD-9999", blockedOutput);
    }

    [Fact]
    public async Task TaskNextCommandRejectsInvalidScope()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var command = new TaskNextCommand(new BoardService(projectRoot));

        var (exitCode, output) = await CaptureConsole(() => command.Execute(null!,
            new TaskNextCommand.Settings { Milestone = "missing" }, CancellationToken.None));

        Assert.Equal(1, exitCode);
        Assert.Contains("Milestone missing not found", output);
    }

    [Fact]
    public async Task EditOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()),
            new RecordingEditorService(), CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task EditMissingTaskReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()),
            new RecordingEditorService(), CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-9999" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task DryRunEditRendersExistingTaskAndDoesNotWrite()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task("PM-0001", "Existing task", "Original body");
        projectRoot.WriteTask(task);
        var originalContent = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, "changed"),
        };
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()), editor,
            CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { DryRun = true, TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, editor.EditCalls);
        Assert.Equal(originalContent, File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task EditEditorFailureExitsOneAndDoesNotWrite()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task("PM-0001", "Existing task", "Original body");
        projectRoot.WriteTask(task);
        var originalContent = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        var editor = new RecordingEditorService
        {
            ExitCode = 1,
            EditAction = path => File.WriteAllText(path, "changed"),
        };
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()), editor,
            CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(originalContent, File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task EditInvalidMarkdownExitsOneAndDoesNotWrite()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task("PM-0001", "Existing task", "Original body");
        projectRoot.WriteTask(task);
        var originalContent = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, "not frontmatter"),
        };
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()), editor,
            CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(originalContent, File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task EditChangedIdExitsOneAndKeepsOriginalFileAndRef()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task("PM-0001", "Existing task", "Original body");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var originalContent = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, TestData.Task("PM-0002", "Changed ID").ToMarkdown()),
        };
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()), editor,
            CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(originalContent, File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0002.md")));
    }

    [Fact]
    public async Task EditAddingStateMetadataExitsOneAndDoesNotWrite()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task("PM-0001", "Existing task", "Original body");
        projectRoot.WriteTask(task);
        var originalContent = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, """
                                                         ---
                                                         id: PM-0001
                                                         title: Existing task
                                                         createdAt: 2026-01-01T00:00:00.0000000Z
                                                         modifiedAt: 2026-01-01T00:00:00.0000000Z
                                                         state: done
                                                         ---

                                                         Original body
                                                         """),
        };
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()), editor,
            CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(originalContent, File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
    }

    [Fact]
    public async Task EditValidMarkdownWritesUpdatedTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task("PM-0001", "Existing task", "Original body");
        projectRoot.WriteTask(task);
        var edited = task with
        {
            Title = "Updated task",
            ModifiedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Description = "# Updated\n\nBody",
        };
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, edited.ToMarkdown()),
        };
        var command = new TaskEditCommand(new TaskService(projectRoot, new RecordingNextIdService()), editor,
            CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var content = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        Assert.Contains("title: Updated task", content);
        Assert.Contains("modifiedAt: 2026-02-01T00:00:00.0000000Z", content);
        Assert.EndsWith("---\n\n# Updated\n\nBody", content);
    }

    [Fact]
    public async Task TaskMetadataCommandSetsClearsAndRejectsPriority()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Existing task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var command = new TaskMetadataCommand(new TaskService(projectRoot, new RecordingNextIdService()));

        Assert.Equal(0,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", Priority = "High" },
                CancellationToken.None));
        Assert.Contains("priority: high", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        Assert.Equal(0,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", Priority = "inherit" },
                CancellationToken.None));
        Assert.DoesNotContain("priority:", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        Assert.Equal(1,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", Priority = "later" },
                CancellationToken.None));
    }

    [Fact]
    public async Task TaskMetadataCommandSetsClearsAndRejectsDependencies()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" }));
        var task = TestData.Task("PM-0001", "Existing task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var command = new TaskMetadataCommand(new TaskService(projectRoot, new RecordingNextIdService()));

        Assert.Equal(0,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", DependsOn = "PM-0002, BUILD-0002,PM-0002" },
                CancellationToken.None));
        var content = File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001"));
        Assert.Contains("dependsOn:", content);
        Assert.Contains("- PM-0002", content);
        Assert.Contains("- BUILD-0002", content);

        const string qualified = "pm://project/prj_other/task/OTHER-0001";
        Assert.Equal(0,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", DependsOn = qualified },
                CancellationToken.None));
        Assert.Contains($"- {qualified}", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        Assert.Equal(0,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", DependsOn = "" },
                CancellationToken.None));
        Assert.DoesNotContain("dependsOn:", File.ReadAllText(projectRoot.GetTaskFilePath("PM-0001")));

        Assert.Equal(1,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", DependsOn = "PM-0001" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!,
                new TaskMetadataCommand.Settings { TaskId = "PM-0001", DependsOn = "pm:not-a-reference" },
                CancellationToken.None));
    }

    [Fact]
    public async Task WikiListEmptyAndPopulatedOutput()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        var command = new WikiListCommand(service, CreateLinkedReads(projectRoot));

        var (emptyExitCode, emptyOutput) = await CaptureConsole(() =>
            command.ExecuteAsync(null!, new WikiListCommand.Settings(), CancellationToken.None));

        Assert.Equal(0, emptyExitCode);
        Assert.Contains("No wiki pages.", emptyOutput);

        service.CreatePage("architecture/rendering", "Rendering", "# Rendering");
        service.CreatePage("getting-started", "Getting Started", "Start here");

        var (exitCode, output) = await CaptureConsole(() =>
            command.ExecuteAsync(null!, new WikiListCommand.Settings(), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("architecture/rendering", output);
        Assert.Contains("Rendering", output);
        Assert.Contains("getting-started", output);
        Assert.Contains("Getting Started", output);
    }

    [Fact]
    public async Task WikiListOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var command = new WikiListCommand(new WikiService(projectRoot), CreateLinkedReads(projectRoot));

        var (exitCode, output) = await CaptureConsole(() =>
            command.ExecuteAsync(null!, new WikiListCommand.Settings(), CancellationToken.None));

        Assert.Equal(1, exitCode);
        Assert.Contains("Project not found", output);
    }

    [Fact]
    public async Task WikiSearchRendersNestedTitlePathAndBodyMatchesWithMetadataAndLimit()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        service.CreatePage("nested/body-hit", "Body page", "Needle appears twice: needle.");
        service.CreatePage("nested/needle-path", "Path page", "No body match.");
        service.CreatePage("nested/title-hit", "Needle title", "No body match.");
        var command = new WikiSearchCommand(service, CreateLinkedReads(projectRoot));

        var all = await CaptureConsole(() => command.ExecuteAsync(null!,
            new WikiSearchCommand.Settings { Query = "needle", Limit = 3 }, CancellationToken.None));
        var limited = await CaptureConsole(() => command.ExecuteAsync(null!,
            new WikiSearchCommand.Settings { Query = "needle", Limit = 2 }, CancellationToken.None));

        Assert.Equal(0, all.ExitCode);
        Assert.Contains("nested/body-hit", all.Output);
        Assert.Contains("nested/needle-path", all.Output);
        Assert.Contains("nested/title-hit", all.Output);
        Assert.Contains("Needle appears twice: needle.", all.Output);
        Assert.Contains("Modified", all.Output);
        Assert.Contains("Matches", all.Output);
        Assert.Equal(0, limited.ExitCode);
        Assert.Contains("nested/body-hit", limited.Output);
        Assert.Contains("nested/needle-path", limited.Output);
        Assert.DoesNotContain("nested/title-hit", limited.Output);
    }

    [Fact]
    public async Task WikiSearchHandlesNoMatchesValidationInvalidMarkdownAndMissingProject()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var command = new WikiSearchCommand(new WikiService(projectRoot), CreateLinkedReads(projectRoot));

        var empty = await CaptureConsole(() => command.ExecuteAsync(null!,
            new WikiSearchCommand.Settings { Query = "missing" }, CancellationToken.None));
        var blank = await CaptureConsole(() => command.ExecuteAsync(null!,
            new WikiSearchCommand.Settings { Query = " " }, CancellationToken.None));

        Assert.Equal(0, empty.ExitCode);
        Assert.Contains("No matching wiki pages.", empty.Output);
        Assert.Equal(1, blank.ExitCode);
        Assert.Contains("Wiki search query is required.", blank.Output);

        File.WriteAllText(Path.Combine(projectRoot.WikiPath, "broken.md"), "not front matter");
        var invalid = await CaptureConsole(() => command.ExecuteAsync(null!,
            new WikiSearchCommand.Settings { Query = "needle" }, CancellationToken.None));

        Assert.Equal(1, invalid.ExitCode);
        Assert.Contains("Wiki page broken markdown is invalid.", invalid.Output);

        using var outsideWorkspace = new TempWorkingDirectory();
        var outsideRoot = new ProjectRoot();
        var outside = new WikiSearchCommand(new WikiService(outsideRoot), CreateLinkedReads(outsideRoot));
        var missingProject = await CaptureConsole(() => outside.ExecuteAsync(null!,
            new WikiSearchCommand.Settings { Query = "needle" }, CancellationToken.None));

        Assert.Equal(1, missingProject.ExitCode);
        Assert.Contains("Project not found. Run pm init first.", missingProject.Output);
    }

    [Fact]
    public async Task WikiSearchEscapesMarkupLikePageValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        service.CreatePage("docs/[needle]", "[Needle] <title>", "Needle [snippet] <body>");
        var command = new WikiSearchCommand(service, CreateLinkedReads(projectRoot));

        var result = await CaptureConsole(() => command.ExecuteAsync(null!,
            new WikiSearchCommand.Settings { Query = "needle" }, CancellationToken.None));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("docs/[needle]", result.Output);
        Assert.Contains("[Needle] <title>", result.Output);
        Assert.Contains("Needle [snippet] <body>", result.Output);
    }

    [Fact]
    public async Task TaskSearchRendersDenseResultsEmptyAndInvalidQueries()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(milestones: new() { ["M1"] = "First" }));
        var task = TestData.Task("PM-0001", "Find [render]", "Useful <snippet>", milestone: "M1");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "review");
        var command = new TaskSearchCommand(
            new TaskService(projectRoot, new RecordingNextIdService()),
            CreateLinkedReads(projectRoot));

        var found = await CaptureConsole(() => command.ExecuteAsync(null!,
            new TaskSearchCommand.Settings { Query = "state:review", Limit = 1 }, CancellationToken.None));
        var empty = await CaptureConsole(() => command.ExecuteAsync(null!,
            new TaskSearchCommand.Settings { Query = "missing" }, CancellationToken.None));
        var invalid = await CaptureConsole(() => command.ExecuteAsync(null!,
            new TaskSearchCommand.Settings { Query = "track:" }, CancellationToken.None));

        Assert.Equal(0, found.ExitCode);
        Assert.Contains("PM-0001", found.Output);
        Assert.Contains("Find [render]", found.Output);
        Assert.Contains("Useful <snippet>", found.Output);
        Assert.Contains("review", found.Output);
        Assert.Contains("M1", found.Output);
        Assert.Equal(0, empty.ExitCode);
        Assert.Contains("No matching tasks.", empty.Output);
        Assert.Equal(1, invalid.ExitCode);
        Assert.Contains("requires a value", invalid.Output);
    }

    [Fact]
    public async Task LinkedReadCommandSettingsRejectConflictingScopeAndRenderOwnership()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot.RootPath!, GlobalConfig.ProjectIdFile), "prj_active\n");
        var task = TestData.Task("PM-0001", "Selected task");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var command = new ListCommand(new BoardService(projectRoot), CreateLinkedReads(projectRoot));

        var invalid = new ListCommand.Settings { Project = "current", Family = true }.Validate();
        var selected = await ExecuteListCommand(command, new ListCommand.Settings { Project = "current" });

        Assert.False(invalid.Successful);
        Assert.Contains("cannot be used together", invalid.Message);
        Assert.Equal(0, selected.ExitCode);
        Assert.Contains(projectRoot.Config!.Name, selected.Output);
        Assert.Contains("/ current", selected.Output);
        Assert.Contains("prj_active", selected.Output);
        Assert.Contains("Selected task", selected.Output);
    }

    [Fact]
    public async Task WikiShowRendersPageAndRejectsMissingPage()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        service.CreatePage("architecture/rendering", "Rendering", "# Rendering\n\nDetails");
        var command = new WikiShowCommand(service, CreateLinkedReads(projectRoot), CreateHighlighter());

        var (exitCode, output) = await CaptureConsole(() =>
            command.ExecuteAsync(null!, new WikiShowCommand.Settings { Path = "architecture/rendering" },
                CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("Rendering", output);
        Assert.Contains("Path: architecture/rendering", output);
        Assert.Contains("# Rendering", output);
        Assert.Contains("Details", output);

        var (missingExitCode, missingOutput) = await CaptureConsole(() =>
            command.ExecuteAsync(null!, new WikiShowCommand.Settings { Path = "missing" }, CancellationToken.None));

        Assert.Equal(1, missingExitCode);
        Assert.Contains("not found", missingOutput);
    }

    [Fact]
    public async Task WikiCreateWritesPageWithRequiredTitleAndOptionalBody()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        var command = new WikiCreateCommand(service, new RecordingEditorService(), CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new WikiCreateCommand.Settings
            {
                Path = "architecture/rendering",
                Title = "Rendering",
                Body = "# Rendering",
            },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        var content = File.ReadAllText(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md"));
        Assert.Contains("title: Rendering", content);
        Assert.EndsWith("---\n\n# Rendering", content);
    }

    [Fact]
    public async Task WikiCreateEditWritesEditedFullMarkdown()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        var edited = new WikiPage
        {
            Path = "notes",
            Title = "Edited Notes",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Body = "# Edited",
        };
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, edited.ToMarkdown()),
        };
        var command = new WikiCreateCommand(service, editor, CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new WikiCreateCommand.Settings { Path = "notes", Title = "Draft", Edit = true },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, editor.EditCalls);
        var page = service.ReadPage("notes");
        Assert.True(page.Success);
        Assert.Equal("Edited Notes", page.Payload!.Title);
        Assert.Equal("# Edited", page.Payload.Body);
    }

    [Fact]
    public async Task WikiCreateRejectsMissingTitleDuplicateInvalidPathAndInvalidEditedMarkdown()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        var command = new WikiCreateCommand(service, new RecordingEditorService(), CreateHighlighter());

        Assert.Equal(1, await command.ExecuteAsync(null!,
            new WikiCreateCommand.Settings { Path = "notes" }, CancellationToken.None));
        Assert.Equal(1, await command.ExecuteAsync(null!,
            new WikiCreateCommand.Settings { Path = "../escape", Title = "Escape" }, CancellationToken.None));

        Assert.Equal(0, await command.ExecuteAsync(null!,
            new WikiCreateCommand.Settings { Path = "notes", Title = "Notes" }, CancellationToken.None));
        Assert.Equal(1, await command.ExecuteAsync(null!,
            new WikiCreateCommand.Settings { Path = "notes", Title = "Duplicate" }, CancellationToken.None));

        var invalidEditor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, "not frontmatter"),
        };
        var editCommand = new WikiCreateCommand(service, invalidEditor, CreateHighlighter());

        Assert.Equal(1, await editCommand.ExecuteAsync(null!,
            new WikiCreateCommand.Settings { Path = "bad-edit", Title = "Bad", Edit = true },
            CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "bad-edit.md")));
    }

    [Fact]
    public async Task WikiEditUpdatesFullMarkdownAndRejectsInvalidMarkdown()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        service.CreatePage("architecture/rendering", "Rendering", "# Rendering");
        var originalContent = File.ReadAllText(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md"));
        var edited = new WikiPage
        {
            Path = "architecture/rendering",
            Title = "Render Pipeline",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Body = "# Updated",
        };
        var editor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, edited.ToMarkdown()),
        };
        var command = new WikiEditCommand(service, editor, CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new WikiEditCommand.Settings { Path = "architecture/rendering" }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));
        var page = service.ReadPage("architecture/rendering");
        Assert.True(page.Success);
        Assert.Equal("Render Pipeline", page.Payload!.Title);
        Assert.Equal("# Updated", page.Payload.Body);

        var invalidEditor = new RecordingEditorService
        {
            EditAction = path => File.WriteAllText(path, "not frontmatter"),
        };
        var invalidCommand = new WikiEditCommand(service, invalidEditor, CreateHighlighter());
        var beforeInvalidEdit = File.ReadAllText(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md"));

        Assert.Equal(1, await invalidCommand.ExecuteAsync(null!,
            new WikiEditCommand.Settings { Path = "architecture/rendering" }, CancellationToken.None));
        Assert.Equal(beforeInvalidEdit, File.ReadAllText(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));
        Assert.NotEqual(originalContent, beforeInvalidEdit);
    }

    [Fact]
    public async Task WikiRenamePersistsPathAndTitleAndRejectsFailures()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        Assert.True(service.CreatePage("architecture/rendering", "Rendering", "# Rendering").Success);
        Assert.True(service.CreatePage("reference/existing", "Existing", "").Success);
        var command = new WikiRenameCommand(service);

        Assert.Equal(0, command.Execute(null!,
            new WikiRenameCommand.Settings
            {
                Path = "architecture/rendering",
                NewPath = "architecture/pipeline",
                Title = "Render Pipeline",
            },
            CancellationToken.None));

        var page = service.ReadPage("architecture/pipeline");
        Assert.True(page.Success);
        Assert.Equal("Render Pipeline", page.Payload!.Title);
        Assert.Equal("# Rendering", page.Payload.Body);
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));

        Assert.Equal(1, command.Execute(null!,
            new WikiRenameCommand.Settings
            {
                Path = "missing",
                NewPath = "reference/missing",
                Title = "Missing",
            },
            CancellationToken.None));
        Assert.Equal(1, command.Execute(null!,
            new WikiRenameCommand.Settings
            {
                Path = "architecture/pipeline",
                NewPath = "reference/existing",
                Title = "Duplicate",
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task WikiRemoveRequiresConfirmationDeletesPageAndRejectsMissingPage()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new WikiService(projectRoot);
        Assert.True(service.CreatePage("architecture/rendering", "Rendering", "# Rendering").Success);
        var command = new WikiRemoveCommand(service);

        Assert.Equal(1, command.Execute(null!,
            new WikiRemoveCommand.Settings { Path = "architecture/rendering" }, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));

        Assert.Equal(0, command.Execute(null!,
            new WikiRemoveCommand.Settings { Path = "architecture/rendering", Yes = true },
            CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md")));

        Assert.Equal(1, command.Execute(null!,
            new WikiRemoveCommand.Settings { Path = "architecture/rendering", Yes = true },
            CancellationToken.None));
    }

    [Fact]
    public async Task TaskRemoveDeletesTaskAndRejectsMissingTask()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var task = TestData.Task("PM-0001", "Remove me");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");
        var command = new TaskRemoveCommand(new TaskService(projectRoot, new RecordingNextIdService()));

        Assert.Equal(1,
            command.Execute(null!, new TaskRemoveCommand.Settings { TaskId = "PM-9999" }, CancellationToken.None));
        Assert.Equal(0,
            command.Execute(null!, new TaskRemoveCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(projectRoot.TasksPath, "PM-0001.md")));
        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0001.ref")));
    }

    [Fact]
    public async Task TrackAddWritesConfigAndRejectsDuplicatesAndEmptyValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var command = new TrackAddCommand(new ProjectConfigService(projectRoot));

        Assert.Equal(0,
            command.Execute(null!, new TrackAddCommand.Settings { Key = "BUILD", Name = "Build" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!, new TrackAddCommand.Settings { Key = "BUILD", Name = "Duplicate" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!, new TrackAddCommand.Settings { Key = " ", Name = "Missing" },
                CancellationToken.None));

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("Build", config.Tracks["BUILD"]);
    }

    [Fact]
    public async Task TrackRemoveRejectsReferencedTracksAndRemovesUnusedTracks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build", ["UI"] = "UI" }));
        projectRoot.WriteTask(TestData.Task("BUILD-0001", "Build task", track: "BUILD"));
        var command = new TrackRemoveCommand(new ProjectConfigService(projectRoot));

        Assert.Equal(1,
            command.Execute(null!, new TrackRemoveCommand.Settings { Key = "BUILD" }, CancellationToken.None));
        Assert.Equal(0,
            command.Execute(null!, new TrackRemoveCommand.Settings { Key = "UI" }, CancellationToken.None));

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.False(config.Tracks.ContainsKey("UI"));
    }

    [Fact]
    public async Task TrackAddPersistsWhenConfigHasOnlyLegacyDefaultTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        File.WriteAllText(Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile), """
                                                                                         name: Legacy
                                                                                         idWidth: 4
                                                                                         idPrefix: PM
                                                                                         taskStates:
                                                                                           todo: To Do
                                                                                         """);
        projectRoot = new ProjectRoot();
        var command = new TrackAddCommand(new ProjectConfigService(projectRoot));

        var exitCode = command.Execute(null!, new TrackAddCommand.Settings { Key = "BUILD", Name = "Build" },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("PM", config.Tracks["PM"]);
        Assert.Equal("Build", config.Tracks["BUILD"]);
    }

    [Fact]
    public async Task StatusAddRenameRemovePersistConfigAndStateDirectories()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var service = new ProjectConfigService(projectRoot);
        var add = new StatusAddCommand(service);
        var rename = new StatusRenameCommand(service);
        var remove = new StatusRemoveCommand(service);

        Assert.Equal(0,
            add.Execute(null!, new StatusAddCommand.Settings { Key = "blocked", Name = "Blocked" },
                CancellationToken.None));
        Assert.Equal(0,
            rename.Execute(null!, new StatusRenameCommand.Settings { Key = "blocked", Name = "Waiting" },
                CancellationToken.None));
        Assert.Equal(0,
            remove.Execute(null!, new StatusRemoveCommand.Settings { Key = "blocked" }, CancellationToken.None));
        Assert.Equal(1,
            remove.Execute(null!, new StatusRemoveCommand.Settings { Key = "missing" }, CancellationToken.None));

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.False(config.TaskStates.ContainsKey("blocked"));
        Assert.False(Directory.Exists(Path.Combine(projectRoot.StatesPath, "blocked")));
    }

    [Fact]
    public async Task TrackAndMilestoneRenameCommandsPersistDisplayNames()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            tracks: new Dictionary<string, string> { ["PM"] = "Project", ["BUILD"] = "Build" },
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var service = new ProjectConfigService(projectRoot);

        Assert.Equal(0,
            new TrackRenameCommand(service).Execute(null!,
                new TrackRenameCommand.Settings { Key = "BUILD", Name = "Build Work" }, CancellationToken.None));
        Assert.Equal(0,
            new MilestoneRenameCommand(service).Execute(null!,
                new MilestoneRenameCommand.Settings { Key = "m1", Title = "Launch" }, CancellationToken.None));
        Assert.Equal(1,
            new TrackRenameCommand(service).Execute(null!,
                new TrackRenameCommand.Settings { Key = "missing", Name = "Missing" }, CancellationToken.None));

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("Build Work", config.Tracks["BUILD"]);
        Assert.Equal("Launch", config.Milestones["m1"]);
    }

    [Fact]
    public async Task MilestoneAddWritesConfigAndRejectsDuplicatesEmptyValuesAndInvalidPriority()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var command = new MilestoneAddCommand(new ProjectConfigService(projectRoot));

        Assert.Equal(0,
            command.Execute(null!,
                new MilestoneAddCommand.Settings { Key = "m1", Title = "Milestone 1", Priority = "HIGH" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!, new MilestoneAddCommand.Settings { Key = "m1", Title = "Duplicate" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!, new MilestoneAddCommand.Settings { Key = "m2", Title = " " },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!,
                new MilestoneAddCommand.Settings { Key = "m2", Title = "Milestone 2", Priority = "later" },
                CancellationToken.None));

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("Milestone 1", config.Milestones["m1"]);
        Assert.Equal("high", config.MilestonePriorities["m1"]);
        Assert.False(config.Milestones.ContainsKey("m2"));
    }

    [Fact]
    public async Task MilestonePriorityCommandPersistsPriority()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1" }));
        var command = new MilestonePriorityCommand(new ProjectConfigService(projectRoot));

        Assert.Equal(0,
            command.Execute(null!, new MilestonePriorityCommand.Settings { Key = "m1", Priority = "Urgent" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!, new MilestonePriorityCommand.Settings { Key = "m1", Priority = "later" },
                CancellationToken.None));

        Assert.Equal("urgent", ProjectConfig.ReadConfig(projectRoot).MilestonePriorities["m1"]);
    }

    [Fact]
    public async Task MilestoneListCommandShowsEscapedTitleAndPriority()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m<1"] = "Milestone <One>" },
            milestonePriorities: new Dictionary<string, string> { ["m<1"] = "medium" }));
        var command = new MilestoneListCommand(new ProjectConfigService(projectRoot));

        var (exitCode, output) = await CaptureConsole(() =>
            command.Execute(null!, new MilestoneListCommand.Settings(), CancellationToken.None));

        Assert.Equal(0, exitCode);
        Assert.Contains("m<1", output);
        Assert.Contains("Milestone <One>", output);
        Assert.Contains("medium", output);
    }

    [Fact]
    public async Task MilestoneRemoveRejectsReferencedMilestonesAndRemovesUnusedMilestones()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(
            milestones: new Dictionary<string, string> { ["m1"] = "Milestone 1", ["m2"] = "Milestone 2" },
            milestonePriorities: new Dictionary<string, string> { ["m2"] = "high" }));
        projectRoot.WriteTask(TestData.Task("PM-0001", "Milestone task", milestone: "m1"));
        var command = new MilestoneRemoveCommand(new ProjectConfigService(projectRoot));

        Assert.Equal(1,
            command.Execute(null!, new MilestoneRemoveCommand.Settings { Key = "m1" }, CancellationToken.None));
        Assert.Equal(0,
            command.Execute(null!, new MilestoneRemoveCommand.Settings { Key = "m2" }, CancellationToken.None));

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.False(config.Milestones.ContainsKey("m2"));
        Assert.False(config.MilestonePriorities.ContainsKey("m2"));
    }

    private static SyntaxHighlighter CreateHighlighter()
    {
        return new SyntaxHighlighter([
            new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
        ]);
    }

    private static LinkedProjectReadService CreateLinkedReads(ProjectRoot projectRoot)
    {
        var registryRoot = projectRoot.Exists
            ? Path.Combine(projectRoot.RepositoryPath, ".command-test-registry")
            : Path.Combine(Path.GetTempPath(), $"pm-command-test-registry-{Guid.NewGuid():N}");
        var family = new LinkedProjectFamilyService(
            projectRoot,
            new LinkedProjectService(projectRoot),
            new LinkedProjectResolver(
                new LinkedProjectRegistryStore(new LinkedProjectRegistryStoreOptions { RootPath = registryRoot }),
                new EmptyLinkedProjectSubmoduleInspector()));
        return new LinkedProjectReadService(
            projectRoot,
            family,
            new RecordingNextIdService(),
            new LinkedProjectGitInspector());
    }

    private static async Task<(int ExitCode, string Output)> ExecuteListCommand(
        ListCommand command,
        ListCommand.Settings settings)
    {
        return await CaptureConsole(() => command.ExecuteAsync(null!, settings, CancellationToken.None));
    }

    private static async Task<(int ExitCode, string Output)> CaptureConsole(Func<int> execute)
    {
        return await CaptureConsole(() => Task.FromResult(execute()));
    }

    private static async Task<(int ExitCode, string Output)> CaptureConsole(Func<Task<int>> execute)
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
            var exitCode = await execute();
            return (exitCode, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
        }
    }

    private sealed class RecordingNextIdService : INextIdService
    {
        public int GetNextIdCalls { get; private set; }
        public int PeekExistingNextIdCalls { get; private set; }
        public int HealthyCalls { get; private set; }
        public List<string> GetNextIdTracks { get; } = [];

        public Task<int> GetNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            GetNextIdCalls++;
            GetNextIdTracks.Add(track);
            return Task.FromResult(1);
        }

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default)
        {
            PeekExistingNextIdCalls++;
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
            return Task.FromResult(true);
        }
    }

    private sealed class EmptyLinkedProjectSubmoduleInspector : ILinkedProjectSubmoduleInspector
    {
        public Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
            string repositoryPath,
            string pathHint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<LinkedProjectRepairAction?>.Ok(null));
    }

    private sealed class RecordingEditorService : IEditorService
    {
        public int EditCalls { get; private set; }
        public int ExitCode { get; init; }
        public Action<string>? EditAction { get; init; }

        public Task<int> EditFile(string filePath, CancellationToken cancellationToken)
        {
            EditCalls++;
            EditAction?.Invoke(filePath);
            return Task.FromResult(ExitCode);
        }
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
}
