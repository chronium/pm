using PM.Tasks;

namespace PM.Web;

public sealed record BoardData(
    string ProjectName,
    IReadOnlyList<BoardOption> Tracks,
    IReadOnlyList<BoardOption> Milestones,
    IReadOnlyList<BoardOption> States,
    IReadOnlyList<BoardMilestoneGroup> MilestoneGroups,
    BoardQuery Query);

public sealed record BoardOption(string Key, string Name);

public sealed record BoardMilestoneGroup(string? Key, string Name, IReadOnlyList<BoardStateGroup> States);

public sealed record BoardStateGroup(string Key, string Name, IReadOnlyList<BoardTask> Tasks);

public sealed record BoardTask(
    TaskItem Task,
    string Track,
    string? Milestone,
    string State,
    string DescriptionPreview,
    string FilePath);

