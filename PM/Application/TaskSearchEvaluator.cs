using PM.Tasks;

namespace PM.Application;

internal sealed record TaskSearchDocument(
    TaskItem Task,
    string Markdown,
    string Track,
    string State,
    string Priority);

internal sealed record TaskSearchField(string Label, string Value, bool IsFallback);

internal sealed record TaskSearchEvaluation(
    bool Matches,
    int MatchCount,
    IReadOnlyList<TaskSearchField> Fields);

internal static class TaskSearchEvaluator
{
    public static TaskSearchEvaluation Evaluate(
        TaskSearchDocument document,
        TaskSearchQuery query,
        TaskSearchContext context)
    {
        var fields = BuildFields(document);
        if (!MatchesFilters(document, query, context))
            return new TaskSearchEvaluation(false, 0, fields);

        var matchCount = query.HasFreeText ? CountSearchMatches(fields, query.FreeText) : 0;
        return new TaskSearchEvaluation(!query.HasFreeText || matchCount > 0, matchCount, fields);
    }

    private static bool MatchesFilters(
        TaskSearchDocument document,
        TaskSearchQuery query,
        TaskSearchContext context) =>
        MatchesAny(query.States, document.State) &&
        MatchesAny(query.Tracks, document.Track) &&
        MatchesAny(query.Milestones, document.Task.Milestone ?? string.Empty) &&
        MatchesAnyTaskId(query.Ids, document.Task.Id) &&
        MatchesContext(context.State, document.State) &&
        (query.Scope == TaskSearchScope.All ||
         MatchesContext(context.Track, document.Track) &&
         MatchesContext(context.Milestone, document.Task.Milestone ?? string.Empty));

    private static IReadOnlyList<TaskSearchField> BuildFields(TaskSearchDocument document) =>
    [
        new("Description", document.Task.Description, false),
        new("Title", document.Task.Title, false),
        new("ID", document.Task.Id, false),
        new("Track", document.Track, false),
        new("Milestone", document.Task.Milestone ?? string.Empty, false),
        new("State", document.State, false),
        new("Priority", document.Priority, false),
        new("Dependencies", string.Join(' ', document.Task.DependencyIds), false),
        new("Markdown", document.Markdown, true),
    ];

    private static bool MatchesAny(IReadOnlyList<string> values, string actual) =>
        values.Count == 0 || values.Any(value => actual.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesAnyTaskId(IReadOnlyList<string> values, string actual) =>
        values.Count == 0 || values.Any(value => MatchesTaskId(value, actual));

    private static bool MatchesTaskId(string value, string actual)
    {
        if (!value.All(char.IsDigit))
            return actual.StartsWith(value, StringComparison.OrdinalIgnoreCase);

        var suffixStart = actual.Length;
        while (suffixStart > 0 && char.IsDigit(actual[suffixStart - 1])) suffixStart--;
        if (suffixStart == actual.Length) return false;

        return NormalizeTaskNumber(actual[suffixStart..]) == NormalizeTaskNumber(value);
    }

    private static string NormalizeTaskNumber(string value)
    {
        var normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static bool MatchesContext(string? expected, string actual) =>
        expected == null || actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static int CountSearchMatches(IReadOnlyList<TaskSearchField> fields, string query)
    {
        var semanticMatchCount = fields
            .Where(field => !field.IsFallback)
            .Sum(field => CountMatches(field.Value, query));
        return semanticMatchCount > 0
            ? semanticMatchCount
            : fields.Where(field => field.IsFallback).Sum(field => CountMatches(field.Value, query));
    }

    private static int CountMatches(string value, string query)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = value.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return count;
            count++;
            index += query.Length;
        }
    }
}
