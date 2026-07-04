using PM;
using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed record ProjectCreationRequest(
    string Name,
    int? IdWidth = null,
    string? IdPrefix = null,
    string? NextIdServiceUrl = null,
    Dictionary<string, string>? States = null,
    Dictionary<string, string>? Tracks = null,
    Dictionary<string, string>? Milestones = null);

public sealed record ProjectCreationResult(
    string Name,
    string RootPath,
    IReadOnlyDictionary<string, string> States,
    IReadOnlyDictionary<string, string> Tracks,
    IReadOnlyDictionary<string, string> Milestones);

public sealed class ProjectCreationService(ProjectRoot projectRoot, INextIdService nextIdService)
{
    public async Task<AppResult<ProjectCreationResult>> CreateProject(
        ProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (projectRoot.Exists)
            return AppResult<ProjectCreationResult>.Fail("project_exists",
                "A project is already initialized in this directory or a parent directory.");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return AppResult<ProjectCreationResult>.Fail("invalid_project", "Project name is required.");

        var idPrefix = string.IsNullOrWhiteSpace(request.IdPrefix) ? "TASK" : request.IdPrefix.Trim();
        var idWidth = request.IdWidth ?? 4;
        if (idWidth < 1)
            return AppResult<ProjectCreationResult>.Fail("invalid_project", "Project ID width must be greater than zero.");

        var states = NormalizeOptions(request.States ?? GlobalConfig.DefaultTaskStates);
        if (states.Count == 0)
            return AppResult<ProjectCreationResult>.Fail("invalid_states", "At least one task state is required.");

        var tracks = NormalizeOptions(request.Tracks ?? new Dictionary<string, string> { [idPrefix] = idPrefix });
        if (tracks.Count == 0)
            return AppResult<ProjectCreationResult>.Fail("invalid_tracks", "At least one track is required.");

        var milestones = NormalizeOptions(request.Milestones ?? new Dictionary<string, string>());
        var config = new ProjectConfig
        {
            Name = name,
            IdWidth = idWidth,
            IdPrefix = idPrefix,
            NextIdServiceUrl = string.IsNullOrWhiteSpace(request.NextIdServiceUrl)
                ? ProjectConfig.DefaultNextIdServiceUrl
                : request.NextIdServiceUrl.Trim(),
            TaskStates = states,
            Tracks = tracks,
            Milestones = milestones,
        };

        if (!await nextIdService.Healthy(config, cancellationToken))
            return AppResult<ProjectCreationResult>.Fail("next_id_unavailable", "Unable to reach the next ID service.");

        await projectRoot.CreateProject(config, cancellationToken);
        return AppResult<ProjectCreationResult>.Ok(new ProjectCreationResult(
            config.Name,
            projectRoot.RootPath,
            config.TaskStates,
            config.Tracks,
            config.Milestones));
    }

    private static Dictionary<string, string> NormalizeOptions(Dictionary<string, string> options)
    {
        var normalized = new Dictionary<string, string>();
        foreach (var (key, value) in options)
        {
            var normalizedKey = key.Trim();
            var normalizedValue = value.Trim();
            if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(normalizedValue))
                continue;

            normalized[normalizedKey] = normalizedValue;
        }

        return normalized;
    }
}
