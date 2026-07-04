using PM.Project;
using PM.Tasks;
using PM.Wiki;

namespace PM.Tests;

public class ProjectRootStorageTests
{
    [Fact]
    public async Task WritingTaskCreatesMarkdownFileWithYamlFrontmatter()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0001", "Wire storage tests");

        projectRoot.WriteTask(task);

        var taskPath = Path.Combine(projectRoot.TasksPath, "PM-0001.md");
        var content = File.ReadAllText(taskPath);
        Assert.StartsWith("---\n", content);
        Assert.Contains("id: PM-0001", content);
        Assert.Contains("title: Wire storage tests", content);
        Assert.Contains("track: PM", content);
        Assert.EndsWith("---\n\n", content);
    }

    [Fact]
    public void ParsingFrontmatterOnlyTaskReturnsEmptyDescription()
    {
        var task = TaskItem.Parse("""
                                  ---
                                  id: PM-0001
                                  title: Existing task
                                  createdAt: 2026-01-01T00:00:00.0000000Z
                                  modifiedAt: 2026-01-01T00:00:00.0000000Z
                                  ---
                                  """);

        Assert.NotNull(task);
        Assert.Equal("Existing task", task.Title);
        Assert.Equal(string.Empty, task.Description);
    }

    [Fact]
    public void ParsingTaskWithMarkdownBodyReturnsDescription()
    {
        var task = TaskItem.Parse("""
                                  ---
                                  id: PM-0002
                                  title: Describe task
                                  createdAt: 2026-01-01T00:00:00.0000000Z
                                  modifiedAt: 2026-01-01T00:00:00.0000000Z
                                  ---

                                  # Scope

                                  - Preserve markdown.
                                  """);

        Assert.NotNull(task);
        Assert.Equal("Describe task", task.Title);
        Assert.Equal("# Scope\n\n- Preserve markdown.", task.Description);
    }

    [Fact]
    public async Task WritingTaskWithDescriptionPreservesMarkdownBodyAfterFrontmatter()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0005", "Write description", "# Scope\n\n- Preserve markdown.");

        projectRoot.WriteTask(task);

        var content = File.ReadAllText(Path.Combine(projectRoot.TasksPath, "PM-0005.md"));
        Assert.Contains("title: Write description", content);
        Assert.EndsWith("---\n\n# Scope\n\n- Preserve markdown.", content);
    }

    [Fact]
    public async Task UpdatingTaskStateCreatesRefFile()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0002", "Create a ref");
        projectRoot.WriteTask(task);

        projectRoot.UpdateTaskState(task, "todo");

        var refPath = Path.Combine(projectRoot.StatesPath, "todo", "PM-0002.ref");
        Assert.True(File.Exists(refPath));
        Assert.Equal("../../tasks/PM-0002.md", File.ReadAllText(refPath));
    }

    [Fact]
    public async Task MovingTaskStateDeletesPreviousRefAndWritesNewRef()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0003", "Move between states");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        projectRoot.UpdateTaskState(task, "review");

        Assert.False(File.Exists(Path.Combine(projectRoot.StatesPath, "todo", "PM-0003.ref")));
        Assert.True(File.Exists(Path.Combine(projectRoot.StatesPath, "review", "PM-0003.ref")));
    }

    [Fact]
    public async Task ReadingTasksInStateReturnsParsedTasks()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var task = TestData.Task("PM-0004", "Read from state");
        projectRoot.WriteTask(task);
        projectRoot.UpdateTaskState(task, "todo");

        var tasks = projectRoot.GetTasksInState("todo");

        var item = Assert.Single(tasks);
        Assert.Equal("PM-0004", item.Id);
        Assert.Equal("Read from state", item.Title);
    }

    [Fact]
    public async Task TaskWithoutTrackResolvesToDefaultTrack()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(idPrefix: "PM"));
        var task = TaskItem.Parse("""
                                  ---
                                  id: PM-0001
                                  title: Legacy task
                                  createdAt: 2026-01-01T00:00:00.0000000Z
                                  modifiedAt: 2026-01-01T00:00:00.0000000Z
                                  ---
                                  """);

        Assert.NotNull(task);
        Assert.Equal("PM", projectRoot.ResolveTaskTrack(task));
    }

    [Fact]
    public async Task GetAllTasksScansTaskMarkdownFiles()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        projectRoot.WriteTask(TestData.Task("PM-0001", "First"));
        projectRoot.WriteTask(TestData.Task("PM-0002", "Second"));

        var tasks = projectRoot.GetAllTasks();

        Assert.Equal(["PM-0001", "PM-0002"], tasks.Select(task => task.Id).Order());
    }

    [Fact]
    public async Task ProjectCreationCreatesWikiDirectory()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();

        Assert.True(Directory.Exists(projectRoot.WikiPath));
    }

    [Fact]
    public void WikiPageParsesAndSerializesFrontmatterMarkdown()
    {
        var page = WikiPage.Parse("architecture/rendering", """
                                                           ---
                                                           title: Rendering
                                                           createdAt: 2026-01-01T00:00:00.0000000Z
                                                           modifiedAt: 2026-01-02T00:00:00.0000000Z
                                                           ---

                                                           # Rendering

                                                           Keep markdown.
                                                           """);

        Assert.NotNull(page);
        Assert.Equal("architecture/rendering", page.Path);
        Assert.Equal("Rendering", page.Title);
        Assert.Equal("# Rendering\n\nKeep markdown.", page.Body);

        var markdown = page.ToMarkdown();
        Assert.StartsWith("---\n", markdown);
        Assert.Contains("title: Rendering", markdown);
        Assert.Contains("createdAt: 2026-01-01T00:00:00.0000000Z", markdown);
        Assert.EndsWith("# Rendering\n\nKeep markdown.", markdown);
    }

    [Fact]
    public void WikiPageRejectsInvalidFrontmatter()
    {
        Assert.Null(WikiPage.Parse("bad", "not markdown"));
        Assert.Null(WikiPage.Parse("bad", """
                                          ---
                                          createdAt: 2026-01-01T00:00:00.0000000Z
                                          modifiedAt: 2026-01-01T00:00:00.0000000Z
                                          ---
                                          """));
        Assert.Null(WikiPage.Parse("bad", """
                                          ---
                                          title: " "
                                          createdAt: 2026-01-01T00:00:00.0000000Z
                                          modifiedAt: 2026-01-01T00:00:00.0000000Z
                                          ---
                                          """));
        Assert.Null(WikiPage.Parse("bad", """
                                          ---
                                          title: Missing dates
                                          ---
                                          """));
    }

    [Fact]
    public async Task WikiPathResolutionRejectsUnsafePaths()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();

        Assert.True(projectRoot.TryResolveWikiPath("architecture/rendering", out var normalized, out var filePath));
        Assert.Equal("architecture/rendering", normalized);
        Assert.Equal(Path.Combine(projectRoot.WikiPath, "architecture", "rendering.md"), filePath);

        Assert.True(projectRoot.TryResolveWikiPath("architecture/rendering.md", out normalized, out _));
        Assert.Equal("architecture/rendering", normalized);

        Assert.False(projectRoot.TryResolveWikiPath("../escape", out _, out _));
        Assert.False(projectRoot.TryResolveWikiPath("architecture//rendering", out _, out _));
        Assert.False(projectRoot.TryResolveWikiPath("/absolute", out _, out _));
        Assert.False(projectRoot.TryResolveWikiPath("architecture\\rendering", out _, out _));
        Assert.False(projectRoot.TryResolveWikiPath("architecture/rendering.txt", out _, out _));
    }
}
