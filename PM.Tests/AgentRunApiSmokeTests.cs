using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using PM.AgentRuns;
using PM.Api;
using PM.Application;
using PM.Auth;
using PM.Project;
using PM.Tasks;
using PM.Web;

namespace PM.Tests;

public class AgentRunApiSmokeTests
{
    [AgentRunApiSmokeFact]
    public async Task RealApiSurvivesControlPlaneRestartAndReturnsTerminalEvidence()
    {
        using var projectDirectory = new WorkingDirectoryScope(
            Required("PM_AGENT_RUN_API_SMOKE_PROJECT_ROOT"));
        var projectRoot = new ProjectRoot();
        Assert.True(projectRoot.Exists);
        var runnerId = Required("PM_AGENT_RUN_API_SMOKE_RUNNER");
        var selection = new AgentRunPreflightRequest(
            Required("PM_AGENT_RUN_API_SMOKE_TASK"),
            runnerId,
            Required("PM_AGENT_RUN_API_SMOKE_PROFILE"),
            Required("PM_AGENT_RUN_API_SMOKE_PROVIDER"),
            Required("PM_AGENT_RUN_API_SMOKE_MODEL"),
            Required("PM_AGENT_RUN_API_SMOKE_EFFORT"));
        var originalTask = new BoardService(projectRoot).GetTask(selection.TaskId);
        Assert.True(originalTask.Success, originalTask.Message);
        string runId;

        var first = await StartApi(projectRoot);
        await using (first.App)
        using (var client = new HttpClient { BaseAddress = first.Endpoint })
        {
            using var preflightRequest = Mutation(HttpMethod.Post, "/api/v1/runs/preflight", selection);
            var preflightResponse = await client.SendAsync(preflightRequest);
            var preflightBody = await preflightResponse.Content.ReadAsStringAsync();
            Assert.True(preflightResponse.StatusCode == HttpStatusCode.OK,
                $"Preflight returned {(int)preflightResponse.StatusCode}: {preflightBody}");
            var preflight = await preflightResponse.Content.ReadFromJsonAsync<AgentRunPreflightResult>();
            Assert.True(preflight!.Ready,
                string.Join("; ", preflight.Checks.Where(check => check.Status == AgentRunPreflightCheckStatus.Failed)
                    .Select(check => check.Summary)));
            runId = preflight.RunId!;

            using var start = Mutation(HttpMethod.Post, $"/api/v1/runs/{runId}/start", new { });
            start.Headers.TryAddWithoutValidation("If-Match", $"\"{preflight.Revision}\"");
            var startResponse = await client.SendAsync(start);
            var startBody = await startResponse.Content.ReadAsStringAsync();
            Assert.True(startResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK,
                $"Start returned {(int)startResponse.StatusCode}: {startBody}");

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/runs/{runId}")).StatusCode);
        }
        await first.App.StopAsync();

        await Task.Delay(TimeSpan.FromSeconds(DisconnectSeconds()));
        var second = await StartApi(projectRoot);
        await using (second.App)
        using (var client = new HttpClient { BaseAddress = second.Endpoint })
        {
            var inspection = await WaitForTerminal(client, runId);
            Assert.Equal(AgentRunState.Completed, inspection.Run.State);
            Assert.False(inspection.TaskChanged);

            var events = await client.GetFromJsonAsync<AgentRunEventPage>(
                $"/api/v1/runs/{runId}/events?afterSequence=0&limit=500");
            Assert.NotNull(events);
            Assert.True(events.Terminal);
            Assert.False(events.HasMore);
            Assert.NotEmpty(events.Events);
            Assert.Equal(Enumerable.Range(1, events.Events.Count).Select(value => (long)value),
                events.Events.Select(item => item.Sequence));
            Assert.DoesNotContain(events.Events, item => item.Type == "validation.failed");
            Assert.Contains(events.Events, item => item.Type == "validation.passed" &&
                                                   EventStepId(item) == "restore");
            Assert.Contains(events.Events, item => item.Type == "validation.passed" &&
                                                   EventStepId(item) == "build");
            Assert.Contains(events.Events, item => item.Type == "validation.passed" &&
                                                   EventStepId(item) == "test");

            var artifacts = await client.GetFromJsonAsync<IReadOnlyList<AgentRunArtifact>>(
                $"/api/v1/runs/{runId}/artifacts");
            Assert.NotNull(artifacts);
            Assert.Contains(artifacts, item => item.ArtifactId == "validation");
            Assert.Contains(artifacts, item => item.ArtifactId == "changes-summary");
            Assert.Contains(artifacts, item => item.ArtifactId == "manifest");
        }
        await second.App.StopAsync();

        var currentTask = new BoardService(projectRoot).GetTask(selection.TaskId);
        Assert.True(currentTask.Success, currentTask.Message);
        Assert.Equal(originalTask.Payload!.State, currentTask.Payload!.State);
    }

    private static async Task<(WebApplication App, Uri Endpoint)> StartApi(ProjectRoot projectRoot)
    {
        var runnerClient = new AgentRunnerClient(new AgentRunnerRegistrationStore(), new IdentityService());
        var board = new BoardService(projectRoot);
        var runService = new AgentRunService(projectRoot, board, new AgentRunGitInspector(),
            new AgentRunCache(projectRoot, TimeProvider.System), runnerClient, TimeProvider.System);
        var port = AvailablePort();
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        WebCommand.ConfigureApiServices(builder.Services);
        var app = builder.Build();
        app.MapApiV1(projectRoot, new ProjectConfigService(projectRoot),
            new ProjectValidationService(projectRoot), board,
            TestTaskServices.Create(projectRoot, new SmokeNextIdService()), new WikiService(projectRoot),
            new ResourceRevisionService(projectRoot, board), agentRunService: runService,
            agentRunnerClient: runnerClient);
        app.MapOpenApi("/openapi/{documentName}.json");
        await app.StartAsync();
        return (app, new Uri($"http://127.0.0.1:{port}"));
    }

    private static async Task<AgentRunInspection> WaitForTerminal(HttpClient client, string runId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        while (true)
        {
            var inspection = await client.GetFromJsonAsync<AgentRunInspection>(
                $"/api/v1/runs/{runId}", timeout.Token);
            Assert.NotNull(inspection);
            if (AgentRunLifecycle.IsTerminal(inspection.Run.State)) return inspection;
            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
        }
    }

    private static double DisconnectSeconds() =>
        double.TryParse(Environment.GetEnvironmentVariable("PM_AGENT_RUN_API_SMOKE_DISCONNECT_SECONDS"),
            out var seconds) && seconds >= 0
            ? seconds
            : 3;

    private static HttpRequestMessage Mutation<T>(HttpMethod method, string path, T body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "agent-run-api-smoke");
        return request;
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");

    private static string? EventStepId(AgentRunEvent item) =>
        item.Data is { } data && data.TryGetProperty("stepId", out var stepId)
            ? stepId.GetString()
            : null;

    private static int AvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class SmokeNextIdService : INextIdService
    {
        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class WorkingDirectoryScope : IDisposable
    {
        private readonly string originalDirectory = Environment.CurrentDirectory;

        public WorkingDirectoryScope(string directory)
        {
            var fullPath = Path.GetFullPath(directory);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"Smoke project directory does not exist: {fullPath}");
            Environment.CurrentDirectory = fullPath;
        }

        public void Dispose() => Environment.CurrentDirectory = originalDirectory;
    }
}

public sealed class AgentRunApiSmokeFactAttribute : FactAttribute
{
    public AgentRunApiSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PM_AGENT_RUN_API_SMOKE") != "1")
            Skip = "Set PM_AGENT_RUN_API_SMOKE=1 and the runner selection variables to run the real API smoke.";
    }
}
