using CodePunk.Highlight.Core.SyntaxHighlighting;
using CodePunk.Highlight.Core.SyntaxHighlighting.Languages;
using PM.Project;
using PM.Tasks;
using Spectre.Console.Cli;

namespace PM.Tests;

public class CommandBehaviorTests
{
    [Fact]
    public async Task ListOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var command = new ListCommand(new ProjectRoot());

        var exitCode = await command.ExecuteAsync(null!, new CommonSettings(), CancellationToken.None);

        Assert.Equal(1, exitCode);
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

    private static SyntaxHighlighter CreateHighlighter()
    {
        return new SyntaxHighlighter([
            new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
        ]);
    }

    private sealed class RecordingNextIdService : INextIdService
    {
        public int GetNextIdCalls { get; private set; }
        public int PeekExistingNextIdCalls { get; private set; }
        public int HealthyCalls { get; private set; }

        public Task<int> GetNextId(ProjectRoot projectRoot, CancellationToken cancellationToken = default)
        {
            GetNextIdCalls++;
            return Task.FromResult(1);
        }

        public Task<int> PeekNextId(ProjectRoot projectRoot, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, CancellationToken cancellationToken = default)
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
}
