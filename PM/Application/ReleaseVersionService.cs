using PM.Files;
using PM.Project;
using YamlDotNet.Core;
using System.Security.Cryptography;
using System.Text;

namespace PM.Application;

public sealed record ReleaseVersionState(bool Enabled, ReleaseVersion? Version);

public sealed class ReleaseVersionService
{
    private readonly ProjectRoot projectRoot;
    private readonly TimeProvider timeProvider;

    public ReleaseVersionService(ProjectRoot projectRoot)
        : this(projectRoot, TimeProvider.System)
    {
    }

    public ReleaseVersionService(ProjectRoot projectRoot, TimeProvider timeProvider)
    {
        this.projectRoot = projectRoot;
        this.timeProvider = timeProvider;
    }

    public AppResult<ReleaseVersionState> Read()
    {
        if (!projectRoot.Exists || projectRoot.RootPath == null)
            return AppResult<ReleaseVersionState>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!FileSystem.FileExists(projectRoot.ReleaseVersionPath))
            return AppResult<ReleaseVersionState>.Ok(new ReleaseVersionState(false, null));

        try
        {
            var content = FileSystem.ReadAllText(projectRoot.ReleaseVersionPath);
            return ReleaseVersion.TryParse(content, out var version, out var error)
                ? AppResult<ReleaseVersionState>.Ok(new ReleaseVersionState(true, version))
                : AppResult<ReleaseVersionState>.Fail("invalid_release_version", error!);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult<ReleaseVersionState>.Fail(
                "release_version_unreadable",
                $"Release version could not be read: {exception.Message}");
        }
    }

    public AppResult<ReleaseVersionStatus> ReadStatus()
    {
        var state = Read();
        if (!state.Success)
            return AppResult<ReleaseVersionStatus>.Fail(state.ErrorCode!, state.Message!);

        var pending = ReadTransition(projectRoot.PendingReleaseTransitionPath, required: false);
        if (!pending.Success)
            return AppResult<ReleaseVersionStatus>.Fail(pending.ErrorCode!, pending.Message!);

        var latest = ReadLatestTransition();
        if (!latest.Success)
            return AppResult<ReleaseVersionStatus>.Fail(latest.ErrorCode!, latest.Message!);

        return AppResult<ReleaseVersionStatus>.Ok(new ReleaseVersionStatus(
            state.Payload!.Enabled,
            state.Payload.Version,
            pending.Payload,
            latest.Payload));
    }

    public AppResult EnsureMutationReady()
    {
        var state = Read();
        if (!state.Success) return AppResult.Fail(state.ErrorCode!, state.Message!);
        if (!state.Payload!.Enabled) return AppResult.Ok();
        return FileSystem.FileExists(projectRoot.PendingReleaseTransitionPath)
            ? AppResult.Fail(
                "release_reconciliation_required",
                "A pending release transition must be reconciled before changing task or milestone lifecycle state.")
            : AppResult.Ok();
    }

    public AppResult<ReleaseTransitionPlan?> PrepareTaskCompletion(string taskId) =>
        Prepare(ReleaseTransitionKinds.Task, taskId, null);

    public AppResult<ReleaseTransitionPlan?> PrepareMilestoneDelivery(string milestoneKey) =>
        Prepare(ReleaseTransitionKinds.Milestone, milestoneKey, null);

    public AppResult<ReleaseTransitionPlan> PreviewMajor(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return AppResult<ReleaseTransitionPlan>.Fail(
                "release_major_reason_required", "Advancing the major version requires a reason.");
        var result = Prepare(ReleaseTransitionKinds.ManualMajor, null, reason.Trim());
        if (!result.Success)
            return AppResult<ReleaseTransitionPlan>.Fail(result.ErrorCode!, result.Message!);
        if (result.Payload == null)
            return AppResult<ReleaseTransitionPlan>.Fail(
                "release_version_disabled",
                $"This project has no {GlobalConfig.ReleaseVersionFile} and does not participate in release versioning.");
        return AppResult<ReleaseTransitionPlan>.Ok(result.Payload);
    }

    public AppResult Begin(ReleaseTransitionPlan plan)
    {
        try
        {
            if (FileSystem.FileExists(projectRoot.PendingReleaseTransitionPath))
                return AppResult.Fail(
                    "release_reconciliation_required", "A pending release transition already exists.");
            var current = Read();
            if (!current.Success) return AppResult.Fail(current.ErrorCode!, current.Message!);
            if (!current.Payload!.Enabled || current.Payload.Version!.ToString() != plan.Transition.FromVersion)
                return AppResult.Fail(
                    "release_transition_stale", "The release version changed after this transition was previewed.");
            if (FileSystem.FileExists(GetEvidencePath(plan.Transition.ToVersion)))
                return AppResult.Fail(
                    "release_transition_exists", $"Release transition evidence for {plan.Transition.ToVersion} already exists.");
            if (GlobalConfig.DryRun) return AppResult.Ok();
            FileSystem.WriteAllTextNew(
                projectRoot.PendingReleaseTransitionPath,
                YamlSerde.Serialize(plan.Transition));
            return AppResult.Ok();
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult.Fail("release_transition_begin_failed", $"Release transition could not begin: {exception.Message}");
        }
    }

    public AppResult<ReleaseVersionTransition> Complete(ReleaseTransitionPlan plan)
    {
        if (GlobalConfig.DryRun)
            return AppResult<ReleaseVersionTransition>.Ok(plan.Transition);

        var pending = ReadTransition(projectRoot.PendingReleaseTransitionPath, required: true);
        if (!pending.Success)
            return AppResult<ReleaseVersionTransition>.Fail(pending.ErrorCode!, pending.Message!);
        if (pending.Payload != plan.Transition)
            return AppResult<ReleaseVersionTransition>.Fail(
                "release_transition_conflict", "The pending release transition does not match this mutation.");

        try
        {
            FileSystem.WriteAllTextAtomic(projectRoot.ReleaseVersionPath, $"{plan.Transition.ToVersion}\n");
            WriteEvidence(plan.Transition);
            FileSystem.DeleteFile(projectRoot.PendingReleaseTransitionPath);
            return AppResult<ReleaseVersionTransition>.Ok(plan.Transition);
        }
        catch (Exception exception) when (IsStorageException(exception) || exception is InvalidDataException)
        {
            return AppResult<ReleaseVersionTransition>.Fail(
                "release_transition_complete_failed",
                $"Release transition requires reconciliation: {exception.Message}");
        }
    }

    public AppResult Rollback(ReleaseTransitionPlan plan)
    {
        if (GlobalConfig.DryRun) return AppResult.Ok();
        try
        {
            var current = Read();
            if (!current.Success) return AppResult.Fail(current.ErrorCode!, current.Message!);
            if (current.Payload!.Version?.ToString() == plan.Transition.ToVersion)
                FileSystem.WriteAllTextAtomic(projectRoot.ReleaseVersionPath, $"{plan.Transition.FromVersion}\n");
            else if (current.Payload.Version?.ToString() != plan.Transition.FromVersion)
                return AppResult.Fail("release_transition_rollback_failed", "Release version no longer matches the transition boundary.");

            var evidencePath = GetEvidencePath(plan.Transition.ToVersion);
            if (FileSystem.FileExists(evidencePath))
            {
                var evidence = ReadTransition(evidencePath, required: true);
                if (!evidence.Success || evidence.Payload != plan.Transition)
                    return AppResult.Fail("release_transition_rollback_failed", "Release transition evidence conflicts with rollback.");
                FileSystem.DeleteFile(evidencePath);
            }

            var pending = ReadTransition(projectRoot.PendingReleaseTransitionPath, required: true);
            if (!pending.Success || pending.Payload != plan.Transition)
                return AppResult.Fail("release_transition_rollback_failed", "Pending release transition conflicts with rollback.");
            FileSystem.DeleteFile(projectRoot.PendingReleaseTransitionPath);
            return AppResult.Ok();
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult.Fail("release_transition_rollback_failed", $"Release transition could not be rolled back: {exception.Message}");
        }
    }

    public AppResult<ReleaseReconciliationResult> Reconcile(bool dryRun = false)
    {
        var pending = ReadTransition(projectRoot.PendingReleaseTransitionPath, required: false);
        if (!pending.Success)
            return AppResult<ReleaseReconciliationResult>.Fail(pending.ErrorCode!, pending.Message!);
        if (pending.Payload == null)
            return AppResult<ReleaseReconciliationResult>.Ok(new(false, "none", null));

        var transition = pending.Payload;
        var applied = IsPrimaryMutationApplied(transition);
        if (!applied.Success)
            return AppResult<ReleaseReconciliationResult>.Fail(applied.ErrorCode!, applied.Message!);

        var current = Read();
        if (!current.Success)
            return AppResult<ReleaseReconciliationResult>.Fail(current.ErrorCode!, current.Message!);
        var currentText = current.Payload!.Version?.ToString();
        if (currentText != transition.FromVersion && currentText != transition.ToVersion)
            return AppResult<ReleaseReconciliationResult>.Fail(
                "release_transition_conflict", "The current version is outside the pending transition boundary.");

        var evidencePath = GetEvidencePath(transition.ToVersion);
        var evidence = ReadTransition(evidencePath, required: false);
        if (!evidence.Success)
            return AppResult<ReleaseReconciliationResult>.Fail(evidence.ErrorCode!, evidence.Message!);
        if (evidence.Payload != null && evidence.Payload != transition)
            return AppResult<ReleaseReconciliationResult>.Fail(
                "release_transition_conflict", "Persisted release evidence conflicts with the pending transition.");

        if (dryRun)
            return AppResult<ReleaseReconciliationResult>.Ok(new(
                true, applied.Payload! ? "complete-forward" : "clear-unapplied", transition));

        try
        {
            if (applied.Payload!)
            {
                if (currentText == transition.FromVersion)
                    FileSystem.WriteAllTextAtomic(projectRoot.ReleaseVersionPath, $"{transition.ToVersion}\n");
                WriteEvidence(transition);
            }
            else
            {
                if (currentText != transition.FromVersion || evidence.Payload != null)
                    return AppResult<ReleaseReconciliationResult>.Fail(
                        "release_transition_conflict", "An unapplied transition has already changed release state.");
            }

            FileSystem.DeleteFile(projectRoot.PendingReleaseTransitionPath);
            return AppResult<ReleaseReconciliationResult>.Ok(new(
                true, applied.Payload ? "completed-forward" : "cleared-unapplied", transition));
        }
        catch (Exception exception) when (IsStorageException(exception) || exception is InvalidDataException)
        {
            return AppResult<ReleaseReconciliationResult>.Fail(
                "release_reconciliation_failed", $"Release reconciliation did not complete: {exception.Message}");
        }
    }

    public AppResult<IReadOnlyList<ReleaseVersionTransition>> ReadEvidence()
    {
        if (!Directory.Exists(projectRoot.ReleaseTransitionsPath))
            return AppResult<IReadOnlyList<ReleaseVersionTransition>>.Ok([]);
        try
        {
            var transitions = new List<ReleaseVersionTransition>();
            foreach (var file in Directory.EnumerateFiles(projectRoot.ReleaseTransitionsPath, "*.yaml")
                         .Order(StringComparer.Ordinal))
            {
                var parsed = ReadTransition(file, required: true);
                if (!parsed.Success)
                    return AppResult<IReadOnlyList<ReleaseVersionTransition>>.Fail(parsed.ErrorCode!, parsed.Message!);
                transitions.Add(parsed.Payload!);
            }
            return AppResult<IReadOnlyList<ReleaseVersionTransition>>.Ok(transitions);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return AppResult<IReadOnlyList<ReleaseVersionTransition>>.Fail(
                "release_transition_unreadable", $"Release transition evidence could not be read: {exception.Message}");
        }
    }

    private AppResult<ReleaseTransitionPlan?> Prepare(string kind, string? source, string? reason)
    {
        var ready = EnsureMutationReady();
        if (!ready.Success)
            return AppResult<ReleaseTransitionPlan?>.Fail(ready.ErrorCode!, ready.Message!);
        var state = Read();
        if (!state.Success)
            return AppResult<ReleaseTransitionPlan?>.Fail(state.ErrorCode!, state.Message!);
        if (!state.Payload!.Enabled)
            return AppResult<ReleaseTransitionPlan?>.Ok(null);

        var current = state.Payload.Version!;
        ReleaseVersion? next = null;
        var advanced = kind switch
        {
            ReleaseTransitionKinds.Task => current.TryNextPatch(out next),
            ReleaseTransitionKinds.Milestone => current.TryNextMinor(out next),
            ReleaseTransitionKinds.ManualMajor => current.TryNextMajor(out next),
            _ => false,
        };
        if (!advanced)
            return AppResult<ReleaseTransitionPlan?>.Fail(
                "release_version_overflow", $"Release version cannot advance beyond component {ReleaseVersion.MaximumComponent}.");

        var transition = new ReleaseVersionTransition
        {
            At = timeProvider.GetUtcNow(),
            Kind = kind,
            FromVersion = current.ToString(),
            ToVersion = next!.ToString(),
            Source = source,
            Reason = reason,
        };
        if (FileSystem.FileExists(GetEvidencePath(transition.ToVersion)))
            return AppResult<ReleaseTransitionPlan?>.Fail(
                "release_transition_exists", $"Release transition evidence for {transition.ToVersion} already exists.");
        var revisionInput = string.Join("\n", transition.Kind, transition.FromVersion,
            transition.ToVersion, transition.Source ?? string.Empty, transition.Reason ?? string.Empty);
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionInput)))
            .ToLowerInvariant();
        return AppResult<ReleaseTransitionPlan?>.Ok(new ReleaseTransitionPlan(transition, revision));
    }

    private AppResult<bool> IsPrimaryMutationApplied(ReleaseVersionTransition transition)
    {
        if (transition.Kind == ReleaseTransitionKinds.ManualMajor)
            return AppResult<bool>.Ok(true);
        if (transition.Kind == ReleaseTransitionKinds.Task)
        {
            if (transition.Source == null || !projectRoot.TryGetById(transition.Source, out var task))
                return AppResult<bool>.Fail("release_transition_source_missing", "Pending task transition source is missing.");
            return AppResult<bool>.Ok(projectRoot.TryGetState(task, out var state) && state == "done");
        }
        if (transition.Kind == ReleaseTransitionKinds.Milestone)
        {
            if (transition.Source == null || projectRoot.Config == null ||
                !projectRoot.Config.Milestones.TryGetValue(transition.Source, out var milestone))
                return AppResult<bool>.Fail("release_transition_source_missing", "Pending milestone transition source is missing.");
            return AppResult<bool>.Ok(milestone.Delivery != null);
        }
        return AppResult<bool>.Fail("invalid_release_transition", $"Unknown transition kind {transition.Kind}.");
    }

    private AppResult<ReleaseVersionTransition?> ReadLatestTransition()
    {
        var evidence = ReadEvidence();
        if (!evidence.Success)
            return AppResult<ReleaseVersionTransition?>.Fail(evidence.ErrorCode!, evidence.Message!);
        var latest = evidence.Payload!
            .Select(item => (Item: item, Parsed: ParseRequiredVersion(item.ToVersion)))
            .Where(item => item.Parsed != null)
            .OrderBy(item => item.Parsed!.Major)
            .ThenBy(item => item.Parsed!.Minor)
            .ThenBy(item => item.Parsed!.Patch)
            .Select(item => item.Item)
            .LastOrDefault();
        return AppResult<ReleaseVersionTransition?>.Ok(latest);
    }

    private AppResult<ReleaseVersionTransition?> ReadTransition(string path, bool required)
    {
        if (!FileSystem.FileExists(path))
            return required
                ? AppResult<ReleaseVersionTransition?>.Fail("release_transition_missing", $"Release transition {path} is missing.")
                : AppResult<ReleaseVersionTransition?>.Ok(null);
        try
        {
            var transition = YamlSerde.Deserialize<ReleaseVersionTransition>(FileSystem.ReadAllText(path));
            var validation = ValidateTransition(transition);
            return validation == null
                ? AppResult<ReleaseVersionTransition?>.Ok(transition)
                : AppResult<ReleaseVersionTransition?>.Fail("invalid_release_transition", $"{path}: {validation}");
        }
        catch (Exception exception) when (exception is YamlException or InvalidDataException || IsStorageException(exception))
        {
            return AppResult<ReleaseVersionTransition?>.Fail(
                "invalid_release_transition", $"Release transition {path} is invalid: {exception.Message}");
        }
    }

    internal static string? ValidateTransition(ReleaseVersionTransition transition)
    {
        if (transition.SchemaVersion != 1) return "schemaVersion must be 1.";
        if (!ReleaseTransitionKinds.IsKnown(transition.Kind)) return "kind is not supported.";
        var from = ParseRequiredVersion(transition.FromVersion);
        var to = ParseRequiredVersion(transition.ToVersion);
        if (from == null || to == null) return "fromVersion and toVersion must be canonical versions.";
        var expected = transition.Kind switch
        {
            ReleaseTransitionKinds.Task when from.TryNextPatch(out var next) => next,
            ReleaseTransitionKinds.Milestone when from.TryNextMinor(out var next) => next,
            ReleaseTransitionKinds.ManualMajor when from.TryNextMajor(out var next) => next,
            _ => null,
        };
        if (expected == null || expected != to) return "version delta does not match kind.";
        if (transition.Kind == ReleaseTransitionKinds.ManualMajor)
        {
            if (transition.Source != null) return "manual-major must not have source.";
            if (string.IsNullOrWhiteSpace(transition.Reason)) return "manual-major requires reason.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(transition.Source)) return $"{transition.Kind} requires source.";
            if (transition.Reason != null) return $"{transition.Kind} must not have reason.";
        }
        return null;
    }

    private void WriteEvidence(ReleaseVersionTransition transition)
    {
        FileSystem.CreateDirectory(projectRoot.ReleaseTransitionsPath);
        var path = GetEvidencePath(transition.ToVersion);
        var existing = ReadTransition(path, required: false);
        if (!existing.Success) throw new InvalidDataException(existing.Message);
        if (existing.Payload == transition) return;
        if (existing.Payload != null) throw new InvalidDataException($"Release transition evidence for {transition.ToVersion} conflicts.");
        FileSystem.WriteAllTextNew(path, YamlSerde.Serialize(transition));
    }

    private string GetEvidencePath(string version) =>
        Path.Combine(projectRoot.ReleaseTransitionsPath, $"{version}.yaml");

    private static ReleaseVersion? ParseRequiredVersion(string value) =>
        ReleaseVersion.TryParse(value, out var version, out _) ? version : null;

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException;
}
