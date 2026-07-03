using System.Net;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public class NextIdServiceTests
{
    [Fact]
    public async Task GetNextIdUsesTrackScopedUrl()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(nextIdServiceUrl: "http://ids.example.test"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.NextIdFile), "project-key");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":7}"""),
        });
        var service = new NextIdService(new HttpClient(handler));

        var id = await service.GetNextId(projectRoot, "BUILD");

        Assert.Equal(7, id);
        Assert.Equal("http://ids.example.test/projects/project-key/tracks/BUILD/nextid",
            handler.Requests.Single().ToString());
    }

    [Fact]
    public async Task PeekExistingNextIdUsesTrackScopedUrl()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject(TestData.Config(nextIdServiceUrl: "http://ids.example.test"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.NextIdFile), "project-key");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":3}"""),
        });
        var service = new NextIdService(new HttpClient(handler));

        var id = await service.PeekExistingNextId(projectRoot, "PM");

        Assert.Equal(3, id);
        Assert.Equal("http://ids.example.test/projects/project-key/tracks/PM/peekid",
            handler.Requests.Single().ToString());
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(handler(request));
        }
    }
}
