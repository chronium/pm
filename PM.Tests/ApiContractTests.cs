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
using PM.Web;

namespace PM.Tests;

public class ApiContractTests
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
            Assert.Equal("{\"name\":\"Contract Project\"}", json);
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
            Assert.True(project.TryGetProperty("get", out _));
            Assert.False(paths.TryGetProperty("/", out _));
            Assert.False(paths.TryGetProperty("/board", out _));
        }
    }

    private static void AddMutationEndpoint(Microsoft.AspNetCore.Routing.RouteGroupBuilder api) =>
        api.MapPost("/test-mutation", () => Results.Ok(new { accepted = true }));

    private static async Task<(WebApplication App, HttpClient Client)> CreateApiClient(
        ProjectRoot projectRoot,
        Action<Microsoft.AspNetCore.Routing.RouteGroupBuilder>? configure = null,
        bool mapLegacy = false)
    {
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);
        WebCommand.ConfigureApiServices(builder.Services);
        var app = builder.Build();
        var configService = new ProjectConfigService(projectRoot);
        app.MapApiV1(configService, configure);
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
}
