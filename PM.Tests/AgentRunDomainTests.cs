using System.Text.Json;
using System.Text.Json.Nodes;
using PM.AgentRuns;

namespace PM.Tests;

public class AgentRunDomainTests
{
    [Fact]
    public void ProtocolVersionsUseStableStringJsonAndMinorCompatibility()
    {
        var json = JsonSerializer.Serialize(AgentRunProtocol.Current, AgentRunJson.Options);

        Assert.Equal("\"1.2\"", json);
        Assert.Equal(AgentRunProtocol.Current,
            JsonSerializer.Deserialize<AgentRunProtocolVersion>(json, AgentRunJson.Options));
        Assert.True(AgentRunProtocol.IsCompatible(new AgentRunProtocolVersion(1, 0), new AgentRunProtocolVersion(1, 2)));
        Assert.False(AgentRunProtocol.IsCompatible(new AgentRunProtocolVersion(1, 3), new AgentRunProtocolVersion(1, 2)));
        Assert.False(AgentRunProtocol.IsCompatible(new AgentRunProtocolVersion(2, 0), new AgentRunProtocolVersion(1, 9)));
        Assert.Equal(AgentRunProtocol.Current,
            AgentRunProtocol.HighestCommon([AgentRunProtocol.Version10, AgentRunProtocol.Current]));
    }

    [Fact]
    public void AgentRunJsonUsesCanonicalUtcMillisecondTimestamps()
    {
        var timestamp = new DateTimeOffset(2026, 7, 29, 8, 50, 30, 123, TimeSpan.Zero);

        var json = JsonSerializer.Serialize(timestamp, AgentRunJson.Options);

        Assert.Equal("\"2026-07-29T08:50:30.123Z\"", json);
        Assert.Equal(timestamp, JsonSerializer.Deserialize<DateTimeOffset>(json, AgentRunJson.Options));
    }

    [Fact]
    public void CanonicalHashIsStableAndCoversNestedValuesAndOrder()
    {
        var specification = CreateSpecification();
        var first = AgentRunCanonicalJson.ComputeSpecificationHash(specification);

        Assert.Equal(first, AgentRunCanonicalJson.ComputeSpecificationHash(specification));
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.NotEqual(first, AgentRunCanonicalJson.ComputeSpecificationHash(
            specification with { Task = specification.Task with { Title = "Changed" } }));
        Assert.NotEqual(first, AgentRunCanonicalJson.ComputeSpecificationHash(
            specification with
            {
                Runtime = specification.Runtime with
                {
                    Profile = RebuildProfile(specification.Runtime.Profile,
                        specification.Runtime.Profile.Validation.Reverse().ToList()),
                },
            }));
    }

    [Fact]
    public void RequestValidationDetectsProfileAndSpecificationHashMismatch()
    {
        var specification = CreateSpecification();
        var request = CreateRequest(specification);

        Assert.True(AgentRunContractValidator.ValidateRequest(request).Success);

        var invalidProfile = specification with
        {
            Runtime = specification.Runtime with
            {
                Profile = specification.Runtime.Profile with
                {
                    Limits = specification.Runtime.Profile.Limits with { MemoryBytes = 4_294_967_296 },
                },
            },
        };
        var profileResult = AgentRunContractValidator.ValidateSpecification(invalidProfile);
        Assert.False(profileResult.Success);
        Assert.Equal("profile_revision_mismatch", profileResult.ErrorCode);

        var hashResult = AgentRunContractValidator.ValidateRequest(request with
        {
            SpecificationHash = new string('0', 64),
        });
        Assert.False(hashResult.Success);
        Assert.Equal("specification_hash_mismatch", hashResult.ErrorCode);
    }

    [Fact]
    public void LinkedWikiContextsAreCanonicalImmutableAndRequireProtocol12()
    {
        var specification = CreateSpecification() with
        {
            LinkedContexts =
            [
                new AgentRunLinkedContext(
                    "project-engine",
                    "Shared engine",
                    "engine",
                    new AgentRunRepository("git@github.com:chronium/engine.git", new string('c', 40)),
                    AgentRunLinkedContextRequirement.Required,
                    [AgentRunLinkedContextScope.Wiki]),
            ],
        };

        Assert.True(AgentRunContractValidator.ValidateSpecification(specification).Success);
        var hash = AgentRunCanonicalJson.ComputeSpecificationHash(specification);
        Assert.NotEqual(hash, AgentRunCanonicalJson.ComputeSpecificationHash(specification with
        {
            LinkedContexts =
            [
                specification.LinkedContexts[0] with
                {
                    Requirement = AgentRunLinkedContextRequirement.Optional,
                },
            ],
        }));

        var oldProtocol = AgentRunContractValidator.ValidateSpecification(specification with
        {
            ProtocolVersion = AgentRunProtocol.Version11,
        });
        Assert.False(oldProtocol.Success);
        Assert.Equal("invalid_run_specification", oldProtocol.ErrorCode);

        var unsafeProjectId = AgentRunContractValidator.ValidateSpecification(specification with
        {
            LinkedContexts = [specification.LinkedContexts[0] with { ProjectId = "../engine" }],
        });
        Assert.False(unsafeProjectId.Success);
    }

    [Fact]
    public void ProfileValidationRejectsMalformedEnvironmentAllowlist()
    {
        var profile = CreateSpecification().Runtime.Profile;
        var dotnetEnvironment = profile with
        {
            Container = profile.Container with
            {
                EnvironmentAllowlist =
                [
                    "CODEX_HOME", "DOTNET_CLI_HOME", "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO",
                    "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "HOME", "NUGET_PACKAGES", "PATH", "TMPDIR",
                ],
            },
        };
        dotnetEnvironment = RebuildProfile(dotnetEnvironment, dotnetEnvironment.Validation);
        Assert.True(AgentRunContractValidator.ValidateProfile(dotnetEnvironment).Success);

        var malformed = profile with
        {
            Container = profile.Container with { EnvironmentAllowlist = [null!] },
        };

        var result = AgentRunContractValidator.ValidateProfile(malformed);

        Assert.False(result.Success);
        Assert.Equal("invalid_run_specification", result.ErrorCode);
    }

    [Fact]
    public void RequestValidationRejectsTaskIdsThatCanEscapeTheTaskDirectory()
    {
        var specification = CreateSpecification();
        var malformed = specification with
        {
            Task = specification.Task with { TaskId = "../outside" },
        };

        var result = AgentRunContractValidator.ValidateSpecification(malformed);

        Assert.False(result.Success);
        Assert.Equal("invalid_run_specification", result.ErrorCode);
    }

    [Fact]
    public void DuplicateStartsAreIdempotentOnlyForTheSameHash()
    {
        var request = CreateRequest(CreateSpecification());

        var created = AgentRunContractValidator.EvaluateStart(null, request);
        var existing = AgentRunContractValidator.EvaluateStart(request.SpecificationHash, request);
        var conflict = AgentRunContractValidator.EvaluateStart(new string('a', 64), request);

        Assert.True(created.Success);
        Assert.Equal(AgentRunStartDisposition.New, created.Payload);
        Assert.True(existing.Success);
        Assert.Equal(AgentRunStartDisposition.Existing, existing.Payload);
        Assert.False(conflict.Success);
        Assert.Equal("run_id_conflict", conflict.ErrorCode);
    }

    [Fact]
    public void LifecycleAllowsForwardFailureAndCancellationTransitionsOnly()
    {
        var states = new[]
        {
            AgentRunState.Requested,
            AgentRunState.Accepted,
            AgentRunState.Queued,
            AgentRunState.PreparingWorkspace,
            AgentRunState.StartingRuntime,
            AgentRunState.StartingAgent,
            AgentRunState.Running,
            AgentRunState.Validating,
            AgentRunState.CollectingArtifacts,
            AgentRunState.Completed,
        };

        foreach (var pair in states.Zip(states.Skip(1)))
            Assert.True(AgentRunLifecycle.CanTransition(pair.First, pair.Second));

        Assert.False(AgentRunLifecycle.CanTransition(AgentRunState.Requested, AgentRunState.Failed));
        Assert.True(AgentRunLifecycle.CanTransition(AgentRunState.Running, AgentRunState.Failed));
        Assert.True(AgentRunLifecycle.CanTransition(AgentRunState.Validating, AgentRunState.Cancelled));
        Assert.False(AgentRunLifecycle.CanTransition(AgentRunState.Completed, AgentRunState.Failed));
        Assert.False(AgentRunLifecycle.CanTransition(AgentRunState.Running, AgentRunState.StartingAgent));
        Assert.Equal("invalid_run_transition",
            AgentRunLifecycle.Transition(AgentRunState.Completed, AgentRunState.Running).ErrorCode);
    }

    [Fact]
    public void ReplayIsExclusiveOrderedAndRequiresContiguousSequences()
    {
        var events = Enumerable.Range(1, 4)
            .Select(sequence => CreateEvent(sequence))
            .Reverse()
            .ToList();

        var replay = AgentRunReplay.AfterSequence(events, 2);
        Assert.True(replay.Success);
        Assert.Equal([3L, 4L], replay.Payload!.Select(item => item.Sequence));
        Assert.True(AgentRunReplay.ValidateNextSequence(4, CreateEvent(5)).Success);
        Assert.Equal("invalid_event_sequence", AgentRunReplay.ValidateNextSequence(4, CreateEvent(6)).ErrorCode);

        var gap = AgentRunReplay.AfterSequence([CreateEvent(1), CreateEvent(3)], 0);
        Assert.False(gap.Success);
        Assert.Equal("invalid_event_sequence", gap.ErrorCode);

        var otherRun = CreateEvent(2) with { RunId = "run-OTHER" };
        Assert.Equal("invalid_event_sequence",
            AgentRunReplay.AfterSequence([CreateEvent(1), otherRun], 0).ErrorCode);
    }

    [Fact]
    public void EventEnvelopeAllowsUnknownCompatibleEventTypes()
    {
        var runEvent = CreateEvent(1) with
        {
            Type = "system.storage_warning",
            Data = JsonSerializer.SerializeToElement(new { futureField = new { value = 42 } }),
        };
        var json = JsonSerializer.Serialize(runEvent, AgentRunJson.Options);
        var envelope = JsonNode.Parse(json)!.AsObject();
        envelope["futureEnvelopeField"] = true;
        envelope["data"]!["futureDataField"] = "preserved";
        var roundTrip = envelope.Deserialize<AgentRunEvent>(AgentRunJson.Options)!;

        Assert.Equal("system.storage_warning", roundTrip.Type);
        Assert.Equal("preserved", roundTrip.Data!.Value.GetProperty("futureDataField").GetString());
        Assert.True(AgentRunContractValidator.ValidateEvent(roundTrip).Success);
    }

    [Theory]
    [InlineData("not-namespaced")]
    [InlineData("Uppercase.output")]
    [InlineData("run.")]
    public void EventEnvelopeRejectsInvalidEventNames(string eventType)
    {
        var result = AgentRunContractValidator.ValidateEvent(CreateEvent(1) with { Type = eventType });

        Assert.False(result.Success);
        Assert.Equal("invalid_run_event", result.ErrorCode);
    }

    [Fact]
    public void EventEnvelopeRejectsUnknownLifecycleValues()
    {
        var result = AgentRunContractValidator.ValidateEvent(CreateEvent(1) with
        {
            State = (AgentRunState)999,
        });

        Assert.False(result.Success);
        Assert.Equal("invalid_run_event", result.ErrorCode);
    }

    [Fact]
    public void ContractJsonIgnoresUnknownAdditiveFields()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AgentRunContracts", "v1", "run-request.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["futureEnvelopeField"] = new JsonObject { ["enabled"] = true };
        document["specification"]!["project"]!["futureProjectField"] = "ignored";

        var request = document.Deserialize<AgentRunRequest>(AgentRunJson.Options)!;

        Assert.True(AgentRunContractValidator.ValidateRequest(request).Success);
    }

    [Fact]
    public void ContractFixturesDeserializeAndMatchCanonicalHashes()
    {
        var fixture = ReadFixture<AgentRunRequest>("run-request.json");
        Assert.True(AgentRunContractValidator.ValidateRequest(fixture).Success);

        var capabilities = ReadFixture<AgentRunnerCapabilities>("runner-capabilities.json");
        Assert.True(AgentRunContractValidator.ValidateCapabilities(capabilities).Success);

        var events = ReadFixture<List<AgentRunEvent>>("run-events.json");
        Assert.True(AgentRunReplay.AfterSequence(events, 0).Success);

        var artifact = ReadFixture<AgentRunArtifact>("artifact.json");
        Assert.True(AgentRunContractValidator.ValidateArtifact(artifact).Success);
    }

    private static T ReadFixture<T>(string name) where T : notnull
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AgentRunContracts", "v1", name);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), AgentRunJson.Options)
            ?? throw new InvalidOperationException($"Fixture {name} did not deserialize.");
    }

    private static AgentRunRequest CreateRequest(AgentRunSpecification specification) =>
        new(AgentRunCanonicalJson.ComputeSpecificationHash(specification), specification);

    private static AgentRunSpecification CreateSpecification()
    {
        var validation = new[]
        {
            new AgentRunValidationStep("build", "Build", "dotnet", ["build", "--no-restore"], ".", 600),
            new AgentRunValidationStep("test", "Test", "dotnet", ["test", "--no-restore"], ".", 900),
        };
        var profile = RebuildProfile(new AgentRunRuntimeProfile(
            "dotnet-10",
            string.Empty,
            "ghcr.io/chronium/pm-agent-dotnet@sha256:" + new string('b', 64),
            new AgentRunResourceLimits(4000, 8_589_934_592, 1024, 21_474_836_480, 10_800),
            new AgentRunNetworkPolicy("development-open", AgentRunNetworkMode.Open),
            new AgentRunContainerPolicy(
                "/workspace",
                "/home/pm/.codex",
                "/tmp",
                1_073_741_824,
                ["CODEX_HOME", "HOME", "PATH"],
                [],
                new AgentRunContainerSecurityPolicy(
                    true, "keep-id", true, true, true, "runtime-default", "none")),
            validation,
            new AgentRunOutputPolicy(AgentRunOutputMode.Patch, 10_485_760, true)), validation);

        return new AgentRunSpecification(
            AgentRunProtocol.Current,
            "run-01K0EXAMPLE",
            new DateTimeOffset(2026, 7, 27, 9, 30, 0, TimeSpan.Zero),
            new AgentRunProject("project-example", "PM π"),
            new AgentRunTask("PM-0001", "Define the agent protocol", new string('1', 64)),
            new AgentRunRepository("git@github.com:chronium/pm.git", new string('a', 40)),
            new AgentRunAgent("codex", "gpt-5.6-sol", "medium", "task-execution"),
            new AgentRunRuntime("linux-workstation", profile));
    }

    private static AgentRunRuntimeProfile RebuildProfile(
        AgentRunRuntimeProfile source,
        IReadOnlyList<AgentRunValidationStep> validation)
    {
        var withoutRevision = source with { Revision = string.Empty, Validation = validation };
        return withoutRevision with { Revision = AgentRunCanonicalJson.ComputeProfileRevision(withoutRevision) };
    }

    private static AgentRunEvent CreateEvent(long sequence) => new(
        AgentRunProtocol.Current,
        "run-01K0EXAMPLE",
        sequence,
        new DateTimeOffset(2026, 7, 27, 9, 30, 0, TimeSpan.Zero).AddSeconds(sequence),
        "run.progress",
        AgentRunState.Running,
        $"Event {sequence}",
        null);
}
