using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using PM.Web;

namespace PM.Tests;

public class AngularWebEndpointTests
{
    [Fact]
    public async Task ServesIndexAndFingerprintAssetWithExpectedHeaders()
    {
        var web = await CreateClient(new MemoryAssetStore(new Dictionary<string, string>
        {
            ["index.html"] = "<html>Angular</html>",
            ["chunk-ABCDEF12.js"] = "console.log('ready')",
            ["manifest.webmanifest"] = "{}",
        }));
        await using var app = web.App;
        using var client = web.Client;

        var index = await client.GetAsync("/");
        var asset = await client.GetAsync("/chunk-ABCDEF12.js");
        var manifest = await client.GetAsync("/manifest.webmanifest");

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal("<html>Angular</html>", await index.Content.ReadAsStringAsync());
        Assert.Equal("text/html", index.Content.Headers.ContentType?.MediaType);
        Assert.True(index.Headers.CacheControl?.Public);
        Assert.True(index.Headers.CacheControl?.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, index.Headers.CacheControl?.MaxAge);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", asset.Headers.CacheControl?.ToString());
        Assert.True(manifest.Headers.CacheControl?.Public);
        Assert.True(manifest.Headers.CacheControl?.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, manifest.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task HeadReturnsAssetHeadersWithoutBody()
    {
        var web = await CreateClient(new MemoryAssetStore(new Dictionary<string, string>
        {
            ["index.html"] = "index",
            ["styles-12345678.css"] = "body{}",
        }));
        await using var app = web.App;
        using var client = web.Client;

        using var request = new HttpRequestMessage(HttpMethod.Head, "/styles-12345678.css");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/tasks/PM-0001", HttpStatusCode.OK)]
    [InlineData("/task/PM-0001", HttpStatusCode.OK)]
    [InlineData("/missing.js", HttpStatusCode.NotFound)]
    [InlineData("/api/unknown", HttpStatusCode.NotFound)]
    [InlineData("/openapi/unknown", HttpStatusCode.NotFound)]
    [InlineData("/safe/%5cprivate", HttpStatusCode.NotFound)]
    public async Task SpaFallbackIsLimitedToSafeNonFileRoutes(string path, HttpStatusCode status)
    {
        var web = await CreateClient(new MemoryAssetStore(new Dictionary<string, string>
        {
            ["index.html"] = "Angular shell",
        }));
        await using var app = web.App;
        using var client = web.Client;

        var response = await client.GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.OK) Assert.Equal("Angular shell", await response.Content.ReadAsStringAsync());
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateClient(IAngularAssetStore assets)
    {
        var port = GetAvailablePort();
        var url = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);
        var app = builder.Build();
        app.MapAngularWeb(assets);
        await app.StartAsync();

        return (app, new HttpClient { BaseAddress = new Uri(url) });
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    internal sealed class MemoryAssetStore(IReadOnlyDictionary<string, string> values) : IAngularAssetStore
    {
        private readonly IReadOnlyDictionary<string, AngularAsset> assets = values.ToDictionary(
            pair => pair.Key,
            pair => new AngularAsset(Encoding.UTF8.GetBytes(pair.Value)),
            StringComparer.Ordinal);

        public bool HasAssets => assets.ContainsKey("index.html");
        public IReadOnlyCollection<string> Paths => assets.Keys.ToArray();
        public bool TryGet(string path, out AngularAsset asset) => assets.TryGetValue(path, out asset!);
    }
}
