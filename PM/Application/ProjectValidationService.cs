using PM.Project;
using PM.Tasks;
using PM.Wiki;

namespace PM.Application;

public sealed record ProjectValidationIssue(
    string Severity,
    string Code,
    string Message,
    string? Path = null,
    string? TaskId = null,
    string? WikiPath = null,
    string? State = null);

public sealed record ProjectValidationResult(bool Valid, IReadOnlyList<ProjectValidationIssue> Issues);

public sealed class ProjectValidationService(ProjectRoot projectRoot)
{
    public AppResult<ProjectValidationResult> ValidateProject()
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<ProjectValidationResult>.Fail("missing_project", "Project not found. Run pm init first.");

        var issues = new List<ProjectValidationIssue>();
        var tasksById = ValidateTaskFiles(issues);
        ValidateStateRefs(issues, tasksById);
        ValidateWikiPages(issues);
        ValidateTaskOrder(issues, tasksById);

        return AppResult<ProjectValidationResult>.Ok(new ProjectValidationResult(issues.Count == 0, issues));
    }

    private Dictionary<string, TaskItem> ValidateTaskFiles(List<ProjectValidationIssue> issues)
    {
        var tasksById = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!Directory.Exists(projectRoot.TasksPath))
            return tasksById;

        foreach (var filePath in Directory.EnumerateFiles(projectRoot.TasksPath, $"*.{GlobalConfig.DefaultTaskExtension}"))
        {
            var content = File.ReadAllText(filePath);
            var task = TaskItem.Parse(content);
            if (task == null)
            {
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "invalid_task_markdown",
                    "Task file has invalid frontmatter or body.",
                    filePath));
                continue;
            }

            idCounts[task.Id] = idCounts.GetValueOrDefault(task.Id) + 1;
            tasksById.TryAdd(task.Id, task);

            var fileId = Path.GetFileNameWithoutExtension(filePath);
            if (!string.Equals(fileId, task.Id, StringComparison.Ordinal))
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "task_filename_mismatch",
                    $"Task file name {fileId} does not match parsed ID {task.Id}.",
                    filePath,
                    task.Id));

            var track = projectRoot.ResolveTaskTrack(task);
            if (!projectRoot.Config!.Tracks.ContainsKey(track))
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "unknown_task_track",
                    $"Task {task.Id} references unknown track {track}.",
                    filePath,
                    task.Id));

            if (!string.IsNullOrWhiteSpace(task.Milestone) &&
                !projectRoot.Config.Milestones.ContainsKey(task.Milestone))
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "unknown_task_milestone",
                    $"Task {task.Id} references unknown milestone {task.Milestone}.",
                    filePath,
                    task.Id));
        }

        foreach (var duplicate in idCounts.Where(entry => entry.Value > 1))
            issues.Add(new ProjectValidationIssue(
                "error",
                "duplicate_task_id",
                $"Task ID {duplicate.Key} appears in multiple task files.",
                TaskId: duplicate.Key));

        return tasksById;
    }

    private void ValidateStateRefs(List<ProjectValidationIssue> issues, Dictionary<string, TaskItem> tasksById)
    {
        var refsByTask = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!Directory.Exists(projectRoot.StatesPath))
            return;

        foreach (var stateDir in Directory.EnumerateDirectories(projectRoot.StatesPath))
        {
            var state = Path.GetFileName(stateDir);
            var knownState = projectRoot.Config!.TaskStates.ContainsKey(state);
            if (!knownState)
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "unknown_state_directory",
                    $"State directory {state} is not configured.",
                    stateDir,
                    State: state));

            foreach (var refPath in Directory.EnumerateFiles(stateDir, "*.ref"))
            {
                var taskId = Path.GetFileNameWithoutExtension(refPath);
                var target = ResolveRefTarget(refPath);
                if (!File.Exists(target))
                {
                    issues.Add(new ProjectValidationIssue(
                        "error",
                        "broken_ref_target",
                        $"State ref {refPath} points to a missing task file.",
                        refPath,
                        taskId,
                        State: state));
                    continue;
                }

                var task = TaskItem.Parse(File.ReadAllText(target));
                if (task == null)
                {
                    issues.Add(new ProjectValidationIssue(
                        "error",
                        "invalid_ref_task_markdown",
                        $"State ref {refPath} points to invalid task markdown.",
                        refPath,
                        taskId,
                        State: state));
                    continue;
                }

                if (knownState)
                    refsByTask[taskId] = refsByTask.GetValueOrDefault(taskId) + 1;
            }
        }

        foreach (var task in tasksById.Values)
        {
            if (!refsByTask.ContainsKey(task.Id))
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "missing_current_state",
                    $"Task {task.Id} has no valid state reference.",
                    projectRoot.GetTaskFilePath(task.Id),
                    task.Id));
        }
    }

    private void ValidateWikiPages(List<ProjectValidationIssue> issues)
    {
        foreach (var (path, filePath, content) in projectRoot.GetWikiMarkdownFiles())
        {
            if (WikiPage.Parse(path, content) != null) continue;
            issues.Add(new ProjectValidationIssue(
                "error",
                "invalid_wiki_markdown",
                $"Wiki page {path} markdown is invalid.",
                filePath,
                WikiPath: path));
        }
    }

    private void ValidateTaskOrder(List<ProjectValidationIssue> issues, Dictionary<string, TaskItem> tasksById)
    {
        if (!File.Exists(projectRoot.TaskOrderPath))
            return;

        TaskOrderFile order;
        try
        {
            order = YamlSerde.Deserialize<TaskOrderFile>(File.ReadAllText(projectRoot.TaskOrderPath)) ??
                    new TaskOrderFile();
        }
        catch
        {
            issues.Add(new ProjectValidationIssue(
                "error",
                "invalid_task_order",
                "Task order file is not valid YAML.",
                projectRoot.TaskOrderPath));
            return;
        }

        foreach (var entry in order.Orders)
        {
            if (!projectRoot.Config!.Tracks.ContainsKey(entry.Track) ||
                !projectRoot.Config.TaskStates.ContainsKey(entry.State) ||
                (!string.IsNullOrWhiteSpace(entry.Milestone) &&
                 !projectRoot.Config.Milestones.ContainsKey(entry.Milestone)))
            {
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "stale_task_order_scope",
                    "Task order entry references a stale track, state, or milestone.",
                    projectRoot.TaskOrderPath,
                    State: entry.State));
            }

            foreach (var taskId in entry.TaskIds)
            {
                if (!tasksById.TryGetValue(taskId, out var task))
                {
                    issues.Add(new ProjectValidationIssue(
                        "error",
                        "stale_task_order_task",
                        $"Task order references missing task {taskId}.",
                        projectRoot.TaskOrderPath,
                        taskId,
                        State: entry.State));
                    continue;
                }

                var inScope = projectRoot.ResolveTaskTrack(task) == entry.Track &&
                              string.Equals(task.Milestone, NormalizeMilestone(entry.Milestone),
                                  StringComparison.Ordinal) &&
                              projectRoot.TryGetState(task, out var state) &&
                              state == entry.State;
                if (!inScope)
                    issues.Add(new ProjectValidationIssue(
                        "error",
                        "stale_task_order_scope",
                        $"Task order references task {taskId} outside its stored scope.",
                        projectRoot.TaskOrderPath,
                        taskId,
                        State: entry.State));
            }
        }
    }

    private static string ResolveRefTarget(string refPath)
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(refPath)!, File.ReadAllText(refPath).Trim()));
    }

    private static string? NormalizeMilestone(string? milestone)
    {
        return string.IsNullOrWhiteSpace(milestone) ? null : milestone.Trim();
    }
}
