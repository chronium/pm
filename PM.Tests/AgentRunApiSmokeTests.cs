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
    public async Task RealApiStartsInspectsReplaysAndCancelsRun()
    {
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
            new TaskService(projectRoot, new SmokeNextIdService()), new WikiService(projectRoot),
            new ResourceRevisionService(projectRoot, board), agentRunService: runService,
            agentRunnerClient: runnerClient);
        app.MapOpenApi("/openapi/{documentName}.json");
        await app.StartAsync();
        await using (app)
        using (var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") })
        {
            using var preflightRequest = Mutation(HttpMethod.Post, "/api/v1/runs/preflight", selection);
            var preflightResponse = await client.SendAsync(preflightRequest);
            Assert.Equal(HttpStatusCode.OK, preflightResponse.StatusCode);
            var preflight = await preflightResponse.Content.ReadFromJsonAsync<AgentRunPreflightResult>();
            Assert.True(preflight!.Ready,
                string.Join("; ", preflight.Checks.Where(check => check.Status == AgentRunPreflightCheckStatus.Failed)
                    .Select(check => check.Summary)));

            using var start = Mutation(HttpMethod.Post, $"/api/v1/runs/{preflight.RunId}/start", new { });
            start.Headers.TryAddWithoutValidation("If-Match", $"\"{preflight.Revision}\"");
            var startResponse = await client.SendAsync(start);
            var startBody = await startResponse.Content.ReadAsStringAsync();
            Assert.True(startResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK,
                $"Start returned {(int)startResponse.StatusCode}: {startBody}");

            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync($"/api/v1/runs/{preflight.RunId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync($"/api/v1/runs/{preflight.RunId}/events?afterSequence=0")).StatusCode);

            using var cancel = Mutation(HttpMethod.Post, $"/api/v1/runs/{preflight.RunId}/cancel", new { });
            var cancelResponse = await client.SendAsync(cancel);
            Assert.True(cancelResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted);
        }
    }

    private static HttpRequestMessage Mutation<T>(HttpMethod method, string path, T body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "agent-run-api-smoke");
        return request;
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");

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
}

public sealed class AgentRunApiSmokeFactAttribute : FactAttribute
{
    public AgentRunApiSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PM_AGENT_RUN_API_SMOKE") != "1")
            Skip = "Set PM_AGENT_RUN_API_SMOKE=1 and the runner selection variables to run the real API smoke.";
    }
}
