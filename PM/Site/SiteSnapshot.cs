using PM.Api;

namespace PM.Site;

public sealed record SiteSnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    string? ProjectId,
    IReadOnlyList<SiteLinkedProjectResponse> LinkedProjects,
    ProjectResponse Project,
    SettingsResponse Settings,
    ActivationSwitchboardResponse Activation,
    BoardNavigationResponse Navigation,
    BoardResponse Board,
    IReadOnlyList<SiteTaskResponse> Tasks,
    IReadOnlyList<WikiPageSummaryResponse> WikiIndex,
    IReadOnlyList<SiteWikiPageResponse> WikiPages);

public sealed record SiteLinkedProjectResponse(
    string ProjectId,
    string Name,
    string? Alias,
    string Relationship,
    string? PublicSiteUrl);

public sealed record SiteTaskResponse(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string PrioritySelection,
    string State,
    DependencyStatusResponse Dependencies,
    TaskActivationEligibilityResponse Activation,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string Description,
    string Revision);

public sealed record SiteWikiPageResponse(
    string Path,
    string Title,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string Body,
    string Revision);
