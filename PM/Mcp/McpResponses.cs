using PM.Application;

namespace PM.Mcp;

public sealed record McpToolResponse<T>(
    bool Success,
    string Summary,
    string? ErrorCode = null,
    string? Message = null,
    T? Data = default)
{
    public static McpToolResponse<T> Ok(string summary, T data) => new(true, summary, Data: data);

    public static McpToolResponse<T> Fail(string errorCode, string message) =>
        new(false, message, errorCode, message);

    public static McpToolResponse<T> FromFailure(AppResult result) =>
        Fail(result.ErrorCode ?? "unknown_error", result.Message ?? "Operation failed.");

    public static McpToolResponse<T> FromFailure<TPayload>(AppResult<TPayload> result) =>
        Fail(result.ErrorCode ?? "unknown_error", result.Message ?? "Operation failed.");
}

public sealed record ProjectPayload(
    string Name,
    string RootPath,
    IReadOnlyList<OptionPayload> States,
    IReadOnlyList<OptionPayload> Tracks,
    IReadOnlyList<MilestonePayload> Milestones,
    string? ProjectId = null,
    string? RecoveryKey = null);

public sealed record OptionPayload(string Key, string Name);

public sealed record MilestonePayload(string Key, string Name, string Priority);

public sealed record LocalIdentityPayload(string UserId, string DisplayName, string PublicKey, string Fingerprint);

public sealed record ProjectMemberPayload(
    string UserId,
    string DisplayName,
    string PublicKey,
    string Fingerprint,
    string Role,
    bool IsLocal);

public sealed record ProjectMembersPayload(
    string ProjectId,
    string CurrentUserId,
    string CurrentRole,
    bool Authenticated,
    IReadOnlyList<ProjectMemberPayload> Members);

public sealed record ProjectInvitationPayload(
    string InvitationId,
    string Role,
    string CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ProjectInvitationsPayload(IReadOnlyList<ProjectInvitationPayload> Invitations);

public sealed record CreatedProjectInvitationPayload(ProjectInvitationPayload Invitation, string Token);

public sealed record TaskListPayload(
    IReadOnlyList<TaskSummaryPayload> Tasks,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null,
    bool Truncated = false);

public sealed record TaskSearchPayload(
    IReadOnlyList<TaskSearchResultPayload> Tasks,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null);

public sealed record NextTaskPayload(
    bool Found,
    TaskSummaryPayload? Task,
    string Reason,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null);

public sealed record TaskSummaryPayload(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string State,
    IReadOnlyList<string> DependsOn,
    bool DependenciesReady,
    string DependencySummary,
    IReadOnlyList<string> WaitingOnDependencies,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> CompletedDependencies,
    IReadOnlyList<string> UnavailableDependencies,
    IReadOnlyList<string> InvalidDependencies,
    string DescriptionPreview,
    string FilePath,
    LinkedProjectOwnerPayload? Project = null);

public sealed record TaskSearchResultPayload(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    string State,
    IReadOnlyList<string> DependsOn,
    bool DependenciesReady,
    string DependencySummary,
    IReadOnlyList<string> WaitingOnDependencies,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> CompletedDependencies,
    IReadOnlyList<string> UnavailableDependencies,
    IReadOnlyList<string> InvalidDependencies,
    string DescriptionPreview,
    string FilePath,
    int MatchCount,
    string Snippet,
    LinkedProjectOwnerPayload? Project = null);

public sealed record TaskDetailPayload(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string Priority,
    string PrioritySource,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string State,
    IReadOnlyList<string> DependsOn,
    bool DependenciesReady,
    string DependencySummary,
    IReadOnlyList<string> WaitingOnDependencies,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> CompletedDependencies,
    IReadOnlyList<string> UnavailableDependencies,
    IReadOnlyList<string> InvalidDependencies,
    string FilePath,
    string Markdown,
    string Description,
    LinkedProjectOwnerPayload? Project = null,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null);

public sealed record ProjectMutationReceiptPayload(string ProjectId, IReadOnlyList<string> ChangedPaths);

public sealed record CreatedTaskPayload(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string FilePath,
    ProjectMutationReceiptPayload? Mutation = null);

public sealed record MutatedPayload(bool Changed, ProjectMutationReceiptPayload? Mutation = null);

public sealed record TaskMutationPayload(
    bool Changed,
    TaskDetailPayload Task,
    ProjectMutationReceiptPayload? Mutation = null);

public sealed record TaskReorderPayload(
    string Track,
    string State,
    string? Milestone,
    IReadOnlyList<string> TaskIds,
    bool Changed,
    ProjectMutationReceiptPayload? Mutation = null);

public sealed record BulkTaskInputPayload(string Title, string? Description = null);

public sealed record BulkCreatedTaskPayload(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string FilePath);

public sealed record BulkFailurePayload(string ErrorCode, string Message);

public sealed record BulkCreatedTasksPayload(
    string Track,
    IReadOnlyList<BulkCreatedTaskPayload> Tasks,
    int RequestedCount,
    int CreatedCount,
    BulkFailurePayload? Failure,
    ProjectMutationReceiptPayload? Mutation = null);

public sealed record BulkMilestoneAssignmentPayload(
    string Milestone,
    IReadOnlyList<string> TaskIds,
    IReadOnlyList<string> FilePaths,
    int RequestedCount,
    int UpdatedCount,
    ProjectMutationReceiptPayload? Mutation = null);

public sealed record WikiPageListPayload(
    IReadOnlyList<WikiPageSummaryPayload> Pages,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null,
    bool Truncated = false);

public sealed record WikiPageSummaryPayload(
    string Path,
    string Title,
    DateTime ModifiedAt,
    string FilePath,
    LinkedProjectOwnerPayload? Project = null);

public sealed record WikiPagePayload(
    string Path,
    string Title,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string FilePath,
    string Markdown,
    string Body,
    LinkedProjectOwnerPayload? Project = null,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null,
    ProjectMutationReceiptPayload? Mutation = null);

public sealed record WikiPageOutlinePayload(
    string Path,
    string Title,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string FilePath,
    string Version,
    IReadOnlyList<WikiHeadingOutlinePayload> Headings,
    LinkedProjectOwnerPayload? Project = null,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null);

public sealed record WikiHeadingOutlinePayload(
    string Id,
    int Level,
    string Title,
    IReadOnlyList<string> Breadcrumb,
    string Preview);

public sealed record WikiPagePatchPayload(
    WikiPagePayload Page,
    string Version,
    ProjectMutationReceiptPayload? Mutation = null);

public sealed record WikiSearchPayload(
    IReadOnlyList<WikiSearchResultPayload> Pages,
    IReadOnlyList<LinkedProjectWarningPayload>? Warnings = null);

public sealed record WikiSearchResultPayload(
    string Path,
    string Title,
    DateTime ModifiedAt,
    string FilePath,
    int MatchCount,
    string Snippet,
    LinkedProjectOwnerPayload? Project = null);

public sealed record ProjectValidationPayload(
    bool Valid,
    IReadOnlyList<ProjectValidationIssuePayload> Issues);

public sealed record ProjectValidationIssuePayload(
    string Severity,
    string Code,
    string Message,
    string? Path,
    string? TaskId,
    string? WikiPath,
    string? State,
    string? ProjectId,
    string? ProjectAlias);

public sealed record LinkedProjectMemberPayload(
    string ProjectId,
    string Name,
    string? Alias,
    string Relationship,
    string Status,
    string Source,
    bool Readable,
    bool WriteTrusted);

public sealed record LinkedProjectWarningPayload(
    string Code,
    string Message,
    string DeclaringProjectId,
    string TargetProjectId,
    string? Alias,
    string Status,
    string? RepairCommand);

public sealed record LinkedProjectOwnerPayload(
    string ProjectId,
    string Name,
    string? Alias,
    string Relationship,
    string? Revision,
    bool? Dirty);

public sealed record LinkedProjectFamilyPayload(
    string ActiveProjectId,
    IReadOnlyList<LinkedProjectMemberPayload> Members,
    IReadOnlyList<LinkedProjectWarningPayload> Warnings);
