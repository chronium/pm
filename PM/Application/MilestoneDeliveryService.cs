using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PM.Project;
using PM.Tasks;
using YamlDotNet.Core;

namespace PM.Application;

public sealed record MilestoneDeliveryPreview(
    string MilestoneKey,
    string Title,
    string Revision,
    MilestoneDeliveryMode Mode,
    int AssignedTaskCount,
    int DoneTaskCount,
    IReadOnlyList<string> UnfinishedTaskIds,
    bool RequiresConfirmation);

public sealed class MilestoneDeliveryService
{
    private readonly ProjectRoot projectRoot;
    private readonly MilestoneActivationResolver resolver;
    private readonly MilestoneActivationValidationService validator;
    private readonly AutomaticActivationService automaticActivations;
    private readonly TimeProvider timeProvider;
    private readonly IProjectConfigPersistence persistence;

    public MilestoneDeliveryService(
        ProjectRoot projectRoot,
        MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validator,
        AutomaticActivationService automaticActivations,
        TimeProvider timeProvider,
        IProjectConfigPersistence persistence)
    {
        this.projectRoot = projectRoot;
        this.resolver = resolver;
        this.validator = validator;
        this.automaticActivations = automaticActivations;
        this.timeProvider = timeProvider;
        this.persistence = persistence;
    }

    public AppResult<MilestoneDeliveryPreview> PreviewDelivery(string key, string? reason)
    {
        var evaluation = EvaluateDelivery(key, reason);
        return evaluation.Success
            ? AppResult<MilestoneDeliveryPreview>.Ok(evaluation.Payload!.Preview)
            : AppResult<MilestoneDeliveryPreview>.Fail(evaluation.ErrorCode!, evaluation.Message!);
    }

    public AppResult<LifecycleMutationResult<ResolvedMilestone>> DeliverMilestone(
        string key,
        string? reason,
        string expectedRevision,
        bool allowExceptional)
    {
        if (string.IsNullOrWhiteSpace(expectedRevision))
            return AppResult<LifecycleMutationResult<ResolvedMilestone>>.Fail(
                "milestone_delivery_revision_required",
                "Milestone delivery requires a preview revision.");

        var evaluationResult = EvaluateDelivery(key, reason);
        if (!evaluationResult.Success)
            return AppResult<LifecycleMutationResult<ResolvedMilestone>>.Fail(
                evaluationResult.ErrorCode!, evaluationResult.Message!);

        var evaluation = evaluationResult.Payload!;
        if (!string.Equals(expectedRevision, evaluation.Preview.Revision, StringComparison.Ordinal))
            return AppResult<LifecycleMutationResult<ResolvedMilestone>>.Fail(
                "milestone_delivery_stale",
                "Milestone delivery conditions changed. Run the command again to review a fresh preview.");
        if (evaluation.Preview.RequiresConfirmation && !allowExceptional)
            return AppResult<LifecycleMutationResult<ResolvedMilestone>>.Fail(
                "milestone_delivery_confirmation_required",
                "Exceptional milestone delivery requires explicit confirmation.");

        var prospective = ProjectConfig.Deserialize(evaluation.State.OriginalYaml);
        prospective.Milestones[evaluation.Preview.MilestoneKey].Delivery = new MilestoneDelivery
        {
            At = timeProvider.GetUtcNow(),
            Mode = evaluation.Preview.Mode,
            Reason = evaluation.Preview.Mode == MilestoneDeliveryMode.Exceptional
                ? reason!.Trim()
                : null,
            AcceptedTaskIds = evaluation.Preview.UnfinishedTaskIds.ToList(),
        };
        var impact = automaticActivations.ApplyAffected(
            prospective,
            evaluation.State.TasksById,
            evaluation.State.StateByTaskId,
            evaluation.State.Snapshot,
            ActivationRequirementKind.Milestone,
            evaluation.Preview.MilestoneKey);

        var persisted = PersistTransition(
            evaluation.State,
            prospective,
            evaluation.Preview.MilestoneKey,
            "milestone_delivery_failed",
            "milestone_delivery_rollback_failed",
            $"Milestone {evaluation.Preview.MilestoneKey} could not be delivered");
        return persisted.Success
            ? AppResult<LifecycleMutationResult<ResolvedMilestone>>.Ok(
                new LifecycleMutationResult<ResolvedMilestone>(persisted.Payload!, impact))
            : AppResult<LifecycleMutationResult<ResolvedMilestone>>.Fail(
                persisted.ErrorCode!, persisted.Message!);
    }

    public AppResult<ResolvedMilestone> ReopenMilestone(string key)
    {
        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult<ResolvedMilestone>.Fail(
                "invalid_milestone", "Milestone key is required.");

        var stateResult = ReadCurrentMilestoneState();
        if (!stateResult.Success)
            return AppResult<ResolvedMilestone>.Fail(stateResult.ErrorCode!, stateResult.Message!);
        var state = stateResult.Payload!;
        if (!state.Config.Milestones.TryGetValue(key, out var milestone))
            return AppResult<ResolvedMilestone>.Fail(
                "missing_milestone", $"Milestone {key} not found.");
        if (milestone.Delivery == null)
            return AppResult<ResolvedMilestone>.Fail(
                "milestone_not_delivered", $"Milestone {key} has no delivery record to reopen.");

        var prospective = ProjectConfig.Deserialize(state.OriginalYaml);
        prospective.Milestones[key].Delivery = null;
        return PersistTransition(
            state,
            prospective,
            key,
            "milestone_reopen_failed",
            "milestone_reopen_rollback_failed",
            $"Milestone {key} could not be reopened");
    }

    private AppResult<DeliveryEvaluation> EvaluateDelivery(string key, string? reason)
    {
        key = key.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return AppResult<DeliveryEvaluation>.Fail(
                "invalid_milestone", "Milestone key is required.");

        var stateResult = ReadCurrentMilestoneState();
        if (!stateResult.Success)
            return AppResult<DeliveryEvaluation>.Fail(stateResult.ErrorCode!, stateResult.Message!);
        var state = stateResult.Payload!;
        if (!state.Config.Milestones.TryGetValue(key, out var definition))
            return AppResult<DeliveryEvaluation>.Fail(
                "missing_milestone", $"Milestone {key} not found.");
        if (definition.Delivery != null)
            return AppResult<DeliveryEvaluation>.Fail(
                "milestone_already_delivered", $"Milestone {key} already has a delivery record.");

        var resolved = state.Snapshot.Milestones.Single(milestone =>
            string.Equals(milestone.Key, key, StringComparison.Ordinal));
        if (resolved.Lifecycle == MilestoneLifecycle.Inactive)
            return AppResult<DeliveryEvaluation>.Fail(
                "milestone_delivery_inactive",
                $"Milestone {key} cannot be delivered while its activation triggers are unmet.");

        var assignedTasks = state.TasksById.Values
            .Where(task => string.Equals(task.Milestone, key, StringComparison.Ordinal))
            .OrderBy(task => task.Id, StringComparer.Ordinal)
            .ToList();
        if (assignedTasks.Count == 0)
            return AppResult<DeliveryEvaluation>.Fail(
                "empty_milestone_delivery",
                $"Milestone {key} cannot be delivered because it has no assigned tasks.");

        var unfinishedTaskIds = assignedTasks
            .Where(task => !state.StateByTaskId.TryGetValue(task.Id, out var taskState) ||
                           !string.Equals(taskState, "done", StringComparison.Ordinal))
            .Select(task => task.Id)
            .ToList();
        MilestoneDeliveryMode mode;
        if (unfinishedTaskIds.Count == 0)
        {
            if (reason != null)
                return AppResult<DeliveryEvaluation>.Fail(
                    "milestone_delivery_reason_not_allowed",
                    $"Ordinary delivery for milestone {key} does not accept an exceptional reason.");
            mode = MilestoneDeliveryMode.Ordinary;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reason))
                return AppResult<DeliveryEvaluation>.Fail(
                    "exceptional_delivery_reason_required",
                    $"Milestone {key} has unfinished tasks. Provide --reason for exceptional delivery.");
            mode = MilestoneDeliveryMode.Exceptional;
        }

        var revision = BuildDeliveryRevision(
            key,
            reason?.Trim() ?? string.Empty,
            state.OriginalYaml,
            state.TasksById,
            state.StateByTaskId);
        var preview = new MilestoneDeliveryPreview(
            key,
            resolved.Title,
            revision,
            mode,
            assignedTasks.Count,
            assignedTasks.Count - unfinishedTaskIds.Count,
            unfinishedTaskIds,
            mode == MilestoneDeliveryMode.Exceptional);
        return AppResult<DeliveryEvaluation>.Ok(new DeliveryEvaluation(preview, state));
    }

    private AppResult<MilestoneActivationProjectState> ReadCurrentMilestoneState() =>
        MilestoneActivationProjectStateReader.Read(
            projectRoot,
            resolver,
            persistence,
            "milestone_delivery_config_reload_failed",
            "Milestone delivery configuration could not be reloaded.");

    private AppResult<ResolvedMilestone> PersistTransition(
        MilestoneActivationProjectState state,
        ProjectConfig prospective,
        string key,
        string failureCode,
        string rollbackFailureCode,
        string failureMessage)
    {
        if (FirstValidationError(validator.ValidateProspectiveConfig(prospective)) is { } validationError)
            return AppResult<ResolvedMilestone>.Fail(validationError.Code, validationError.Message);

        try
        {
            persistence.WriteTextAtomic(YamlSerde.Serialize(prospective));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult<ResolvedMilestone>.Fail(failureCode, $"{failureMessage}: {exception.Message}");
        }

        if (GlobalConfig.DryRun)
        {
            var snapshot = resolver.Resolve(prospective, state.TasksById, state.StateByTaskId);
            return AppResult<ResolvedMilestone>.Ok(snapshot.Milestones.Single(milestone =>
                string.Equals(milestone.Key, key, StringComparison.Ordinal)));
        }

        try
        {
            if (persistence.Reload())
            {
                var refreshed = resolver.ResolveCurrentProject();
                if (refreshed.Success)
                {
                    var milestone = refreshed.Payload!.Milestones.SingleOrDefault(item =>
                        string.Equals(item.Key, key, StringComparison.Ordinal));
                    if (milestone != null) return AppResult<ResolvedMilestone>.Ok(milestone);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or YamlException)
        {
            // Restore the exact previous document below.
        }

        if (!TryRestoreConfig(state.OriginalYaml))
            return AppResult<ResolvedMilestone>.Fail(
                rollbackFailureCode,
                $"{failureMessage} and the previous configuration could not be restored.");

        return AppResult<ResolvedMilestone>.Fail(
            failureCode,
            $"{failureMessage}; the previous milestone state was restored.");
    }

    private bool TryRestoreConfig(string yaml)
    {
        try
        {
            persistence.WriteTextAtomic(yaml);
            return persistence.Reload();
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return false;
        }
    }

    private static ProjectValidationIssue? FirstValidationError(ProjectValidationResult validation) =>
        validation.Issues.FirstOrDefault(issue =>
            string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    private string BuildDeliveryRevision(
        string milestoneKey,
        string reason,
        string yaml,
        IReadOnlyDictionary<string, TaskItem> tasksById,
        IReadOnlyDictionary<string, string> stateByTaskId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashValue(hash, "milestone-delivery");
        AppendHashValue(hash, GetRevisionProjectIdentity());
        AppendHashValue(hash, milestoneKey);
        AppendHashValue(hash, reason);
        AppendHashValue(hash, yaml);
        foreach (var task in tasksById.Values.OrderBy(task => task.Id, StringComparer.Ordinal))
        {
            AppendHashValue(hash, task.Id);
            AppendHashValue(hash, task.Milestone ?? string.Empty);
            AppendHashValue(hash, stateByTaskId.GetValueOrDefault(task.Id, string.Empty));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private string GetRevisionProjectIdentity() =>
        projectRoot.TryReadProjectId(out var projectId)
            ? projectId
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot.RepositoryPath));

    private static void AppendHashValue(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private sealed record DeliveryEvaluation(
        MilestoneDeliveryPreview Preview,
        MilestoneActivationProjectState State);
}
