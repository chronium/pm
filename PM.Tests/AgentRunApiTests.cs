using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using PM.AgentRuns;
using PM.Api;
using PM.Application;
using PM.Project;
using PM.Tasks;
using PM.Web;

namespace PM.Tests;

public class AgentRunApiTests
{
    [Fact]
    public async Task RunApiEnforcesClientAndPreflightRevisionThenProxiesLifecycle()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var runService = new FakeRunService();
        var runnerClient = new FakeRunnerClient();
        var (app, client) = await CreateApi(root, runService, runnerClient);
        await using (app)
        using (client)
        {
            var input = new AgentRunPreflightRequest("PM-0001", "runner-test", "dotnet-10",
                "codex", "gpt-5.6-sol", "medium");
            var missingClient = await client.PostAsJsonAsync("/api/v1/runs/preflight", input);
            Assert.Equal(HttpStatusCode.BadRequest, missingClient.StatusCode);

            using var preflightRequest = Post("/api/v1/runs/preflight", input);
            var preflight = await client.SendAsync(preflightRequest);
            Assert.Equal(HttpStatusCode.OK, preflight.StatusCode);
            Assert.Equal($"\"{runService.Revision}\"", preflight.Headers.ETag!.Tag);

            using var noMatch = Post($"/api/v1/runs/{runService.RunId}/start", new { });
            var noMatchResponse = await client.SendAsync(noMatch);
            Assert.Equal((HttpStatusCode)428, noMatchResponse.StatusCode);

            using var start = Post($"/api/v1/runs/{runService.RunId}/start", new { });
            start.Headers.TryAddWithoutValidation("If-Match", $"\"{runService.Revision}\"");
            var started = await client.SendAsync(start);
            Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
            Assert.Equal(runService.Revision, runService.ExpectedRevision);

            var inspected = await client.GetAsync($"/api/v1/runs/{runService.RunId}");
            Assert.Equal(HttpStatusCode.OK, inspected.StatusCode);
            Assert.NotNull(inspected.Headers.ETag);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync($"/api/v1/runs/{runService.RunId}/events")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync($"/api/v1/runs/{runService.RunId}/artifacts")).StatusCode);
            var artifactContent = await client.GetAsync(
                $"/api/v1/runs/{runService.RunId}/artifacts/changes-patch/content");
            Assert.Equal(HttpStatusCode.OK, artifactContent.StatusCode);
            Assert.Equal([0x64], await artifactContent.Content.ReadAsByteArrayAsync());
            Assert.Equal("changes-patch", artifactContent.Headers.GetValues("PM-Artifact-Id").Single());
            Assert.Equal(new string('d', 64),
                artifactContent.Headers.GetValues("PM-Artifact-SHA256").Single());
            Assert.Equal("no-store", artifactContent.Headers.CacheControl!.ToString());

            using var collectionPreflight = Post(
                $"/api/v1/runs/{runService.RunId}/patch-collection/preflight", new { });
            var collectionPreflightResponse = await client.SendAsync(collectionPreflight);
            Assert.Equal(HttpStatusCode.OK, collectionPreflightResponse.StatusCode);
            Assert.Equal($"\"{runService.PatchRevision}\"", collectionPreflightResponse.Headers.ETag!.Tag);

            using var collect = Post($"/api/v1/runs/{runService.RunId}/patch-collection/apply",
                new AgentRunPatchCollectionRequest(new string('d', 64)));
            collect.Headers.TryAddWithoutValidation("If-Match", $"\"{runService.PatchRevision}\"");
            var collected = await client.SendAsync(collect);
            Assert.Equal(HttpStatusCode.OK, collected.StatusCode);
            Assert.Equal(runService.PatchRevision, runService.ExpectedPatchRevision);

            var stream = await client.GetStringAsync($"/api/v1/runs/{runService.RunId}/events/stream");
            Assert.Contains("event: run-event", stream);
            Assert.Contains("event: stream-end", stream);
            Assert.Equal(1, runService.AdvancedSequence);
        }
    }

    [Fact]
    public async Task RunnerApiDoesNotEchoPairingCodesAndOpenApiAdvertisesRunSurface()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var runService = new FakeRunService();
        var runnerClient = new FakeRunnerClient();
        var (app, client) = await CreateApi(root, runService, runnerClient);
        await using (app)
        using (client)
        {
            const string code = "pairing-secret";
            using var pair = Post("/api/v1/runners/pair", new PairAgentRunnerRequest(
                "https://runner.test/", "runner-test", $"sha256:{new string('a', 64)}", code));
            var response = await client.SendAsync(pair);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.DoesNotContain(code, body, StringComparison.Ordinal);
            Assert.Equal(code, runnerClient.PairingCode);

            var status = await client.GetAsync("/api/v1/runners/runner-test/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.NotNull(status.Headers.ETag);

            using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
            var paths = document.RootElement.GetProperty("paths");
            Assert.True(paths.TryGetProperty("/api/v1/runs/preflight", out _));
            Assert.True(paths.TryGetProperty("/api/v1/runs/{runId}/events/stream", out _));
            Assert.True(paths.TryGetProperty(
                "/api/v1/runs/{runId}/artifacts/{artifactId}/content", out _));
            Assert.True(paths.TryGetProperty(
                "/api/v1/runs/{runId}/patch-collection/preflight", out _));
            Assert.True(paths.TryGetProperty(
                "/api/v1/runs/{runId}/patch-collection/apply", out _));
            Assert.True(paths.TryGetProperty("/api/v1/runners/{runnerId}/rotate", out _));
        }
    }

    private static HttpRequestMessage Post<T>(string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "agent-api-test");
        return request;
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateApi(
        ProjectRoot root,
        IAgentRunService runs,
        IAgentRunnerClient runners)
    {
        var port = AvailablePort();
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        WebCommand.ConfigureApiServices(builder.Services);
        var app = builder.Build();
        var board = new BoardService(root);
        app.MapApiV1(root, new ProjectConfigService(root), new ProjectValidationService(root), board,
            new TaskService(root, new StubNextIdService()), new WikiService(root),
            new ResourceRevisionService(root, board), agentRunService: runs, agentRunnerClient: runners);
        app.MapOpenApi("/openapi/{documentName}.json");
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") });
    }

    private static int AvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FakeRunService : IAgentRunService
    {
        public string RunId { get; } = "run-api-test";
        public string Revision { get; } = new('c', 64);
        public string PatchRevision { get; } = new('e', 64);
        public string? ExpectedRevision { get; private set; }
        public string? ExpectedPatchRevision { get; private set; }
        public long AdvancedSequence { get; private set; }
        private AgentRunRequest Request => Fixture(RunId);
        private AgentRunnerRun Run => new(RunId, Request.SpecificationHash, Request.Specification,
            AgentRunState.Accepted, 1, Request.Specification.RequestedAt, Request.Specification.RequestedAt,
            null, null, null);

        public Task<AppResult<AgentRunPreflightResult>> Preflight(AgentRunSelection selection,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunPreflightResult>.Ok(new AgentRunPreflightResult(true, RunId, Revision, Request,
                [new AgentRunPreflightCheck("ready", "Ready", AgentRunPreflightCheckStatus.Passed, "Ready.")])));
        public Task<AppResult<AgentRunRemoteStart>> Start(string runId, string expectedRevision,
            CancellationToken cancellationToken = default)
        {
            ExpectedRevision = expectedRevision;
            return Task.FromResult(AppResult<AgentRunRemoteStart>.Ok(
                new AgentRunRemoteStart(AgentRunRemoteStartDisposition.New, Run)));
        }
        public Task<AppResult<AgentRunInspection>> Inspect(string runId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunInspection>.Ok(new AgentRunInspection(Run, false,
                Request.Specification.Task.Revision, Revision)));
        public Task<AppResult<AgentRunnerRunPage>> ActiveRuns(string runnerId, int limit, string? cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunnerRunPage>.Ok(new AgentRunnerRunPage([], null, false)));
        public Task<AppResult<AgentRunEventPage>> Events(string runId, long afterSequence, int limit,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunEventPage>.Ok(new AgentRunEventPage([], afterSequence, false, false)));
        public Task<AppResult<IAgentRunnerEventStream>> OpenEventStream(string runId, long afterSequence,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<IAgentRunnerEventStream>.Ok(new FakeStream(RunId)));
        public Task<AppResult> AdvanceSequence(string runId, long sequence)
        {
            AdvancedSequence = sequence;
            return Task.FromResult(AppResult.Ok());
        }
        public Task<AppResult<AgentRunCancellation>> Cancel(string runId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunCancellation>.Ok(new AgentRunCancellation("requested", Run)));
        public Task<AppResult<IReadOnlyList<AgentRunArtifact>>> Artifacts(string runId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<IReadOnlyList<AgentRunArtifact>>.Ok([]));
        public Task<AppResult<AgentRunArtifact>> Artifact(string runId, string artifactId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunArtifact>.Ok(new AgentRunArtifact(artifactId, "git_patch", "change.patch",
                "text/x-diff", 1, new string('d', 64), Request.Specification.RequestedAt)));
        public Task<AppResult<IAgentRunArtifactContent>> ArtifactContent(string runId, string artifactId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<IAgentRunArtifactContent>.Ok(new FakeArtifactContent(
                new AgentRunArtifact(artifactId, "git_patch", "change.patch", "text/x-diff", 1,
                    new string('d', 64), Request.Specification.RequestedAt), [0x64])));
        public Task<AppResult<AgentRunPatchPreflightResult>> PreflightPatchCollection(string runId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunPatchPreflightResult>.Ok(new AgentRunPatchPreflightResult(
                true, PatchRevision, "changes-patch", new string('d', 64),
                Request.Specification.Repository.BaseCommit, Request.Specification.Repository.BaseCommit,
                Request.Specification.Task.Revision, Request.Specification.Task.Revision,
                [new AgentRunPreflightCheck("ready", "Ready", AgentRunPreflightCheckStatus.Passed, "Ready.")],
                [], [new AgentRunPatchPath("PM/test.cs", "modified", 1, 0, false)],
                new AgentRunPatchStatistics(1, 1, 0, 0))));
        public Task<AppResult<AgentRunPatchCollectionResult>> CollectPatch(string runId,
            string expectedRevision, string expectedArtifactSha256,
            CancellationToken cancellationToken = default)
        {
            ExpectedPatchRevision = expectedRevision;
            return Task.FromResult(AppResult<AgentRunPatchCollectionResult>.Ok(
                new AgentRunPatchCollectionResult(runId, "changes-patch", expectedArtifactSha256,
                    Request.Specification.Repository.BaseCommit, Request.Specification.Repository.BaseCommit,
                    ["PM/test.cs"], Request.Specification.RequestedAt)));
        }
    }

    private sealed class FakeStream(string runId) : IAgentRunnerEventStream
    {
        public async IAsyncEnumerable<AgentRunStreamMessage> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var timestamp = DateTimeOffset.Parse("2026-07-27T09:30:01.000Z");
            yield return AgentRunStreamMessage.Durable(new AgentRunEvent(AgentRunProtocol.Current, runId, 1,
                timestamp, "run.state_changed", AgentRunState.Accepted, "Accepted", null));
            yield return AgentRunStreamMessage.Terminal(new AgentRunStreamEnd(AgentRunState.Completed, 1));
            await Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeArtifactContent(AgentRunArtifact artifact, byte[] bytes) : IAgentRunArtifactContent
    {
        public AgentRunArtifact Artifact { get; } = artifact;
        public Stream Content { get; } = new MemoryStream(bytes, writable: false);
        public async ValueTask DisposeAsync() => await Content.DisposeAsync();
    }

    private sealed class FakeRunnerClient : IAgentRunnerClient
    {
        private static readonly AgentRunnerRegistration RegistrationValue = new("runner-test", "Runner",
            new Uri("https://runner.test/"), $"sha256:{new string('a', 64)}", AgentRunProtocol.Current,
            "client-test", $"sha256:{new string('b', 64)}", DateTimeOffset.Parse("2026-07-27T09:30:00Z"));
        private static readonly AgentRunnerCapabilities CapabilitiesValue =
            JsonSerializer.Deserialize<AgentRunnerCapabilities>(File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "AgentRunContracts", "v1", "runner-capabilities.json")),
                AgentRunJson.Options)! with { RunnerId = "runner-test" };
        public string? PairingCode { get; private set; }
        public AppResult<IReadOnlyList<AgentRunnerRegistration>> Registrations() =>
            AppResult<IReadOnlyList<AgentRunnerRegistration>>.Ok([RegistrationValue]);
        public AppResult<AgentRunnerRegistration> Registration(string runnerId) =>
            AppResult<AgentRunnerRegistration>.Ok(RegistrationValue);
        public Task<AppResult<AgentRunnerRegistration>> Pair(AgentRunnerPairingRequest request,
            CancellationToken cancellationToken = default)
        {
            PairingCode = request.PairingCode;
            return Task.FromResult(AppResult<AgentRunnerRegistration>.Ok(RegistrationValue));
        }
        public Task<AppResult<AgentRunnerHealth>> Health(string runnerId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunnerHealth>.Ok(new AgentRunnerHealth(runnerId, "online", AgentRunProtocol.Current,
                DateTimeOffset.Parse("2026-07-27T09:30:00Z"))));
        public Task<AppResult<AgentRunnerCapabilities>> Capabilities(string runnerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<AgentRunnerCapabilities>.Ok(CapabilitiesValue));
        public Task<AppResult<AgentRunRemoteStart>> Start(string runnerId, AgentRunRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<AgentRunnerRun>> Inspect(string runnerId, string runId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<AgentRunnerRunPage>> ActiveRuns(string runnerId, int limit = 100,
            string? cursor = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<AgentRunEventPage>> Events(string runnerId, string runId, long afterSequence = 0,
            int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<IAgentRunnerEventStream>> OpenEventStream(string runnerId, string runId,
            long afterSequence = 0, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<AgentRunCancellation>> Cancel(string runnerId, string runId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<IReadOnlyList<AgentRunArtifact>>> Artifacts(string runnerId, string runId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<AgentRunArtifact>> Artifact(string runnerId, string runId, string artifactId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<IAgentRunArtifactContent>> ArtifactContent(string runnerId, string runId,
            string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<AgentRunnerRegistration>> Rotate(string runnerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<AgentRunnerRegistration>.Ok(RegistrationValue));
        public Task<AppResult> Revoke(string runnerId,
            CancellationToken cancellationToken = default) => Task.FromResult(AppResult.Ok());
    }

    private sealed class StubNextIdService : INextIdService
    {
        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult<int?>(1);
        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectRegistration("test", null));
        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static AgentRunRequest Fixture(string runId)
    {
        var value = JsonSerializer.Deserialize<AgentRunRequest>(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "AgentRunContracts", "v1", "run-request.json")), AgentRunJson.Options)!;
        var specification = value.Specification with { RunId = runId };
        return new AgentRunRequest(AgentRunCanonicalJson.ComputeSpecificationHash(specification), specification);
    }
}
