namespace PM.Application;

public sealed record TaskSearchQuery(
    string FreeText,
    IReadOnlyList<string> States,
    IReadOnlyList<string> Ids,
    IReadOnlyList<string> Tracks,
    IReadOnlyList<string> Milestones,
    TaskSearchScope Scope,
    bool HasScopePredicate)
{
    public bool HasFreeText => !string.IsNullOrWhiteSpace(FreeText);
    public bool HasFilters => States.Count > 0 || Ids.Count > 0 || Tracks.Count > 0 || Milestones.Count > 0 ||
                              HasScopePredicate;
}

public enum TaskSearchScope
{
    Selection,
    All,
}

public sealed record TaskSearchContext(
    string? Track = null,
    string? Milestone = null,
    string? State = null,
    bool IncludeDelivered = false);

public static class TaskSearchQueryParser
{
    private static readonly HashSet<string> Fields =
        new(["state", "id", "track", "milestone", "in"], StringComparer.OrdinalIgnoreCase);

    public static AppResult<TaskSearchQuery> Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return AppResult<TaskSearchQuery>.Fail("invalid_task_query", "Task search query is required.");

        var states = new List<string>();
        var ids = new List<string>();
        var tracks = new List<string>();
        var milestones = new List<string>();
        TaskSearchScope? scope = null;
        var freeText = new List<string>();
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var colon = token.IndexOf(':');
            if (colon < 0 || !Fields.Contains(token[..colon]))
            {
                freeText.Add(token);
                continue;
            }

            var field = token[..colon].ToLowerInvariant();
            var value = token[(colon + 1)..];
            if (value.Length == 0)
            {
                if (index + 1 >= tokens.Length || IsRecognizedFieldToken(tokens[index + 1]))
                    return Invalid(field);
                value = tokens[++index];
            }

            if (string.IsNullOrWhiteSpace(value)) return Invalid(field);
            switch (field)
            {
                case "state": states.Add(value); break;
                case "id": ids.Add(value); break;
                case "track": tracks.Add(value); break;
                case "milestone": milestones.Add(value); break;
                case "in":
                    if (scope != null) return InvalidSingleValue(field);
                    scope = value.ToLowerInvariant() switch
                    {
                        "selection" => TaskSearchScope.Selection,
                        "all" => TaskSearchScope.All,
                        _ => null,
                    };
                    if (scope == null) return InvalidScope(value);
                    break;
            }
        }

        var parsed = new TaskSearchQuery(string.Join(' ', freeText), states, ids, tracks, milestones,
            scope ?? TaskSearchScope.Selection, scope != null);
        return parsed.HasFreeText || parsed.HasFilters
            ? AppResult<TaskSearchQuery>.Ok(parsed)
            : AppResult<TaskSearchQuery>.Fail("invalid_task_query", "Task search query is required.");
    }

    private static bool IsRecognizedFieldToken(string token)
    {
        var colon = token.IndexOf(':');
        return colon >= 0 && Fields.Contains(token[..colon]);
    }

    private static AppResult<TaskSearchQuery> Invalid(string field) =>
        AppResult<TaskSearchQuery>.Fail("invalid_task_query", $"Task search field {field}: requires a value.");

    private static AppResult<TaskSearchQuery> InvalidSingleValue(string field) =>
        AppResult<TaskSearchQuery>.Fail("invalid_task_query", $"Task search field {field}: may only be specified once.");

    private static AppResult<TaskSearchQuery> InvalidScope(string value) =>
        AppResult<TaskSearchQuery>.Fail("invalid_task_query",
            $"Task search field in: does not support value {value}. Use selection or all.");
}
