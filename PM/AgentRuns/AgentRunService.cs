using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PM.Application;
using PM.Project;

namespace PM.AgentRuns;

public sealed partial class AgentRunService(
    ProjectRoot projectRoot,
    BoardService boardService,
    IAgentRunGitInspector gitInspector,
    AgentRunCache cache,
    IAgentRunnerClient runners,
    TimeProvider timeProvider,
    IAgentRunLinkedContextResolver? linkedContextResolver = null) : IAgentRunService
{
    private const string PromptProfile = "task-execution";

    public async Task<AppResult<AgentRunPreflightResult>> Preflight(
        AgentRunSelection selection,
        CancellationToken cancellationToken = default)
    {
        var input = ValidateSelection(selection);
        if (!input.Success)
            return AppResult<AgentRunPreflightResult>.Fail(input.ErrorCode!, input.Message!);

        var environment = await InspectEnvironment(selection, cancellationToken);
        if (!environment.Success)
            return AppResult<AgentRunPreflightResult>.Fail(environment.ErrorCode!, environment.Message!);
        if (!environment.Payload!.Ready)
            return AppResult<AgentRunPreflightResult>.Ok(new AgentRunPreflightResult(
                false, null, null, null, environment.Payload.Checks));

        var requestedAt = CanonicalNow();
        var runId = $"run-{Guid.CreateVersion7(requestedAt).ToString("N")}";
        var request = BuildRequest(runId, requestedAt, environment.Payload);
        var finalChecks = environment.Payload.Checks;
        if ((request.Specification.LinkedContexts?.Count ?? 0) > 0)
        {
            var runnerPreflight = await runners.Preflight(selection.RunnerId, request, cancellationToken);
            if (!runnerPreflight.Success)
                return AppResult<AgentRunPreflightResult>.Fail(runnerPreflight.ErrorCode!, runnerPreflight.Message!);
            var checks = environment.Payload.Checks.Concat(runnerPreflight.Payload!.Checks).ToList();
            if (!runnerPreflight.Payload.Ready)
                return AppResult<AgentRunPreflightResult>.Ok(new AgentRunPreflightResult(
                    false, null, null, null, checks));
            finalChecks = checks;
        }
        var save = await cache.SaveDraft(selection, request);
        if (!save.Success)
            return AppResult<AgentRunPreflightResult>.Fail(save.ErrorCode!, save.Message!);

        return AppResult<AgentRunPreflightResult>.Ok(new AgentRunPreflightResult(
            true, runId, request.SpecificationHash, request, finalChecks));
    }

    public async Task<AppResult<AgentRunRemoteStart>> Start(
        string runId,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        if (!cached.Success)
            return AppResult<AgentRunRemoteStart>.Fail(cached.ErrorCode!, cached.Message!);
        if (!string.Equals(cached.Payload!.Request.SpecificationHash, expectedRevision, StringComparison.Ordinal))
            return AppResult<AgentRunRemoteStart>.Fail("precondition_failed",
                "The persisted run draft has changed. Refetch it and retry.");

        if (cached.Payload.RemoteRun != null)
        {
            var repeated = await runners.Start(cached.Payload.Selection.RunnerId, cached.Payload.Request,
                cancellationToken);
            if (!repeated.Success)
                return AppResult<AgentRunRemoteStart>.Fail(repeated.ErrorCode!, repeated.Message!);
            var repeatedUpdate = await cache.UpdateRemote(runId, repeated.Payload!.Run);
            return repeatedUpdate.Success
                ? repeated
                : AppResult<AgentRunRemoteStart>.Fail(repeatedUpdate.ErrorCode!, repeatedUpdate.Message!);
        }

        var environment = await InspectEnvironment(cached.Payload.Selection, cancellationToken);
        if (!environment.Success)
            return AppResult<AgentRunRemoteStart>.Fail(environment.ErrorCode!, environment.Message!);
        if (!environment.Payload!.Ready)
            return AppResult<AgentRunRemoteStart>.Fail("stale_run_preflight",
                "Run readiness changed after preflight. Run preflight again.");

        var current = BuildRequest(runId, cached.Payload.Request.Specification.RequestedAt,
            environment.Payload);
        if (!string.Equals(current.SpecificationHash, cached.Payload.Request.SpecificationHash,
                StringComparison.Ordinal))
            return AppResult<AgentRunRemoteStart>.Fail("stale_run_preflight",
                "The task, repository, runner, or runtime selection changed after preflight.");

        if ((current.Specification.LinkedContexts?.Count ?? 0) > 0)
        {
            var runnerPreflight = await runners.Preflight(cached.Payload.Selection.RunnerId, current,
                cancellationToken);
            if (!runnerPreflight.Success)
                return AppResult<AgentRunRemoteStart>.Fail(runnerPreflight.ErrorCode!, runnerPreflight.Message!);
            if (!runnerPreflight.Payload!.Ready)
                return AppResult<AgentRunRemoteStart>.Fail("stale_run_preflight",
                    "Runner access to one or more linked wiki contexts changed after preflight.");
        }

        var started = await runners.Start(cached.Payload.Selection.RunnerId, cached.Payload.Request,
            cancellationToken);
        if (!started.Success)
            return AppResult<AgentRunRemoteStart>.Fail(started.ErrorCode!, started.Message!);
        var updated = await cache.UpdateRemote(runId, started.Payload!.Run);
        return updated.Success
            ? started
            : AppResult<AgentRunRemoteStart>.Fail(updated.ErrorCode!, updated.Message!);
    }

    public async Task<AppResult<AgentRunInspection>> Inspect(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        if (!cached.Success)
            return AppResult<AgentRunInspection>.Fail(cached.ErrorCode!, cached.Message!);
        var remote = await runners.Inspect(cached.Payload!.Selection.RunnerId, runId, cancellationToken);
        if (!remote.Success)
            return AppResult<AgentRunInspection>.Fail(remote.ErrorCode!, remote.Message!);
        var updated = await cache.UpdateRemote(runId, remote.Payload!);
        if (!updated.Success)
            return AppResult<AgentRunInspection>.Fail(updated.ErrorCode!, updated.Message!);

        var currentTaskRevision = CurrentTaskRevision(cached.Payload.Request.Specification.Task.TaskId);
        var taskChanged = currentTaskRevision == null ||
                          !string.Equals(currentTaskRevision,
                              cached.Payload.Request.Specification.Task.Revision, StringComparison.Ordinal);
        var remoteRun = remote.Payload!;
        var revision = AgentRunCanonicalJson.ComputeSpecificationHash(remoteRun.Specification) +
                       $"-{remoteRun.UpdatedAt.ToUnixTimeMilliseconds()}-{remoteRun.LastEventSequence}";
        return AppResult<AgentRunInspection>.Ok(new AgentRunInspection(
            remoteRun, taskChanged, currentTaskRevision, revision));
    }

    public Task<AppResult<AgentRunnerRunPage>> ActiveRuns(
        string runnerId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default) =>
        runners.ActiveRuns(runnerId, limit, cursor, cancellationToken);

    public async Task<AppResult<AgentRunEventPage>> Events(
        string runId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        if (!cached.Success)
            return AppResult<AgentRunEventPage>.Fail(cached.ErrorCode!, cached.Message!);
        var events = await runners.Events(cached.Payload!.Selection.RunnerId, runId,
            afterSequence, limit, cancellationToken);
        if (events.Success)
        {
            var advanced = await cache.AdvanceSequence(runId, events.Payload!.NextAfterSequence);
            if (!advanced.Success)
                return AppResult<AgentRunEventPage>.Fail(advanced.ErrorCode!, advanced.Message!);
        }
        return events;
    }

    public async Task<AppResult<IAgentRunnerEventStream>> OpenEventStream(
        string runId,
        long afterSequence,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        return cached.Success
            ? await runners.OpenEventStream(cached.Payload!.Selection.RunnerId, runId,
                afterSequence, cancellationToken)
            : AppResult<IAgentRunnerEventStream>.Fail(cached.ErrorCode!, cached.Message!);
    }

    public Task<AppResult> AdvanceSequence(string runId, long sequence) => cache.AdvanceSequence(runId, sequence);

    public async Task<AppResult<AgentRunCancellation>> Cancel(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        if (!cached.Success)
            return AppResult<AgentRunCancellation>.Fail(cached.ErrorCode!, cached.Message!);
        var cancelled = await runners.Cancel(cached.Payload!.Selection.RunnerId, runId, cancellationToken);
        if (!cancelled.Success) return cancelled;
        var updated = await cache.UpdateRemote(runId, cancelled.Payload!.Run);
        return updated.Success
            ? cancelled
            : AppResult<AgentRunCancellation>.Fail(updated.ErrorCode!, updated.Message!);
    }

    public async Task<AppResult<IReadOnlyList<AgentRunArtifact>>> Artifacts(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        return cached.Success
            ? await runners.Artifacts(cached.Payload!.Selection.RunnerId, runId, cancellationToken)
            : AppResult<IReadOnlyList<AgentRunArtifact>>.Fail(cached.ErrorCode!, cached.Message!);
    }

    public async Task<AppResult<AgentRunArtifact>> Artifact(
        string runId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        return cached.Success
            ? await runners.Artifact(cached.Payload!.Selection.RunnerId, runId, artifactId, cancellationToken)
            : AppResult<AgentRunArtifact>.Fail(cached.ErrorCode!, cached.Message!);
    }

    public async Task<AppResult<IAgentRunArtifactContent>> ArtifactContent(
        string runId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.Get(runId);
        return cached.Success
            ? await runners.ArtifactContent(cached.Payload!.Selection.RunnerId, runId, artifactId, cancellationToken)
            : AppResult<IAgentRunArtifactContent>.Fail(cached.ErrorCode!, cached.Message!);
    }

    private async Task<AppResult<EnvironmentSnapshot>> InspectEnvironment(
        AgentRunSelection selection,
        CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists || projectRoot.RootPath == null || projectRoot.Config == null)
            return AppResult<EnvironmentSnapshot>.Fail("missing_project", "Project not found. Run pm init first.");
        var task = boardService.GetTask(selection.TaskId);
        if (!task.Success)
            return AppResult<EnvironmentSnapshot>.Fail(task.ErrorCode!, task.Message!);
        string projectId;
        try
        {
            projectId = File.ReadAllText(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile)).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AppResult<EnvironmentSnapshot>.Fail("missing_project_id", "The project ID could not be read.");
        }
        if (projectId.Length == 0 || projectId.Length > 256 || projectId.Any(char.IsControl))
            return AppResult<EnvironmentSnapshot>.Fail("invalid_project_id", "The project ID is invalid.");

        var checks = new List<AgentRunPreflightCheck>();
        var projectDirectory = Directory.GetParent(projectRoot.RootPath)?.FullName;
        if (projectDirectory == null)
            return AppResult<EnvironmentSnapshot>.Fail("missing_repository", "The project repository was not found.");
        var git = await gitInspector.Inspect(projectDirectory, selection.TaskId, cancellationToken);
        if (!git.Success)
            return AppResult<EnvironmentSnapshot>.Fail(git.ErrorCode!, git.Message!);
        checks.AddRange(git.Payload!.Checks);
        if (!git.Payload.Ready)
        {
            checks.Add(Skipped("runner", "Runner", "Runner checks were skipped until repository checks pass."));
            return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, false, checks,
                projectId, task.Payload!, null, null));
        }

        var contextSelections = selection.LinkedContexts ?? [];
        var contextResolutionService = linkedContextResolver ??
                                       new AgentRunLinkedContextResolver(
                                           LinkedProjectFamilyService.CreateDefault(projectRoot));
        var linkedContexts = await contextResolutionService.Resolve(contextSelections, cancellationToken);
        if (!linkedContexts.Success)
            return AppResult<EnvironmentSnapshot>.Fail(linkedContexts.ErrorCode!, linkedContexts.Message!);
        checks.AddRange(linkedContexts.Payload!.Checks);
        if (!linkedContexts.Payload.Ready)
        {
            checks.Add(Skipped("runner", "Runner", "Runner checks were skipped until linked context checks pass."));
            return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, false, checks,
                projectId, task.Payload!, git.Payload.Snapshot, null, linkedContexts.Payload.Contexts));
        }

        var registration = runners.Registration(selection.RunnerId);
        if (!registration.Success)
        {
            checks.Add(Failed("runner_registration", "Runner registration", "The selected runner is not paired."));
            return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, false, checks,
                projectId, task.Payload!, git.Payload.Snapshot, null));
        }
        checks.Add(Passed("runner_registration", "Runner registration", "The selected runner is paired."));

        var health = await runners.Health(selection.RunnerId, cancellationToken);
        if (!health.Success)
        {
            checks.Add(Failed("runner_health", "Runner health", health.Message ?? "The runner is unavailable."));
            return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, false, checks,
                projectId, task.Payload!, git.Payload.Snapshot, null));
        }
        checks.Add(Passed("runner_health", "Runner health", "The runner is online and protocol-compatible."));

        var capabilities = await runners.Capabilities(selection.RunnerId, cancellationToken);
        if (!capabilities.Success)
        {
            checks.Add(Failed("runner_capabilities", "Runner capabilities",
                capabilities.Message ?? "Runner capabilities are unavailable."));
            return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, false, checks,
                projectId, task.Payload!, git.Payload.Snapshot, null));
        }

        var negotiatedRegistration = runners.Registration(selection.RunnerId);
        if (!negotiatedRegistration.Success)
        {
            checks.Add(Failed("runner_registration", "Runner registration",
                "The negotiated runner registration could not be loaded."));
            return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, false, checks,
                projectId, task.Payload!, git.Payload.Snapshot, null, linkedContexts.Payload.Contexts));
        }
        if (linkedContexts.Payload.Contexts.Count > 0 && negotiatedRegistration.Payload!.ProtocolVersion.Minor < 2)
        {
            checks.Add(Failed("linked_context_protocol", "Linked wiki context",
                "The selected runner must negotiate protocol 1.2 for linked wiki context."));
            return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, false, checks,
                projectId, task.Payload!, git.Payload.Snapshot, null, linkedContexts.Payload.Contexts));
        }

        var provider = capabilities.Payload!.AgentProviders.FirstOrDefault(item => item.ProviderId == selection.ProviderId);
        var profile = capabilities.Payload.RuntimeProfiles.FirstOrDefault(item => item.ProfileId == selection.ProfileId);
        var selectionsValid = provider != null && provider.ModelIds.Contains(selection.ModelId, StringComparer.Ordinal) &&
                              provider.EffortIds.Contains(selection.EffortId, StringComparer.Ordinal) && profile != null;
        checks.Add(selectionsValid
            ? Passed("runner_capabilities", "Runner capabilities", "The selected provider, model, effort, and profile are supported.")
            : Failed("runner_capabilities", "Runner capabilities", "The runner does not support the complete explicit selection."));
        var hasCapacity = capabilities.Payload.Capacity.ActiveRuns < capabilities.Payload.Capacity.MaximumRuns;
        checks.Add(hasCapacity
            ? Passed("runner_capacity", "Runner capacity", "The runner has an available execution slot.")
            : Failed("runner_capacity", "Runner capacity", "The runner has no available execution slots."));
        var ready = selectionsValid && hasCapacity;
        return AppResult<EnvironmentSnapshot>.Ok(new EnvironmentSnapshot(selection, ready, checks,
            projectId, task.Payload!, git.Payload.Snapshot, ready ? profile : null,
            linkedContexts.Payload.Contexts));
    }

    private AgentRunRequest BuildRequest(string runId, DateTimeOffset requestedAt, EnvironmentSnapshot environment)
    {
        var registration = runners.Registration(environment.Selection.RunnerId);
        var protocolVersion = registration.Success
            ? registration.Payload!.ProtocolVersion
            : AgentRunProtocol.Current;
        var specification = new AgentRunSpecification(
            protocolVersion,
            runId,
            requestedAt,
            new AgentRunProject(environment.ProjectId, projectRoot.Config!.Name),
            new AgentRunTask(environment.Task.Task.Id, environment.Task.Task.Title, environment.Git!.TaskRevision),
            new AgentRunRepository(environment.Git.RemoteUrl, environment.Git.HeadCommit),
            new AgentRunAgent(environment.Selection.ProviderId, environment.Selection.ModelId,
                environment.Selection.EffortId, PromptProfile),
            new AgentRunRuntime(environment.Selection.RunnerId, environment.Profile!),
            environment.LinkedContexts.Count == 0 ? null : environment.LinkedContexts);
        return new AgentRunRequest(AgentRunCanonicalJson.ComputeSpecificationHash(specification), specification);
    }

    private AppResult ValidateSelection(AgentRunSelection selection)
    {
        if (selection == null || !IsIdentifier(selection.TaskId) || !IsIdentifier(selection.RunnerId) ||
            !IsIdentifier(selection.ProfileId) || !IsIdentifier(selection.ProviderId) ||
            !IsIdentifier(selection.ModelId) || !IsIdentifier(selection.EffortId) ||
            selection.LinkedContexts is { Count: > 31 } ||
            (selection.LinkedContexts ?? []).Any(context => !IsIdentifier(context.ProjectId)) ||
            (selection.LinkedContexts ?? []).Select(context => context.ProjectId)
                .Distinct(StringComparer.Ordinal).Count() != (selection.LinkedContexts?.Count ?? 0))
            return AppResult.Fail("invalid_run_selection", "Task, runner, profile, provider, model, and effort are required.");
        return AppResult.Ok();
    }

    private string? CurrentTaskRevision(string taskId)
    {
        try
        {
            var bytes = File.ReadAllBytes(projectRoot.GetTaskFilePath(taskId));
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private DateTimeOffset CanonicalNow()
    {
        var now = timeProvider.GetUtcNow();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second,
            now.Millisecond, TimeSpan.Zero);
    }

    private static AgentRunPreflightCheck Passed(string id, string label, string summary) =>
        new(id, label, AgentRunPreflightCheckStatus.Passed, summary);
    private static AgentRunPreflightCheck Failed(string id, string label, string summary) =>
        new(id, label, AgentRunPreflightCheckStatus.Failed, summary);
    private static AgentRunPreflightCheck Skipped(string id, string label, string summary) =>
        new(id, label, AgentRunPreflightCheckStatus.Skipped, summary);
    private static bool IsIdentifier(string? value) =>
        value != null && IdentifierPattern().IsMatch(value);

    private sealed record EnvironmentSnapshot(
        AgentRunSelection Selection,
        bool Ready,
        IReadOnlyList<AgentRunPreflightCheck> Checks,
        string ProjectId,
        BoardTask Task,
        AgentRunGitSnapshot? Git,
        AgentRunRuntimeProfile? Profile,
        IReadOnlyList<AgentRunLinkedContext>? Contexts = null)
    {
        public IReadOnlyList<AgentRunLinkedContext> LinkedContexts => Contexts ?? [];
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
