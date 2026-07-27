using PM.Application;

namespace PM.AgentRuns;

public static class AgentRunLifecycle
{
    private static readonly IReadOnlyDictionary<AgentRunState, AgentRunState> ForwardTransitions =
        new Dictionary<AgentRunState, AgentRunState>
        {
            [AgentRunState.Requested] = AgentRunState.Accepted,
            [AgentRunState.Accepted] = AgentRunState.Queued,
            [AgentRunState.Queued] = AgentRunState.PreparingWorkspace,
            [AgentRunState.PreparingWorkspace] = AgentRunState.StartingRuntime,
            [AgentRunState.StartingRuntime] = AgentRunState.StartingAgent,
            [AgentRunState.StartingAgent] = AgentRunState.Running,
            [AgentRunState.Running] = AgentRunState.Validating,
            [AgentRunState.Validating] = AgentRunState.CollectingArtifacts,
            [AgentRunState.CollectingArtifacts] = AgentRunState.Completed,
        };

    public static bool IsTerminal(AgentRunState state) =>
        state is AgentRunState.Completed or AgentRunState.Failed or AgentRunState.Cancelled;

    public static bool CanTransition(AgentRunState current, AgentRunState next)
    {
        if (IsTerminal(current)) return false;
        if (ForwardTransitions.TryGetValue(current, out var forward) && forward == next) return true;
        return current != AgentRunState.Requested && next is AgentRunState.Failed or AgentRunState.Cancelled;
    }

    public static AppResult<AgentRunState> Transition(AgentRunState current, AgentRunState next) =>
        CanTransition(current, next)
            ? AppResult<AgentRunState>.Ok(next)
            : AppResult<AgentRunState>.Fail("invalid_run_transition",
                $"A run cannot transition from {current} to {next}.");
}

public static class AgentRunReplay
{
    public static AppResult ValidateNextSequence(long previousSequence, AgentRunEvent nextEvent)
    {
        if (previousSequence < 0 || nextEvent.Sequence != previousSequence + 1)
            return AppResult.Fail("invalid_event_sequence",
                $"Expected event sequence {previousSequence + 1}, received {nextEvent.Sequence}.");
        return AppResult.Ok();
    }

    public static AppResult<IReadOnlyList<AgentRunEvent>> AfterSequence(
        IEnumerable<AgentRunEvent> events,
        long afterSequence)
    {
        if (afterSequence < 0)
            return AppResult<IReadOnlyList<AgentRunEvent>>.Fail("invalid_event_sequence",
                "The replay cursor cannot be negative.");

        var ordered = events.OrderBy(item => item.Sequence).ToList();
        if (ordered.Any(item => !AgentRunContractValidator.ValidateEvent(item).Success) ||
            ordered.Select(item => item.RunId).Distinct(StringComparer.Ordinal).Skip(1).Any() ||
            ordered.Zip(ordered.Skip(1), (left, right) => right.Sequence == left.Sequence + 1).Any(valid => !valid))
            return AppResult<IReadOnlyList<AgentRunEvent>>.Fail("invalid_event_sequence",
                "Durable event sequences must belong to one run and be positive and contiguous.");

        return AppResult<IReadOnlyList<AgentRunEvent>>.Ok(
            ordered.Where(item => item.Sequence > afterSequence).ToList());
    }
}
