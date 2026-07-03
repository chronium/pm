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
        var command = new TaskAddCommand(projectRoot, nextIdService, highlighter);
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
        var command = new TaskAddCommand(projectRoot, nextIdService, CreateHighlighter());

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
        var command = new TaskAddCommand(projectRoot, nextIdService, CreateHighlighter());
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
        var command = new FailingEditorTaskAddCommand(projectRoot, nextIdService, CreateHighlighter());

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

    private sealed class FailingEditorTaskAddCommand(
        ProjectRoot projectRoot,
        INextIdService nextIdService,
        SyntaxHighlighter highlighter)
        : TaskAddCommand(projectRoot, nextIdService, highlighter)
    {
        protected override Task<int> RunEditor(string filePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(1);
        }
    }
}
