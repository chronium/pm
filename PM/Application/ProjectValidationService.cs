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
    string? State = null,
    string? ProjectId = null,
    string? ProjectAlias = null);

public sealed record ProjectValidationResult(bool Valid, IReadOnlyList<ProjectValidationIssue> Issues);

public sealed class ProjectValidationService
{
    private readonly ProjectRoot projectRoot;
    private readonly LinkedProjectService linkedProjects;
    private readonly LinkedProjectFamilyService linkedProjectFamily;
    private readonly LinkedProjectTaskGraphService linkedTaskGraph;
    private readonly MilestoneActivationValidationService milestoneActivationValidation;

    public ProjectValidationService(ProjectRoot projectRoot)
        : this(projectRoot, new LinkedProjectService(projectRoot),
            LinkedProjectFamilyService.CreateDefault(projectRoot))
    {
    }

    public ProjectValidationService(
        ProjectRoot projectRoot,
        LinkedProjectService linkedProjects,
        LinkedProjectFamilyService linkedProjectFamily)
        : this(projectRoot, linkedProjects, linkedProjectFamily,
            new LinkedProjectTaskGraphService(linkedProjectFamily))
    {
    }

    public ProjectValidationService(
        ProjectRoot projectRoot,
        LinkedProjectService linkedProjects,
        LinkedProjectFamilyService linkedProjectFamily,
        LinkedProjectTaskGraphService linkedTaskGraph)
        : this(projectRoot, linkedProjects, linkedProjectFamily, linkedTaskGraph,
            new MilestoneActivationValidationService(projectRoot))
    {
    }

    public ProjectValidationService(
        ProjectRoot projectRoot,
        LinkedProjectService linkedProjects,
        LinkedProjectFamilyService linkedProjectFamily,
        LinkedProjectTaskGraphService linkedTaskGraph,
        MilestoneActivationValidationService milestoneActivationValidation)
    {
        this.projectRoot = projectRoot;
        this.linkedProjects = linkedProjects;
        this.linkedProjectFamily = linkedProjectFamily;
        this.linkedTaskGraph = linkedTaskGraph;
        this.milestoneActivationValidation = milestoneActivationValidation;
    }

    public AppResult<ProjectValidationResult> ValidateProject() =>
        ValidateProjectAsync().GetAwaiter().GetResult();

    public async Task<AppResult<ProjectValidationResult>> ValidateProjectAsync(
        CancellationToken cancellationToken = default)
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<ProjectValidationResult>.Fail("missing_project", "Project not found. Run pm init first.");

        var issues = new List<ProjectValidationIssue>();
        ValidateConfigMetadata(issues);
        await ValidateLinkedProjects(issues, cancellationToken);
        var tasksById = ValidateTaskFiles(issues);
        issues.AddRange(milestoneActivationValidation.Validate(projectRoot.Config, tasksById));
        ValidateTaskDependencies(issues, tasksById);
        ValidateStateRefs(issues, tasksById);
        ValidateWikiPages(issues);
        ValidateTaskOrder(issues, tasksById);

        var valid = issues.All(issue =>
            !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return AppResult<ProjectValidationResult>.Ok(new ProjectValidationResult(valid, issues));
    }

    private async Task ValidateLinkedProjects(
        List<ProjectValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var result = linkedProjects.GetManifest();
        if (!result.Success)
        {
            issues.Add(new ProjectValidationIssue(
                "error",
                result.ErrorCode ?? "invalid_linked_projects_manifest",
                result.Message ?? "Linked-project manifest is invalid.",
                projectRoot.LinkedProjectsPath));
            return;
        }
        if (!result.Payload!.Exists) return;

        var family = await linkedProjectFamily.ResolveAsync(cancellationToken);
        if (!family.Success)
        {
            issues.Add(new ProjectValidationIssue(
                "error",
                family.ErrorCode ?? "linked_project_validation_failed",
                family.Message ?? "Linked-project topology could not be validated.",
                projectRoot.LinkedProjectsPath));
            return;
        }

        issues.AddRange(family.Payload!.Warnings.Select(warning => new ProjectValidationIssue(
            "warning",
            warning.Code,
            warning.Message,
            ProjectId: warning.TargetProjectId,
            ProjectAlias: warning.Alias)));
        issues.AddRange(linkedTaskGraph.Build(family.Payload).Warnings.Select(warning =>
            new ProjectValidationIssue(
                "warning",
                warning.Code,
                warning.Message,
                ProjectId: warning.TargetProjectId,
                ProjectAlias: warning.Alias)));
    }

    private void ValidateConfigMetadata(List<ProjectValidationIssue> issues)
    {
        var config = projectRoot.Config!;
        var configPath = projectRoot.RootPath == null
            ? null
            : Path.Combine(projectRoot.RootPath, GlobalConfig.PmConfigFile);

        if (config.RequiresMilestoneSchemaMigration)
            issues.Add(new ProjectValidationIssue(
                "warning",
                "legacy_milestone_schema",
                "Milestones use the legacy scalar schema. Run pm doctor --fix to migrate them.",
                configPath));

        foreach (var (milestone, _) in config.LegacyMilestonePriorities)
        {
            if (!config.Milestones.ContainsKey(milestone))
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "unknown_milestone_priority",
                    $"Milestone priority references unknown milestone {milestone}.",
                    configPath));
        }

        foreach (var (milestone, definition) in config.Milestones)
        {
            if (!PriorityLevel.TryNormalize(definition.Priority, out _))
                issues.Add(new ProjectValidationIssue(
                    "error",
                    "invalid_milestone_priority",
                    $"Milestone {milestone} has invalid priority {definition.Priority}.",
                    configPath));
        }
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
            if (!TaskItem.TryParse(content, out var task, out var errorCode, out var message) || task == null)
            {
                issues.Add(new ProjectValidationIssue(
                    "error",
                    errorCode,
                    message,
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

    private void ValidateTaskDependencies(List<ProjectValidationIssue> issues, Dictionary<string, TaskItem> tasksById)
    {
        var activeProjectId = projectRoot.TryReadProjectId(out var projectId) ? projectId : null;
        foreach (var task in tasksById.Values)
        {
            foreach (var dependencyValue in task.DependencyIds)
            {
                if (!TaskDependencyReference.TryParse(dependencyValue, out var dependency, out var message))
                {
                    issues.Add(new ProjectValidationIssue(
                        "error",
                        "invalid_dependency_reference",
                        $"Task {task.Id} has invalid dependency {dependencyValue}: {message}",
                        projectRoot.GetTaskFilePath(task.Id),
                        task.Id));
                    continue;
                }

                if (!dependency!.IsLocalTo(activeProjectId))
                    continue;

                var dependencyId = dependency.TaskId;
                if (string.Equals(task.Id, dependencyId, StringComparison.Ordinal))
                {
                    issues.Add(new ProjectValidationIssue(
                        "error",
                        "self_dependency",
                        $"Task {task.Id} cannot depend on itself.",
                        projectRoot.GetTaskFilePath(task.Id),
                        task.Id));
                    continue;
                }

                if (!tasksById.ContainsKey(dependencyId))
                    issues.Add(new ProjectValidationIssue(
                        "error",
                        "missing_dependency",
                        $"Task {task.Id} depends on missing task {dependencyId}.",
                        projectRoot.GetTaskFilePath(task.Id),
                        task.Id));
            }
        }

        foreach (var cycle in FindDependencyCycles(tasksById, activeProjectId))
            issues.Add(new ProjectValidationIssue(
                "error",
                "dependency_cycle",
                $"Task dependency cycle detected: {string.Join(" -> ", cycle)}.",
                projectRoot.GetTaskFilePath(cycle[0]),
                cycle[0]));
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindDependencyCycles(
        Dictionary<string, TaskItem> tasksById,
        string? activeProjectId)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new List<IReadOnlyList<string>>();

        foreach (var taskId in tasksById.Keys)
            Visit(taskId);

        return cycles;

        void Visit(string taskId)
        {
            if (visited.Contains(taskId))
                return;

            if (visiting.Contains(taskId))
            {
                AddCycle(taskId);
                return;
            }

            visiting.Add(taskId);
            stack.Add(taskId);

            foreach (var dependencyValue in tasksById[taskId].DependencyIds)
            {
                if (!TaskDependencyReference.TryParse(dependencyValue, out var dependency, out _) ||
                    !dependency!.IsLocalTo(activeProjectId))
                    continue;

                var dependencyId = dependency.TaskId;
                if (!tasksById.ContainsKey(dependencyId) ||
                    string.Equals(taskId, dependencyId, StringComparison.Ordinal))
                    continue;

                Visit(dependencyId);
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(taskId);
            visited.Add(taskId);
        }

        void AddCycle(string repeatedTaskId)
        {
            var startIndex = stack.FindIndex(id => string.Equals(id, repeatedTaskId, StringComparison.Ordinal));
            if (startIndex < 0)
                return;

            var cycle = stack[startIndex..].Concat([repeatedTaskId]).ToList();
            var canonical = string.Join(">", cycle.Take(cycle.Count - 1).OrderBy(id => id, StringComparer.Ordinal));
            if (reported.Add(canonical))
                cycles.Add(cycle);
        }
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
