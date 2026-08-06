using PM.Project;
using PM.Tasks;
using YamlDotNet.Core;

namespace PM.Application;

internal sealed record MilestoneActivationProjectState(
    string OriginalYaml,
    ProjectConfig Config,
    IReadOnlyDictionary<string, TaskItem> TasksById,
    IReadOnlyDictionary<string, string> StateByTaskId,
    MilestoneActivationSnapshot Snapshot);

internal static class MilestoneActivationProjectStateReader
{
    public static AppResult<MilestoneActivationProjectState> Read(
        ProjectRoot projectRoot,
        MilestoneActivationResolver resolver,
        IProjectConfigPersistence persistence,
        string reloadFailureCode,
        string reloadFailureMessage)
    {
        if (!projectRoot.Exists || projectRoot.Config == null)
            return AppResult<MilestoneActivationProjectState>.Fail(
                "missing_project", "Project not found. Run pm init first.");

        try
        {
            if (!persistence.Reload())
                return AppResult<MilestoneActivationProjectState>.Fail(
                    reloadFailureCode,
                    reloadFailureMessage);

            var originalYaml = persistence.ReadText();
            var config = ProjectConfig.Deserialize(originalYaml);
            if (config.RequiresMilestoneSchemaMigration)
                return AppResult<MilestoneActivationProjectState>.Fail(
                    "milestone_schema_migration_required",
                    "Legacy milestone configuration must be migrated with pm doctor --fix before project settings can be changed.");

            var tasksById = projectRoot.GetAllTasks()
                .GroupBy(task => task.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var stateByTaskId = tasksById.Values.ToDictionary(
                task => task.Id,
                task => projectRoot.TryGetState(task, out var taskState) ? taskState : string.Empty,
                StringComparer.Ordinal);
            var snapshot = resolver.Resolve(config, tasksById, stateByTaskId);
            return AppResult<MilestoneActivationProjectState>.Ok(new MilestoneActivationProjectState(
                originalYaml,
                config,
                tasksById,
                stateByTaskId,
                snapshot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or YamlException)
        {
            return AppResult<MilestoneActivationProjectState>.Fail(
                "invalid_project", $"Project configuration could not be read: {exception.Message}");
        }
    }
}
