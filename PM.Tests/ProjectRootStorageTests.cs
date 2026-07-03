using PM.Project;
using PM.Tasks;

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
}
