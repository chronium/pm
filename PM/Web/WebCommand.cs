using System.ComponentModel;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Web;

public class WebCommand(ProjectRoot projectRoot, BoardService boardService) : AsyncCommand<WebCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        var port = settings.Port ?? GetAvailablePort();
        var url = $"http://127.0.0.1:{port}";

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);

        var app = builder.Build();
        MapEndpoints(app, boardService);

        await app.StartAsync(cancellationToken);
        AnsiConsole.MarkupLineInterpolated($"Serving board at [green]{url.EscapeMarkup()}[/]");
        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            _ = Task.Run(() => app.StopAsync(CancellationToken.None));
        });

        try
        {
            await app.WaitForShutdownAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return 0;
    }

    public static void MapEndpoints(IEndpointRouteBuilder endpoints, BoardService boardService)
    {
        endpoints.MapGet("/favicon.ico", () => Results.NoContent());

        endpoints.MapGet("/", (HttpRequest request) =>
        {
            var board = CreateBoard(boardService, request);
            return Results.Content(BoardHtmlRenderer.RenderPage(board), "text/html; charset=utf-8");
        });

        endpoints.MapGet("/board", (HttpRequest request) =>
        {
            var board = CreateBoard(boardService, request);
            return Results.Content(BoardHtmlRenderer.RenderBoard(board), "text/html; charset=utf-8");
        });

        endpoints.MapGet("/task/{id}", (string id) =>
        {
            var result = boardService.GetBoard(new BoardQuery());
            if (!result.Success) return Results.NotFound("Task not found.");

            var task = result.Payload!.MilestoneGroups
                .SelectMany(milestone => milestone.States)
                .SelectMany(state => state.Tasks)
                .FirstOrDefault(task => task.Task.Id == id);

            return task == null
                ? Results.NotFound("Task not found.")
                : Results.Content(BoardHtmlRenderer.RenderTaskDetail(task), "text/html; charset=utf-8");
        });
    }

    private static BoardData CreateBoard(BoardService boardService, HttpRequest request)
    {
        var query = new BoardQuery(
            ReadQueryValue(request, "track"),
            ReadQueryValue(request, "milestone"),
            ReadQueryValue(request, "state"));

        var result = boardService.GetBoard(query);
        if (!result.Success) throw new InvalidOperationException(result.Message);

        return result.Payload!;
    }

    private static string? ReadQueryValue(HttpRequest request, string key)
    {
        var value = request.Query[key].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("--port <PORT>")]
        [Description("Localhost port to bind. Defaults to an available port.")]
        public int? Port { get; init; }
    }
}
