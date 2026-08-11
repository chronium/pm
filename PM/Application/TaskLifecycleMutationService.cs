using PM.Files;
using PM.Project;
using PM.Tasks;
using YamlDotNet.Core;

namespace PM.Application;

public sealed class TaskLifecycleMutationService(
    ProjectRoot projectRoot,
    MilestoneActivationResolver resolver,
    AutomaticActivationService automaticActivations,
    IProjectConfigPersistence persistence,
    ReleaseVersionService releaseVersions)
{
    public TaskLifecycleMutationService(
        ProjectRoot projectRoot,
        MilestoneActivationResolver resolver,
        AutomaticActivationService automaticActivations,
        IProjectConfigPersistence persistence)
        : this(projectRoot, resolver, automaticActivations, persistence,
            new ReleaseVersionService(projectRoot))
    {
    }

    public AppResult<TaskLifecycleMutationImpact> Execute(
        TaskItem originalTask,
        TaskItem prospectiveTask,
        string currentState,
        string targetState,
        Action primaryMutation,
        string primaryFailureCode,
        string primaryFailureMessage)
    {
        var completion = !string.Equals(currentState, "done", StringComparison.Ordinal) &&
                         string.Equals(targetState, "done", StringComparison.Ordinal);
        if (!string.Equals(currentState, targetState, StringComparison.Ordinal))
        {
            var ready = releaseVersions.EnsureMutationReady();
            if (!ready.Success)
                return AppResult<TaskLifecycleMutationImpact>.Fail(ready.ErrorCode!, ready.Message!);
        }
        var releasePlanResult = completion
            ? releaseVersions.PrepareTaskCompletion(prospectiveTask.Id)
            : AppResult<ReleaseTransitionPlan?>.Ok(null);
        if (!releasePlanResult.Success)
            return AppResult<TaskLifecycleMutationImpact>.Fail(
                releasePlanResult.ErrorCode!, releasePlanResult.Message!);
        var releasePlan = releasePlanResult.Payload;
        var hasAffectedTrigger = completion && projectRoot.Config!.ActivationTriggers.Any(entry =>
            entry.Value.Activation == null &&
            (entry.Value.Requirements ?? []).Any(requirement =>
                requirement.Kind == ActivationRequirementKind.Task &&
                string.Equals(requirement.Source, prospectiveTask.Id, StringComparison.Ordinal)));
        var stateResult = hasAffectedTrigger ? ReadState() : null;
        if (stateResult is { Success: false })
            return AppResult<TaskLifecycleMutationImpact>.Fail(stateResult.ErrorCode!, stateResult.Message!);

        var impact = AutomaticActivationImpact.None;
        ProjectConfig? prospectiveConfig = null;
        if (stateResult?.Payload is { } state)
        {
            prospectiveConfig = ProjectConfig.Deserialize(state.OriginalYaml);
            var prospectiveTasks = state.TasksById.ToDictionary(entry => entry.Key, entry => entry.Value,
                StringComparer.Ordinal);
            prospectiveTasks[prospectiveTask.Id] = prospectiveTask;
            var prospectiveStates = state.StateByTaskId.ToDictionary(entry => entry.Key, entry => entry.Value,
                StringComparer.Ordinal);
            prospectiveStates[prospectiveTask.Id] = targetState;
            impact = automaticActivations.ApplyAffected(
                prospectiveConfig,
                prospectiveTasks,
                prospectiveStates,
                state.Snapshot,
                ActivationRequirementKind.Task,
                prospectiveTask.Id);
        }

        var snapshots = CaptureMutationFiles(originalTask, prospectiveTask, currentState, targetState);
        if (releasePlan != null)
        {
            var begin = releaseVersions.Begin(releasePlan);
            if (!begin.Success)
                return AppResult<TaskLifecycleMutationImpact>.Fail(begin.ErrorCode!, begin.Message!);
        }
        try
        {
            primaryMutation();
        }
        catch (Exception exception) when (IsMutationException(exception))
        {
            return RestoreOrFail(
                snapshots,
                releasePlan,
                primaryFailureCode,
                primaryFailureMessage,
                "task_mutation_rollback_failed");
        }

        if (impact.ActivatedTriggers.Count > 0 && prospectiveConfig != null && !GlobalConfig.DryRun)
        {
            try
            {
                persistence.WriteTextAtomic(YamlSerde.Serialize(prospectiveConfig));
                if (!persistence.Reload())
                    throw new InvalidDataException("The project configuration could not be reloaded.");
            }
            catch (Exception exception) when (IsMutationException(exception) ||
                                               exception is InvalidDataException or YamlException)
            {
                return RestoreOrFail(
                    snapshots,
                    releasePlan,
                    "task_lifecycle_transition_failed",
                    $"Task {prospectiveTask.Id} was not completed because automatic activation could not be persisted: {exception.Message}",
                    "task_lifecycle_transition_rollback_failed");
            }
        }

        ReleaseVersionTransition? releaseTransition = null;
        if (releasePlan != null)
        {
            var completed = releaseVersions.Complete(releasePlan);
            if (!completed.Success)
                return RestoreOrFail(
                    snapshots,
                    releasePlan,
                    "task_release_transition_failed",
                    $"Task {prospectiveTask.Id} was not completed because its release version could not advance: {completed.Message}",
                    "task_release_transition_rollback_failed");
            releaseTransition = completed.Payload;
        }

        return AppResult<TaskLifecycleMutationImpact>.Ok(new(impact, releaseTransition));
    }

    private AppResult<MilestoneActivationProjectState> ReadState() =>
        MilestoneActivationProjectStateReader.Read(
            projectRoot,
            resolver,
            persistence,
            "task_lifecycle_config_reload_failed",
            "Task lifecycle configuration could not be reloaded.");

    private IReadOnlyList<FileSnapshot> CaptureMutationFiles(
        TaskItem originalTask,
        TaskItem prospectiveTask,
        string currentState,
        string targetState) =>
    [
        FileSnapshot.Capture(projectRoot.ConfigPath),
        FileSnapshot.Capture(projectRoot.TaskOrderPath),
        FileSnapshot.Capture(projectRoot.GetTaskFilePath(originalTask.Id)),
        FileSnapshot.Capture(Path.Combine(projectRoot.StatesPath, currentState, $"{originalTask.Id}.ref")),
        FileSnapshot.Capture(Path.Combine(projectRoot.StatesPath, targetState, $"{prospectiveTask.Id}.ref")),
    ];

    private AppResult<TaskLifecycleMutationImpact> RestoreOrFail(
        IReadOnlyList<FileSnapshot> snapshots,
        ReleaseTransitionPlan? releasePlan,
        string failureCode,
        string failureMessage,
        string rollbackFailureCode)
    {
        var restored = true;
        foreach (var snapshot in snapshots.Reverse())
            restored &= snapshot.TryRestore();

        if (releasePlan != null)
            restored &= releaseVersions.Rollback(releasePlan).Success;

        try
        {
            restored &= persistence.Reload();
        }
        catch (Exception exception) when (IsMutationException(exception))
        {
            restored = false;
        }

        return restored
            ? AppResult<TaskLifecycleMutationImpact>.Fail(failureCode, failureMessage)
            : AppResult<TaskLifecycleMutationImpact>.Fail(
                rollbackFailureCode,
                $"{failureMessage} The previous project state could not be fully restored.");
    }

    private static bool IsMutationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException;

    private sealed record FileSnapshot(string Path, bool Existed, string? Content)
    {
        public static FileSnapshot Capture(string path) =>
            FileSystem.FileExists(path)
                ? new FileSnapshot(path, true, FileSystem.ReadAllText(path))
                : new FileSnapshot(path, false, null);

        public bool TryRestore()
        {
            try
            {
                if (Existed)
                {
                    var directory = System.IO.Path.GetDirectoryName(Path);
                    if (directory != null) FileSystem.CreateDirectory(directory);
                    FileSystem.WriteAllTextAtomic(Path, Content ?? string.Empty);
                }
                else if (FileSystem.FileExists(Path))
                {
                    FileSystem.DeleteFile(Path);
                }

                return true;
            }
            catch (Exception exception) when (IsMutationException(exception))
            {
                return false;
            }
        }
    }
}
