using System.Net;
using PM.Auth;
using PM.Project;
using PM.Tasks;
using PM.Worker;

namespace PM.Tests;

public class NextIdServiceTests
{
    [Fact]
    public async Task GetNextIdUsesProjectIdUrlAndAuthHeaders()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(nextIdServiceUrl: "http://ids.example.test"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile), "project-id");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":7}"""),
        });
        var service = CreateService(handler, workspace);

        var id = await service.GetNextId(projectRoot, "BUILD");

        Assert.Equal(7, id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ids.example.test/projects/project-id/tracks/BUILD/nextid", request.RequestUri!.ToString());
        AssertSigned(request);
    }

    [Fact]
    public async Task PeekExistingNextIdUsesProjectIdUrl()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(nextIdServiceUrl: "http://ids.example.test"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile), "project-id");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":3}"""),
        });
        var service = CreateService(handler, workspace);

        var id = await service.PeekExistingNextId(projectRoot, "PM");

        Assert.Equal(3, id);
        Assert.Equal("http://ids.example.test/projects/project-id/tracks/PM/peekid",
            handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task RegisterProjectWritesPublicProjectIdAndReturnsRecoveryKey()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(nextIdServiceUrl: "http://ids.example.test"));
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"projectId":"created-project"}"""),
        });
        var service = CreateService(handler, workspace);

        var registration = await service.RegisterProject(projectRoot);

        Assert.Equal("created-project", registration.ProjectId);
        Assert.StartsWith("pmrec_", registration.RecoveryKey);
        Assert.Equal("created-project",
            (await File.ReadAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile))).Trim());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ids.example.test/projects", request.RequestUri!.ToString());
        AssertSigned(request);
    }

    [Fact]
    public async Task LegacyNextIdKeyIsClaimedThenReplacedByProjectId()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(nextIdServiceUrl: "http://ids.example.test"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.LegacyNextIdFile), "legacy-key");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"projectId":"claimed-project"}"""),
        });
        var service = CreateService(handler, workspace);

        var registration = await service.RegisterProject(projectRoot);

        Assert.Equal("claimed-project", registration.ProjectId);
        Assert.Equal("claimed-project",
            (await File.ReadAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile))).Trim());
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ids.example.test/legacy-projects/claim", request.RequestUri!.ToString());
        AssertSigned(request);
    }

    private static NextIdService CreateService(RecordingHandler handler, TempWorkingDirectory workspace)
    {
        var identityPath = Path.Combine(workspace.Path, "identity.json");
        var identityService = new IdentityService(new IdentityServiceOptions { IdentityPath = identityPath });
        return new NextIdService(new PmWorkerClient(new HttpClient(handler)), identityService);
    }

    private static void AssertSigned(HttpRequestMessage request)
    {
        Assert.True(request.Headers.Contains("PM-User-Id"));
        Assert.True(request.Headers.Contains("PM-Timestamp"));
        Assert.True(request.Headers.Contains("PM-Nonce"));
        Assert.True(request.Headers.Contains("PM-Signature"));
        Assert.True(request.Headers.Contains("PM-Public-Key"));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(handler(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            return clone;
        }
    }
}
