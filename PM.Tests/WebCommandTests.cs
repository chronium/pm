using System.Net;
using PM.Application;
using PM.Project;
using PM.Tasks;
using PM.Web;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PM.Tests;

public class WebCommandTests
{
    [Fact]
    public async Task WebOutsideProjectReturnsOne()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = new ProjectRoot();
        var command = CreateWebCommand(projectRoot);

        var (exitCode, output) = await ExecuteWebCommand(command, new WebCommand.Settings());

        Assert.Equal(1, exitCode);
        Assert.Contains("Project not found", output);
    }

    [Fact]
    public async Task WebOpenFlagLaunchesResolvedLocalUrl()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var port = GetAvailablePort();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string? openedUrl = null;
        var command = new RecordingOpenWebCommand(projectRoot, url =>
        {
            openedUrl = url;
            cancellation.Cancel();
        }, new AngularWebEndpointTests.MemoryAssetStore(new Dictionary<string, string>
        {
            ["index.html"] = "Angular",
        }));

        var (exitCode, output) = await ExecuteWebCommand(command, new WebCommand.Settings
        {
            Port = port,
            Open = true,
        }, cancellation.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal($"http://127.0.0.1:{port}", openedUrl);
        Assert.Contains($"Serving Angular UI at http://127.0.0.1:{port}", output);
    }

    [Fact]
    public async Task WebRejectsApiAndOpenBeforeStarting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();

        var (exitCode, output) = await ExecuteWebCommand(CreateWebCommand(projectRoot), new WebCommand.Settings
        {
            Api = true,
            Open = true,
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("--open cannot be combined with --api", output);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task WebRejectsInvalidPortsBeforeStarting(int port)
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();

        var (exitCode, output) = await ExecuteWebCommand(CreateWebCommand(projectRoot),
            new WebCommand.Settings { Port = port });

        Assert.Equal(1, exitCode);
        Assert.Contains("--port must be between 1 and 65535", output);
    }

    [Fact]
    public async Task WebWithoutEmbeddedAssetsFailsBeforeStarting()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var opened = false;
        var command = new RecordingOpenWebCommand(projectRoot, _ => opened = true);

        var (exitCode, output) = await ExecuteWebCommand(command, new WebCommand.Settings { Open = true });

        Assert.Equal(1, exitCode);
        Assert.Contains("Angular UI assets are not embedded", output);
        Assert.DoesNotContain("legacy", output, StringComparison.OrdinalIgnoreCase);
        Assert.False(opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiModeUsesDefaultOrCustomPortAndMapsOnlyApiEndpoints(bool customPort)
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var port = customPort ? GetAvailablePort() : 51237;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = ExecuteWebCommand(CreateWebCommand(projectRoot), new WebCommand.Settings
        {
            Api = true,
            Port = customPort ? port : null,
        }, cancellation.Token);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        try
        {
            HttpResponseMessage? project = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    project = await client.GetAsync("/api/v1/project", cancellation.Token);
                    break;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(25, cancellation.Token);
                }
            }

            Assert.NotNull(project);
            Assert.Equal(HttpStatusCode.OK, project.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json", cancellation.Token)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/", cancellation.Token)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tasks/PM-0001", cancellation.Token)).StatusCode);
        }
        finally
        {
            cancellation.Cancel();
        }

        var (exitCode, output) = await execution;
        Assert.Equal(0, exitCode);
        Assert.Contains($"Serving API at http://127.0.0.1:{port}", output);
    }

    private static WebCommand CreateWebCommand(ProjectRoot projectRoot) => new(
        projectRoot,
        TestBoardServices.Create(projectRoot),
        TestTaskServices.Create(projectRoot, new RecordingNextIdService()),
        new ProjectConfigService(projectRoot),
        new WikiService(projectRoot),
        new ProjectValidationService(projectRoot));

    private static async Task<(int ExitCode, string Output)> ExecuteWebCommand(
        WebCommand command,
        WebCommand.Settings settings,
        CancellationToken cancellationToken = default)
    {
        var originalConsole = AnsiConsole.Console;
        using var writer = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new FixedWidthConsoleOutput(writer),
        });

        try
        {
            var exitCode = await command.ExecuteAsync(null!, settings, cancellationToken);
            return (exitCode, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FixedWidthConsoleOutput(TextWriter writer) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => false;
        public int Width => 240;
        public int Height => 80;

        public void SetEncoding(System.Text.Encoding encoding)
        {
        }
    }

    private sealed class RecordingOpenWebCommand(
        ProjectRoot projectRoot,
        Action<string> onOpen,
        IAngularAssetStore? angularAssets = null) : WebCommand(
        projectRoot,
        TestBoardServices.Create(projectRoot),
        TestTaskServices.Create(projectRoot, new RecordingNextIdService()),
        new ProjectConfigService(projectRoot),
        new WikiService(projectRoot),
        new ProjectValidationService(projectRoot))
    {
        protected override void OpenBrowser(string url) => onOpen(url);

        protected override IAngularAssetStore CreateAngularAssetStore() =>
            angularAssets ?? base.CreateAngularAssetStore();
    }

    private sealed class RecordingNextIdService : INextIdService
    {
        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult<int?>(1);

        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectRegistration("project-test", "recovery-test"));

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
