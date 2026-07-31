using PM.Project;

namespace PM.Tasks;

public sealed record TaskDependencyReference
{
    private TaskDependencyReference(string persistedValue, string taskId, string? projectId)
    {
        PersistedValue = persistedValue;
        TaskId = taskId;
        ProjectId = projectId;
    }

    public string PersistedValue { get; }
    public string TaskId { get; }
    public string? ProjectId { get; }
    public bool IsQualified => ProjectId != null;

    public bool IsLocalTo(string? activeProjectId) =>
        !IsQualified ||
        activeProjectId != null && string.Equals(ProjectId, activeProjectId, StringComparison.Ordinal);

    public string ToPersistedValue(string? activeProjectId) =>
        IsLocalTo(activeProjectId) ? TaskId : PersistedValue;

    public static bool TryParse(
        string? value,
        out TaskDependencyReference? dependency,
        out string message)
    {
        dependency = null;
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            message = "Task dependency is required.";
            return false;
        }

        if (!ProjectResourceReference.LooksLikeReference(normalized))
        {
            dependency = new TaskDependencyReference(normalized, normalized, null);
            message = string.Empty;
            return true;
        }

        if (!ProjectResourceReference.TryParse(normalized, out var reference, out message))
            return false;
        if (reference!.Kind != ProjectResourceKind.Task)
        {
            message = "Task dependencies must reference a task resource.";
            return false;
        }

        dependency = new TaskDependencyReference(reference.ToCanonicalUri(), reference.ResourcePath,
            reference.ProjectId);
        message = string.Empty;
        return true;
    }
}
