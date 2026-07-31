using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed class LinkedProjectTaskGraph
{
    private readonly IReadOnlyDictionary<string, ProjectTasks> projects;

    internal LinkedProjectTaskGraph(
        LinkedProjectFamily family,
        IReadOnlyDictionary<string, ProjectTasks> projects,
        IReadOnlyList<LinkedProjectFamilyWarning> warnings)
    {
        Family = family;
        this.projects = projects;
        Warnings = warnings;
    }

    public LinkedProjectFamily Family { get; }
    public IReadOnlyList<LinkedProjectFamilyWarning> Warnings { get; }

    public DependencyStatus GetDependencyStatus(string owningProjectId, TaskItem task)
    {
        if (task.DependencyIds.Count == 0)
            return new DependencyStatus(true, [], [], [], [], [], [], "no dependencies");

        var completed = new List<string>();
        var waiting = new List<string>();
        var missing = new List<string>();
        var unavailable = new List<string>();
        var invalid = new List<string>();

        foreach (var value in task.DependencyIds)
        {
            if (!TryResolve(owningProjectId, value, out var target, out var status))
            {
                Add(status, value);
                continue;
            }

            if (string.Equals(target!.State, "done", StringComparison.Ordinal))
                completed.Add(value);
            else
                waiting.Add(value);
        }

        var ready = waiting.Count == 0 && missing.Count == 0 &&
                    unavailable.Count == 0 && invalid.Count == 0;
        return new DependencyStatus(
            ready,
            task.DependencyIds.ToList(),
            completed,
            waiting,
            missing,
            unavailable,
            invalid,
            ready
                ? "all dependencies complete"
                : BoardService.BuildWaitingSummary(waiting, missing, unavailable, invalid));

        void Add(DependencyResolution status, string value)
        {
            switch (status)
            {
                case DependencyResolution.Missing:
                    missing.Add(value);
                    break;
                case DependencyResolution.Unavailable:
                    unavailable.Add(value);
                    break;
                default:
                    invalid.Add(value);
                    break;
            }
        }
    }

    private bool TryResolve(
        string owningProjectId,
        string value,
        out GraphTask? target,
        out DependencyResolution status)
    {
        target = null;
        if (!TaskDependencyReference.TryParse(value, out var dependency, out _))
        {
            status = DependencyResolution.Invalid;
            return false;
        }

        var projectId = dependency!.ProjectId ?? owningProjectId;
        if (!projects.TryGetValue(projectId, out var project))
        {
            status = DependencyResolution.Unavailable;
            return false;
        }

        if (!project.Tasks.TryGetValue(dependency.TaskId, out target))
        {
            status = DependencyResolution.Missing;
            return false;
        }

        status = DependencyResolution.Available;
        return true;
    }

    internal IEnumerable<GraphTask> Tasks => projects.Values.SelectMany(project => project.Tasks.Values);

    internal sealed record ProjectTasks(
        LinkedProjectFamilyMember Member,
        IReadOnlyDictionary<string, GraphTask> Tasks);

    internal sealed record GraphTask(string ProjectId, TaskItem Task, string State);

    private enum DependencyResolution
    {
        Available,
        Missing,
        Unavailable,
        Invalid,
    }
}

public sealed class LinkedProjectTaskGraphService(LinkedProjectFamilyService familyService)
{
    public async Task<AppResult<LinkedProjectTaskGraph>> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        var family = await familyService.ResolveAsync(cancellationToken);
        return family.Success
            ? AppResult<LinkedProjectTaskGraph>.Ok(Build(family.Payload!))
            : AppResult<LinkedProjectTaskGraph>.Fail(family.ErrorCode!, family.Message!);
    }

    public LinkedProjectTaskGraph Build(LinkedProjectFamily family)
    {
        var projects = new Dictionary<string, LinkedProjectTaskGraph.ProjectTasks>(StringComparer.Ordinal);
        foreach (var member in family.Members.Where(member => member.Readable && member.Project != null))
        {
            var tasks = member.Project!.GetAllTasks()
                .GroupBy(task => task.Id, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var task = group.First();
                        var state = member.Project.TryGetState(task, out var currentState)
                            ? currentState
                            : string.Empty;
                        return new LinkedProjectTaskGraph.GraphTask(member.ProjectId, task, state);
                    },
                    StringComparer.Ordinal);
            projects[member.ProjectId] = new LinkedProjectTaskGraph.ProjectTasks(member, tasks);
        }

        var warnings = BuildWarnings(family, projects);
        return new LinkedProjectTaskGraph(family, projects, warnings);
    }

    private static IReadOnlyList<LinkedProjectFamilyWarning> BuildWarnings(
        LinkedProjectFamily family,
        IReadOnlyDictionary<string, LinkedProjectTaskGraph.ProjectTasks> projects)
    {
        var warnings = new List<LinkedProjectFamilyWarning>();
        var unavailable = new HashSet<(string Owner, string Target)>(new ProjectPairComparer());
        var edges = new Dictionary<TaskKey, List<TaskKey>>();

        foreach (var project in projects.Values)
        foreach (var task in project.Tasks.Values)
        {
            var source = new TaskKey(task.ProjectId, task.Task.Id);
            var targets = new List<TaskKey>();
            edges[source] = targets;
            foreach (var value in task.Task.DependencyIds)
            {
                if (!TaskDependencyReference.TryParse(value, out var dependency, out _)) continue;
                var targetProjectId = dependency!.ProjectId ?? task.ProjectId;
                if (!projects.TryGetValue(targetProjectId, out var targetProject))
                {
                    if (unavailable.Add((task.ProjectId, targetProjectId)))
                    {
                        var member = family.Members.FirstOrDefault(candidate =>
                            string.Equals(candidate.ProjectId, targetProjectId, StringComparison.Ordinal));
                        warnings.Add(new LinkedProjectFamilyWarning(
                            "dependency_graph_incomplete",
                            $"Dependencies from project {task.ProjectId} reference unavailable project {targetProjectId}.",
                            task.ProjectId,
                            targetProjectId,
                            member?.Alias,
                            member?.Status ?? LinkedProjectResolutionStatus.Missing));
                    }

                    continue;
                }

                if (targetProject.Tasks.ContainsKey(dependency.TaskId))
                    targets.Add(new TaskKey(targetProjectId, dependency.TaskId));
            }
        }

        foreach (var cycle in FindCrossProjectCycles(edges))
        {
            var first = cycle[0];
            warnings.Add(new LinkedProjectFamilyWarning(
                "cross_project_dependency_cycle",
                $"Cross-project task dependency cycle detected: {string.Join(" -> ", cycle.Select(Format))}.",
                first.ProjectId,
                first.ProjectId,
                family.Members.FirstOrDefault(member => member.ProjectId == first.ProjectId)?.Alias,
                LinkedProjectResolutionStatus.Invalid));
        }

        return warnings.Take(LinkedProjectFamilyService.MaximumWarningCount).ToList();
    }

    private static IReadOnlyList<IReadOnlyList<TaskKey>> FindCrossProjectCycles(
        IReadOnlyDictionary<TaskKey, List<TaskKey>> edges)
    {
        var visiting = new HashSet<TaskKey>();
        var visited = new HashSet<TaskKey>();
        var stack = new List<TaskKey>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new List<IReadOnlyList<TaskKey>>();

        foreach (var task in edges.Keys.OrderBy(Format, StringComparer.Ordinal)) Visit(task);
        return cycles;

        void Visit(TaskKey task)
        {
            if (visited.Contains(task)) return;
            if (visiting.Contains(task))
            {
                AddCycle(task);
                return;
            }

            visiting.Add(task);
            stack.Add(task);
            if (edges.TryGetValue(task, out var dependencies))
                foreach (var dependency in dependencies.OrderBy(Format, StringComparer.Ordinal)) Visit(dependency);
            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(task);
            visited.Add(task);
        }

        void AddCycle(TaskKey repeated)
        {
            var start = stack.FindIndex(item => item == repeated);
            if (start < 0) return;
            var cycle = stack[start..].Concat([repeated]).ToList();
            if (cycle.Select(item => item.ProjectId).Distinct(StringComparer.Ordinal).Count() < 2) return;
            var canonical = string.Join(">", cycle.Take(cycle.Count - 1)
                .Select(Format).OrderBy(value => value, StringComparer.Ordinal));
            if (reported.Add(canonical)) cycles.Add(cycle);
        }
    }

    private static string Format(TaskKey task) => $"pm://{task.ProjectId}/tasks/{task.TaskId}";

    private readonly record struct TaskKey(string ProjectId, string TaskId);

    private sealed class ProjectPairComparer : IEqualityComparer<(string Owner, string Target)>
    {
        public bool Equals((string Owner, string Target) x, (string Owner, string Target) y) =>
            string.Equals(x.Owner, y.Owner, StringComparison.Ordinal) &&
            string.Equals(x.Target, y.Target, StringComparison.Ordinal);

        public int GetHashCode((string Owner, string Target) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Owner),
                StringComparer.Ordinal.GetHashCode(obj.Target));
    }
}
