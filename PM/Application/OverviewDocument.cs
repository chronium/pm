namespace PM.Application;

public enum OverviewDocumentStatus
{
    Disabled,
    Ready,
    Invalid,
}

public sealed record OverviewDocument(
    OverviewDocumentStatus Status,
    string? ProjectId,
    string ProjectName,
    string DocumentTitle,
    OverviewComposition? Composition,
    IReadOnlyList<OverviewIssue> Issues,
    string Revision);

public sealed record OverviewIssue(string Code, string Message, string Path);

public abstract record OverviewComposition(string Layout);

public sealed record SingleOverviewComposition(IReadOnlyList<OverviewSection> Sections)
    : OverviewComposition("single");

public sealed record SplitOverviewComposition(
    IReadOnlyList<OverviewContentSection> Primary,
    IReadOnlyList<OverviewContentSection> Secondary,
    IReadOnlyList<OverviewSection> After)
    : OverviewComposition("split");

public abstract record OverviewSection(string Type);

public abstract record OverviewContentSection(string Type) : OverviewSection(Type);

public sealed record HeroOverviewSection(string Title, string? Description)
    : OverviewContentSection("hero");

public sealed record MilestoneOverviewSection(string Title, OverviewMilestone? Milestone)
    : OverviewContentSection("milestone");

public sealed record TasksOverviewSection(string Title, IReadOnlyList<OverviewTask> Tasks)
    : OverviewContentSection("tasks");

public sealed record WikiOverviewSection(string Title, IReadOnlyList<OverviewWikiPage> Pages)
    : OverviewContentSection("wiki");

public sealed record MarkdownOverviewSection(string Title, string SourcePath, string Body)
    : OverviewContentSection("markdown");

public sealed record CopyrightOverviewSection(string Notice)
    : OverviewSection("copyright");

public sealed record OverviewMilestone(
    string Key,
    string Title,
    string Description,
    string Priority,
    MilestoneLifecycle Lifecycle,
    int AssignedTaskCount,
    int DoneTaskCount,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers);

public sealed record OverviewTask(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string State,
    DependencyStatus Dependencies,
    TaskActivationEligibility Activation,
    string DescriptionPreview,
    DateTime ModifiedAt);

public sealed record OverviewWikiPage(string Path, string Title, DateTime ModifiedAt);
