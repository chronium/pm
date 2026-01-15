using System.Diagnostics.CodeAnalysis;
using PM.Files;
using PM.Project;

namespace PM.Tasks;

public static class TaskState
{
    public static void SetState(ProjectRoot projectRoot, TaskItem task, string state)
    {
        if (TryGetState(projectRoot, task, out var currentState))
            FileSystem.DeleteFile(Path.Combine(projectRoot.StatesPath, currentState, $"{task.Id}.ref"));

        var stateDir = Path.Combine(projectRoot.StatesPath, state);
        var stateRelativePath = Path.GetRelativePath(stateDir, projectRoot.TasksPath);

        FileSystem.WriteAllText(Path.Combine(projectRoot.StatesPath, state, $"{task.Id}.ref"),
            $"{stateRelativePath}/{task.Id}.{GlobalConfig.DefaultTaskExtension}");
    }

    public static bool TryGetState(ProjectRoot projectRoot, TaskItem task, [MaybeNullWhen(false)] out string state)
    {
        state = null;
        foreach (var key in projectRoot.Config!.TaskStates.Keys)
        {
            var statePath = Path.Combine(projectRoot.StatesPath, key, $"{task.Id}.ref");
            if (FileSystem.FileExists(statePath))
            {
                state = key;
                return true;
            }
        }

        return false;
    }

    public static List<TaskItem> GetTasksInState(ProjectRoot projectRoot, string state)
    {
        var statePath = Path.Combine(projectRoot.StatesPath, state);
        if (!FileSystem.DirectoryExists(statePath)) return [];

        var items = new List<TaskItem>();

        foreach (var refFile in FileSystem.ReadFiles(statePath, "*.ref"))
        {
            var item = TaskItem.Parse(FileSystem.ReadAllText(ResolveRef(refFile)));
            if (item == null)
                continue;

            items.Add(item);
        }

        return items;
    }

    private static string ResolveRef(FileInfo refFile)
    {
        var refContent = FileSystem.ReadAllText(refFile.FullName);
        return Path.Combine(refFile.Directory!.FullName, refContent);
    }
}