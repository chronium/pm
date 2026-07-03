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
    IReadOnlyList<OptionPayload> Milestones);

public sealed record OptionPayload(string Key, string Name);

public sealed record TaskListPayload(IReadOnlyList<TaskSummaryPayload> Tasks);

public sealed record TaskSummaryPayload(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    string State,
    string DescriptionPreview,
    string FilePath);

public sealed record TaskDetailPayload(
    string Id,
    string Title,
    string Track,
    string? Milestone,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string State,
    string FilePath,
    string Markdown,
    string Description);

public sealed record CreatedTaskPayload(string Id, string Title, string Track, string? Milestone, string FilePath);

public sealed record MutatedPayload(bool Changed);
