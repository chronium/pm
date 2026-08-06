using PM.Project;
using PM.Tasks;

namespace PM.Application;

public enum MilestoneActivationGraphNodeKind
{
    Milestone,
    Trigger,
}

public readonly record struct MilestoneActivationGraphNode(
    MilestoneActivationGraphNodeKind Kind,
    string Key)
{
    public override string ToString() => Kind switch
    {
        MilestoneActivationGraphNodeKind.Milestone => $"milestone:{Key}",
        MilestoneActivationGraphNodeKind.Trigger => $"trigger:{Key}",
        _ => $"unknown:{Key}",
    };
}

public sealed record MilestoneActivationCycle(
    IReadOnlyList<MilestoneActivationGraphNode> Path);

public sealed record MilestoneActivationGraph(
    IReadOnlyDictionary<MilestoneActivationGraphNode, IReadOnlyList<MilestoneActivationGraphNode>> Edges,
    IReadOnlyList<MilestoneActivationCycle> Cycles);

public sealed class MilestoneActivationGraphService
{
    public MilestoneActivationGraph Build(
        ProjectConfig config,
        IReadOnlyDictionary<string, TaskItem> tasksById)
    {
        var mutableEdges = new Dictionary<MilestoneActivationGraphNode, HashSet<MilestoneActivationGraphNode>>();
        foreach (var milestoneKey in config.Milestones.Keys)
            mutableEdges[Milestone(milestoneKey)] = [];
        foreach (var triggerKey in config.ActivationTriggers.Keys)
            mutableEdges[Trigger(triggerKey)] = [];

        foreach (var (milestoneKey, milestone) in config.Milestones)
        foreach (var triggerKey in milestone.RequiredActivationTriggers ?? [])
        {
            if (config.ActivationTriggers.ContainsKey(triggerKey))
                mutableEdges[Milestone(milestoneKey)].Add(Trigger(triggerKey));
        }

        foreach (var (triggerKey, trigger) in config.ActivationTriggers)
        foreach (var requirement in trigger.Requirements ?? [])
        {
            var targetMilestone = requirement.Kind switch
            {
                ActivationRequirementKind.Milestone when config.Milestones.ContainsKey(requirement.Source) =>
                    requirement.Source,
                ActivationRequirementKind.Task when tasksById.TryGetValue(requirement.Source, out var task) &&
                                                    !string.IsNullOrWhiteSpace(task.Milestone) &&
                                                    config.Milestones.ContainsKey(task.Milestone) =>
                    task.Milestone,
                _ => null,
            };

            if (targetMilestone != null)
                mutableEdges[Trigger(triggerKey)].Add(Milestone(targetMilestone));
        }

        var edges = mutableEdges.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<MilestoneActivationGraphNode>)entry.Value
                .OrderBy(Format, StringComparer.Ordinal)
                .ToList());
        return new MilestoneActivationGraph(edges, FindCycles(edges));
    }

    private static IReadOnlyList<MilestoneActivationCycle> FindCycles(
        IReadOnlyDictionary<MilestoneActivationGraphNode, IReadOnlyList<MilestoneActivationGraphNode>> edges)
    {
        var visiting = new HashSet<MilestoneActivationGraphNode>();
        var visited = new HashSet<MilestoneActivationGraphNode>();
        var stack = new List<MilestoneActivationGraphNode>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var cycles = new List<MilestoneActivationCycle>();

        foreach (var node in edges.Keys.OrderBy(Format, StringComparer.Ordinal))
            Visit(node);

        return cycles;

        void Visit(MilestoneActivationGraphNode node)
        {
            if (visited.Contains(node))
                return;

            if (visiting.Contains(node))
            {
                AddCycle(node);
                return;
            }

            visiting.Add(node);
            stack.Add(node);

            if (edges.TryGetValue(node, out var targets))
                foreach (var target in targets)
                    Visit(target);

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
            visited.Add(node);
        }

        void AddCycle(MilestoneActivationGraphNode repeatedNode)
        {
            var startIndex = stack.FindIndex(node => node == repeatedNode);
            if (startIndex < 0)
                return;

            var path = stack[startIndex..].Concat([repeatedNode]).ToList();
            var canonical = string.Join(
                ">",
                path.Take(path.Count - 1).Select(Format).OrderBy(value => value, StringComparer.Ordinal));
            if (reported.Add(canonical))
                cycles.Add(new MilestoneActivationCycle(path));
        }
    }

    private static MilestoneActivationGraphNode Milestone(string key) =>
        new(MilestoneActivationGraphNodeKind.Milestone, key);

    private static MilestoneActivationGraphNode Trigger(string key) =>
        new(MilestoneActivationGraphNodeKind.Trigger, key);

    private static string Format(MilestoneActivationGraphNode node) => node.ToString();
}
