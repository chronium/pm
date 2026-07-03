using CodePunk.Highlight.Core.SyntaxHighlighting;
using CodePunk.Highlight.Core.SyntaxHighlighting.Languages;
using PM.Project;
using PM.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Tests;

public class CommandBehaviorTests
{
    [Fact]
    public async Task ListOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var command = new ListCommand(new ProjectRoot());

        var (exitCode, _) = await ExecuteListCommand(command, new ListCommand.Settings());

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ListEmptyStatesRendersWithoutCrashing()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var command = new ListCommand(projectRoot);

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
        var command = new ListCommand(projectRoot);

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
        var command = new ListCommand(projectRoot);

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
        var command = new ListCommand(projectRoot);

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
        var command = new ListCommand(projectRoot);

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
        var command = new ListCommand(projectRoot);

        var (exitCode, output) = await ExecuteListCommand(command,
            new ListCommand.Settings { Track = "BUILD", Milestone = "m1", State = "review" });

        Assert.Equal(0, exitCode);
        Assert.Contains("Matching task", output);
        Assert.DoesNotContain("Wrong track", output);
        Assert.DoesNotContain("Wrong milestone", output);
        Assert.DoesNotContain("Wrong state", output);
    }

    [Fact]
    public async Task DryRunAddWithNoNextIdFileUsesPlaceholderAndDoesNotCreateProjectKey()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var nextIdService = new RecordingNextIdService();
        var highlighter = new SyntaxHighlighter([
            new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
        ]);
        var command = new TaskAddCommand(projectRoot, nextIdService, highlighter, new RecordingEditorService());
        GlobalConfig.DryRun = true;

        try
        {
            var exitCode = await command.ExecuteAsync(null!,
                new TaskAddCommand.Settings { DryRun = true, Title = "Preview task" }, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, nextIdService.GetNextIdCalls);
            Assert.Equal(1, nextIdService.PeekExistingNextIdCalls);
            Assert.Equal(0, nextIdService.HealthyCalls);
            Assert.False(File.Exists(Path.Combine(projectRoot.RootPath, GlobalConfig.NextIdFile)));
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
        var command = new TaskAddCommand(projectRoot, nextIdService, CreateHighlighter(), new RecordingEditorService());

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
        var command = new TaskAddCommand(projectRoot, nextIdService, CreateHighlighter(), new RecordingEditorService());

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
        var command = new TaskAddCommand(projectRoot, new RecordingNextIdService(), CreateHighlighter(),
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
        var command = new TaskAddCommand(projectRoot, new RecordingNextIdService(), CreateHighlighter(),
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
        var command = new TaskAddCommand(projectRoot, nextIdService, CreateHighlighter(), new RecordingEditorService());
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
        var command = new TaskAddCommand(projectRoot, nextIdService, CreateHighlighter(), editor);

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
    public async Task EditOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var command = new TaskEditCommand(new ProjectRoot(), new RecordingEditorService(), CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task EditMissingTaskReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM", idWidth: 4));
        var command = new TaskEditCommand(projectRoot, new RecordingEditorService(), CreateHighlighter());

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
        var command = new TaskEditCommand(projectRoot, editor, CreateHighlighter());

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
        var command = new TaskEditCommand(projectRoot, editor, CreateHighlighter());

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
        var command = new TaskEditCommand(projectRoot, editor, CreateHighlighter());

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
        var command = new TaskEditCommand(projectRoot, editor, CreateHighlighter());

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
        var command = new TaskEditCommand(projectRoot, editor, CreateHighlighter());

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
        var command = new TaskEditCommand(projectRoot, editor, CreateHighlighter());

        var exitCode = await command.ExecuteAsync(null!,
            new TaskEditCommand.Settings { TaskId = "PM-0001" }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var content = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0001.md"));
        Assert.Contains("title: Updated task", content);
        Assert.Contains("modifiedAt: 2026-02-01T00:00:00.0000000Z", content);
        Assert.EndsWith("---\n\n# Updated\n\nBody", content);
    }

    [Fact]
    public async Task TrackAddWritesConfigAndRejectsDuplicatesAndEmptyValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var command = new TrackAddCommand(projectRoot);

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
        var command = new TrackAddCommand(projectRoot);

        var exitCode = command.Execute(null!, new TrackAddCommand.Settings { Key = "BUILD", Name = "Build" },
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("PM", config.Tracks["PM"]);
        Assert.Equal("Build", config.Tracks["BUILD"]);
    }

    [Fact]
    public async Task MilestoneAddWritesConfigAndRejectsDuplicatesAndEmptyValues()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var command = new MilestoneAddCommand(projectRoot);

        Assert.Equal(0,
            command.Execute(null!, new MilestoneAddCommand.Settings { Key = "m1", Title = "Milestone 1" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!, new MilestoneAddCommand.Settings { Key = "m1", Title = "Duplicate" },
                CancellationToken.None));
        Assert.Equal(1,
            command.Execute(null!, new MilestoneAddCommand.Settings { Key = "m2", Title = " " },
                CancellationToken.None));

        var config = ProjectConfig.ReadConfig(projectRoot);
        Assert.Equal("Milestone 1", config.Milestones["m1"]);
    }

    private static SyntaxHighlighter CreateHighlighter()
    {
        return new SyntaxHighlighter([
            new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
        ]);
    }

    private static async Task<(int ExitCode, string Output)> ExecuteListCommand(
        ListCommand command,
        ListCommand.Settings settings)
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

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default)
        {
            HealthyCalls++;
            return Task.FromResult(true);
        }
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
