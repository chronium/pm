namespace PM.Application;

public sealed record AppResult<T>(
    bool Success,
    string? ErrorCode = null,
    string? Message = null,
    T? Payload = default)
{
    public static AppResult<T> Ok(T payload) => new(true, Payload: payload);

    public static AppResult<T> Fail(string errorCode, string message) => new(false, errorCode, message);
}

public sealed record AppResult(
    bool Success,
    string? ErrorCode = null,
    string? Message = null)
{
    public static AppResult Ok() => new(true);

    public static AppResult Fail(string errorCode, string message) => new(false, errorCode, message);
}
