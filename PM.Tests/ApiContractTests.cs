using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PM.Api;
using PM.Application;
using PM.Project;
using PM.Tasks;
using PM.Web;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task ProjectMetadataReturnsDirectCamelCaseJson()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(name: "Contract Project"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/project");
            var json = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = JsonDocument.Parse(json).RootElement;
            var revision = body.GetProperty("revision").GetString();
            Assert.Equal("Contract Project", body.GetProperty("name").GetString());
            Assert.Matches("^[0-9a-f]{64}$", revision!);
            Assert.Equal(ApiPreconditions.FormatETag(revision!), response.Headers.ETag?.Tag);
        }
    }

    [Theory]
    [InlineData("\"{revision}\"")]
    [InlineData("W/\"{revision}\"")]
    [InlineData("\"stale\", W/\"{revision}\"")]
    [InlineData("*")]
    public async Task ProjectConditionalGetReturnsEmptyNotModifiedResponse(string header)
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var revision = new ResourceRevisionService(root, new BoardService(root))
            .GetProjectConfigRevision().Payload!;
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/project");
            request.Headers.TryAddWithoutValidation("If-None-Match", header.Replace("{revision}", revision));
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.Equal(ApiPreconditions.FormatETag(revision), response.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task StaleProjectConditionalGetReturnsCurrentRepresentation()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/project");
            request.Headers.TryAddWithoutValidation("If-None-Match", "\"stale\"");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ProjectReloadsExternalConfigurationWithoutRestartingAndRetainsLastValidValue()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(name: "Before"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var before = await client.GetFromJsonAsync<ProjectResponse>("/api/v1/project");
            var changed = TestData.Config(name: "After");
            File.WriteAllText(root.ConfigPath, global::PM.YamlSerde.Serialize(changed));

            var after = await client.GetFromJsonAsync<ProjectResponse>("/api/v1/project");
            Assert.Equal("After", after!.Name);
            Assert.NotEqual(before!.Revision, after.Revision);

            File.WriteAllText(root.ConfigPath, "name: [unterminated");
            var invalid = await client.GetAsync("/api/v1/project");
            var problem = await invalid.Content.ReadFromJsonAsync<ApiProblemDetails>();
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_project", problem!.ErrorCode);
            Assert.Equal("After", root.Config!.Name);
        }
    }

    [Theory]
    [InlineData(null, HttpStatusCode.PreconditionRequired, "precondition_required", false)]
    [InlineData("\"stale\"", HttpStatusCode.PreconditionFailed, "precondition_failed", false)]
    [InlineData("malformed", HttpStatusCode.PreconditionFailed, "precondition_failed", false)]
    [InlineData("W/\"current\"", HttpStatusCode.PreconditionFailed, "precondition_failed", false)]
    [InlineData("\"current\"", HttpStatusCode.OK, null, true)]
    [InlineData("\"stale\", \"current\"", HttpStatusCode.OK, null, true)]
    [InlineData("*", HttpStatusCode.OK, null, true)]
    public async Task MutationPreconditionsRunBeforeServiceCallback(
        string? ifMatch,
        HttpStatusCode expectedStatus,
        string? expectedCode,
        bool mutated)
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var calls = 0;
        var (app, client) = await CreateApiClient(root, api =>
            api.MapPut("/revision-test", (HttpRequest request) =>
            {
                var failure = ApiPreconditions.RequireIfMatch(request, "current");
                if (failure != null) return failure;
                calls++;
                ApiPreconditions.SetETag(request.HttpContext.Response, "next");
                return Results.Ok(new { revision = "next" });
            }).WithRevisionedMutationMetadata());
        await using (app)
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/revision-test")
            {
                Content = JsonContent.Create(new { value = true }),
            };
            request.Headers.Add(ApiV1Endpoints.ClientHeader, "test");
            if (ifMatch != null)
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

            var response = await client.SendAsync(request);
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(mutated ? 1 : 0, calls);
            if (expectedCode != null)
            {
                var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();
                Assert.Equal(expectedCode, problem!.ErrorCode);
                Assert.Equal((int)expectedStatus, problem.Status);
                Assert.Equal($"https://pm.dev/problems/{expectedCode}", problem.Type);
                Assert.Equal("/api/v1/revision-test", problem.Instance);
                Assert.False(response.Headers.Contains("ETag"));
            }
            else
            {
                var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                Assert.Equal("next", body.GetProperty("revision").GetString());
                Assert.Equal("\"next\"", response.Headers.ETag?.Tag);
            }
        }
    }

    [Fact]
    public async Task OpenApiDocumentsRevisionedMutationPreconditions()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root, api =>
            api.MapPut("/revision-test", () => Results.Ok(new { revision = "next" }))
                .WithName("RevisionTest")
                .Produces<object>()
                .WithRevisionedMutationMetadata());
        await using (app)
        using (client)
        {
            var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
            var operation = document.GetProperty("paths").GetProperty("/api/v1/revision-test").GetProperty("put");
            var ifMatch = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
            Assert.Equal("If-Match", ifMatch.GetProperty("name").GetString());
            Assert.True(ifMatch.GetProperty("required").GetBoolean());
            Assert.True(operation.GetProperty("responses").TryGetProperty("412", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("428", out _));
            Assert.True(operation.GetProperty("responses").GetProperty("200").GetProperty("headers")
                .TryGetProperty("ETag", out _));
        }
    }

    [Fact]
    public async Task MissingProjectReturnsStableProblemDetails()
    {
        using var workspace = new TempWorkingDirectory();
        var (app, client) = await CreateApiClient(new ProjectRoot());
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/project");
            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("https://pm.dev/problems/missing_project", problem.GetProperty("type").GetString());
            Assert.Equal("Not Found", problem.GetProperty("title").GetString());
            Assert.Equal(404, problem.GetProperty("status").GetInt32());
            Assert.Contains("Project not found", problem.GetProperty("detail").GetString());
            Assert.Equal("/api/v1/project", problem.GetProperty("instance").GetString());
            Assert.Equal("missing_project", problem.GetProperty("errorCode").GetString());
        }
    }

    [Theory]
    [InlineData("missing_task", 404)]
    [InlineData("invalid_title", 400)]
    [InlineData("duplicate_task_id", 409)]
    [InlineData("status_in_use", 409)]
    [InlineData("next_id_unavailable", 503)]
    [InlineData("unknown_failure", 500)]
    public void ApplicationErrorsMapToStableStatusCategories(string errorCode, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ApiResults.StatusFor(errorCode));
    }

    [Fact]
    public async Task UnmappedFailuresAreSanitized()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root,
            api => api.MapGet("/failure", () => ApiResults.Failure("unknown_failure", "secret detail")));
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/failure");
            var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("An unexpected error occurred.", problem!.Detail);
            Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.BadRequest, "missing_client_header")]
    [InlineData("   ", null, HttpStatusCode.BadRequest, "missing_client_header")]
    [InlineData("angular", "text/plain", HttpStatusCode.UnsupportedMediaType, "unsupported_media_type")]
    public async Task MutationGuardRejectsUnsafeRequests(
        string? clientHeader,
        string? contentType,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root, AddMutationEndpoint);
        await using (app)
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test-mutation");
            request.Content = new StringContent("{}", Encoding.UTF8, contentType ?? "application/json");
            if (clientHeader != null) request.Headers.TryAddWithoutValidation(ApiV1Endpoints.ClientHeader, clientHeader);

            var response = await client.SendAsync(request);
            var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();

            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(expectedCode, problem!.ErrorCode);
        }
    }

    [Fact]
    public async Task MutationGuardAcceptsClientJsonAndDoesNotAffectGet()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root, AddMutationEndpoint);
        await using (app)
        using (client)
        {
            using var mutation = new HttpRequestMessage(HttpMethod.Post, "/api/v1/test-mutation")
            {
                Content = JsonContent.Create(new { value = true }),
            };
            mutation.Headers.Add(ApiV1Endpoints.ClientHeader, "angular");

            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(mutation)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/project")).StatusCode);
        }
    }

    [Fact]
    public async Task ApiDoesNotEnableCorsOrAuthorizePreflight()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            using var get = new HttpRequestMessage(HttpMethod.Get, "/api/v1/project");
            get.Headers.Add("Origin", "https://example.test");
            var getResponse = await client.SendAsync(get);

            using var options = new HttpRequestMessage(HttpMethod.Options, "/api/v1/project");
            options.Headers.Add("Origin", "https://example.test");
            options.Headers.Add("Access-Control-Request-Method", "GET");
            var optionsResponse = await client.SendAsync(options);

            Assert.False(getResponse.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.False(optionsResponse.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.NotEqual(HttpStatusCode.OK, optionsResponse.StatusCode);
            Assert.NotEqual(HttpStatusCode.NoContent, optionsResponse.StatusCode);
        }
    }

    [Fact]
    public async Task OpenApiContainsOnlyVersionedApiOperations()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root, mapLegacy: true);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/openapi/v1.json");
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            var paths = document.GetProperty("paths");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("3.1.", document.GetProperty("openapi").GetString());
            Assert.True(paths.TryGetProperty("/api/v1/project", out var project));
            Assert.True(project.TryGetProperty("get", out var get));
            Assert.Contains(get.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "If-None-Match" &&
                parameter.GetProperty("in").GetString() == "header");
            Assert.True(get.GetProperty("responses").TryGetProperty("304", out var notModified));
            Assert.True(notModified.GetProperty("headers").TryGetProperty("ETag", out _));
            Assert.True(get.GetProperty("responses").GetProperty("200").GetProperty("headers")
                .TryGetProperty("ETag", out _));
            Assert.False(paths.TryGetProperty("/", out _));
            Assert.False(paths.TryGetProperty("/board", out _));
        }
    }

    private static void AddMutationEndpoint(Microsoft.AspNetCore.Routing.RouteGroupBuilder api) =>
        api.MapPost("/test-mutation", () => Results.Ok(new { accepted = true }));

    private static async Task<(WebApplication App, HttpClient Client)> CreateApiClient(
        ProjectRoot projectRoot,
        Action<Microsoft.AspNetCore.Routing.RouteGroupBuilder>? configure = null,
        bool mapLegacy = false,
        INextIdService? nextIdService = null)
    {
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);
        WebCommand.ConfigureApiServices(builder.Services);
        var app = builder.Build();
        var configService = new ProjectConfigService(projectRoot);
        var boardService = new BoardService(projectRoot);
        app.MapApiV1(projectRoot, configService, new ProjectValidationService(projectRoot), boardService,
            new TaskService(projectRoot, nextIdService ?? new ApiNextIdService()),
            new ResourceRevisionService(projectRoot, boardService), configure);
        app.MapOpenApi("/openapi/{documentName}.json");
        if (mapLegacy)
            app.MapGet("/board", () => Results.Content("legacy", "text/html"));

        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(url) });
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class ApiNextIdService(bool healthy = true) : INextIdService
    {
        private int _nextId;
        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(++_nextId);
        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(_nextId + 1);
        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult<int?>(_nextId + 1);
        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectRegistration("api-test", "recovery-test"));
        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(healthy);
    }
}
