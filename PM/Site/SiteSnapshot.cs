using PM.Api;

namespace PM.Site;

public sealed record SiteSnapshot(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    ProjectResponse Project,
    SettingsResponse Settings,
    BoardNavigationResponse Navigation,
    BoardResponse Board,
    IReadOnlyList<SiteTaskResponse> Tasks,
    IReadOnlyList<WikiPageSummaryResponse> WikiIndex,
    IReadOnlyList<SiteWikiPageResponse> WikiPages);

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
