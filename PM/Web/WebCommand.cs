using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PM.Api;
using PM.Application;
using PM.Auth;
using PM.Project;
using PM.Worker;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Web;

public class WebCommand(
    ProjectRoot projectRoot,
    BoardService boardService,
    TaskService taskService,
    ProjectConfigService configService,
    WikiService wikiService,
    ProjectValidationService validationService,
    IProjectMembershipService membershipService) : AsyncCommand<WebCommand.Settings>
{
    public WebCommand(
        ProjectRoot projectRoot,
        BoardService boardService,
        TaskService taskService,
        ProjectConfigService configService,
        WikiService wikiService,
        ProjectValidationService validationService)
        : this(projectRoot, boardService, taskService, configService, wikiService, validationService,
            new ProjectMembershipService(projectRoot, new IdentityService(), new PmWorkerClient(new HttpClient())))
    {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        var settingsError = ValidateSettings(settings);
        if (settingsError != null)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{settingsError.EscapeMarkup()}[/]");
            return 1;
        }

        IAngularAssetStore? angularAssets = null;
        if (!settings.Api)
        {
            angularAssets = CreateAngularAssetStore();
            if (!angularAssets.HasAssets)
            {
                AnsiConsole.MarkupLine(
                    "[red]Angular UI assets are not embedded. Run [green]npm run build[/] in web/, then publish with [green]dotnet publish PM/PM.csproj -p:EmbedAngularAssets=true[/].[/]");
                return 1;
            }
        }

        var port = settings.Port ?? (settings.Api ? 51237 : GetAvailablePort());
        var url = $"http://127.0.0.1:{port}";

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);
        ConfigureApiServices(builder.Services);

        var app = builder.Build();
        MapApiEndpoints(app, projectRoot, configService, validationService, boardService, taskService, wikiService,
            membershipService);
        if (!settings.Api) app.MapAngularWeb(angularAssets!);

        await app.StartAsync(cancellationToken);
        var subject = settings.Api ? "API" : "Angular UI";
        AnsiConsole.MarkupLineInterpolated($"Serving {subject.EscapeMarkup()} at [green]{url.EscapeMarkup()}[/]");
        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            _ = Task.Run(() => app.StopAsync(CancellationToken.None));
        });

        if (!settings.Api && settings.Open)
        {
            try
            {
                OpenBrowser(url);
            }
            catch (Exception exception)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[yellow]Unable to open browser automatically: {exception.Message.EscapeMarkup()}[/]");
            }
        }

        try
        {
            await app.WaitForShutdownAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return 0;
    }

    public static void ConfigureApiServices(IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        services.AddOpenApi("v1", options =>
            options.ShouldInclude = description =>
                description.RelativePath?.StartsWith("api/v1", StringComparison.Ordinal) == true);
    }

    public static void MapApiEndpoints(
        IEndpointRouteBuilder endpoints,
        ProjectRoot projectRoot,
        ProjectConfigService configService,
        ProjectValidationService validationService,
        BoardService boardService,
        TaskService taskService,
        WikiService wikiService,
        IProjectMembershipService? membershipService = null)
    {
        endpoints.MapApiV1(projectRoot, configService, validationService, boardService, taskService,
            wikiService, new ResourceRevisionService(projectRoot, boardService),
            membershipService: membershipService);
        endpoints.MapOpenApi("/openapi/{documentName}.json");
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    protected virtual void OpenBrowser(string url)
    {
        OpenUrlInDefaultBrowser(url);
    }

    protected virtual IAngularAssetStore CreateAngularAssetStore() => new EmbeddedAngularAssetStore();

    private static string? ValidateSettings(Settings settings)
    {
        if (settings.Api && settings.Open) return "--open cannot be combined with --api.";
        if (settings.Port is <= 0 or > 65535) return "--port must be between 1 and 65535.";
        return null;
    }

    private static void OpenUrlInDefaultBrowser(string url)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(url)
            {
                UseShellExecute = true,
            }
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", url)
                {
                    UseShellExecute = false,
                }
                : new ProcessStartInfo("xdg-open", url)
                {
                    UseShellExecute = false,
                };

        Process.Start(startInfo);
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--port <PORT>")]
        [Description("Localhost port to bind. Defaults to an available port.")]
        public int? Port { get; init; }

        [CommandOption("--open")]
        [Description("Open the board in the default browser after the server starts.")]
        public bool Open { get; init; }

        [CommandOption("--api")]
        [Description("Serve only the versioned API and OpenAPI document on loopback.")]
        public bool Api { get; init; }
    }
}
