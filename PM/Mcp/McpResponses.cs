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

public sealed record TaskListPayload(IReadOnlyList<TaskSummaryPayload> Tasks);

public sealed record NextTaskPayload(bool Found, TaskSummaryPayload? Task, string Reason);

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
    string DescriptionPreview,
    string FilePath);

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
    string FilePath,
    string Markdown,
    string Description);

public sealed record CreatedTaskPayload(string Id, string Title, string Track, string? Milestone, string FilePath);

public sealed record MutatedPayload(bool Changed);

public sealed record TaskMutationPayload(bool Changed, TaskDetailPayload Task);

public sealed record TaskReorderPayload(
    string Track,
    string State,
    string? Milestone,
    IReadOnlyList<string> TaskIds,
    bool Changed);

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
    BulkFailurePayload? Failure);

public sealed record BulkMilestoneAssignmentPayload(
    string Milestone,
    IReadOnlyList<string> TaskIds,
    IReadOnlyList<string> FilePaths,
    int RequestedCount,
    int UpdatedCount);

public sealed record WikiPageListPayload(IReadOnlyList<WikiPageSummaryPayload> Pages);

public sealed record WikiPageSummaryPayload(
    string Path,
    string Title,
    DateTime ModifiedAt,
    string FilePath);

public sealed record WikiPagePayload(
    string Path,
    string Title,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string FilePath,
    string Markdown,
    string Body);

public sealed record WikiSearchPayload(IReadOnlyList<WikiSearchResultPayload> Pages);

public sealed record WikiSearchResultPayload(
    string Path,
    string Title,
    DateTime ModifiedAt,
    string FilePath,
    int MatchCount,
    string Snippet);

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
    string? State);
