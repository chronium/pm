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

public class WebCommand(ProjectRoot projectRoot, BoardService boardService, TaskService taskService) : AsyncCommand<WebCommand.Settings>
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
        MapEndpoints(app, boardService, taskService);

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

    public static void MapEndpoints(IEndpointRouteBuilder endpoints, BoardService boardService, TaskService taskService)
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

            var board = result.Payload!;
            var task = FindTask(board, id);

            return task == null
                ? Results.NotFound("Task not found.")
                : Results.Content(BoardHtmlRenderer.RenderTaskDetail(task, board.States), "text/html; charset=utf-8");
        });

        endpoints.MapPost("/task/{id}/state", async (string id, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var targetState = form["targetState"].ToString();
            var mutation = taskService.MoveTask(id, targetState);
            if (!mutation.Success)
                return Results.Content(BoardHtmlRenderer.RenderDialogError(mutation.Message ?? "Task update failed."),
                    "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);

            var filteredBoard = CreateBoard(boardService, form);
            var taskBoard = boardService.GetBoard(new BoardQuery());
            if (!taskBoard.Success)
                return Results.Content(BoardHtmlRenderer.RenderDialogError(taskBoard.Message ?? "Task not found."),
                    "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);

            var task = FindTask(taskBoard.Payload!, id);
            return task == null
                ? Results.Content(BoardHtmlRenderer.RenderDialogError("Task not found."), "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest)
                : Results.Content(BoardHtmlRenderer.RenderTaskUpdate(filteredBoard, task), "text/html; charset=utf-8");
        });

        endpoints.MapPost("/task/{id}/remove", async (string id, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var mutation = taskService.RemoveTask(id);
            if (!mutation.Success)
                return Results.Content(BoardHtmlRenderer.RenderDialogError(mutation.Message ?? "Task removal failed."),
                    "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);

            var board = CreateBoard(boardService, form);
            return Results.Content(BoardHtmlRenderer.RenderTaskRemoval(board), "text/html; charset=utf-8");
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

    private static BoardData CreateBoard(BoardService boardService, IFormCollection form)
    {
        var query = new BoardQuery(
            ReadFormValue(form, "track"),
            ReadFormValue(form, "milestone"),
            ReadFormValue(form, "state"));

        var result = boardService.GetBoard(query);
        if (!result.Success) throw new InvalidOperationException(result.Message);

        return result.Payload!;
    }

    private static BoardTask? FindTask(BoardData board, string id)
    {
        return board.MilestoneGroups
            .SelectMany(milestone => milestone.States)
            .SelectMany(state => state.Tasks)
            .FirstOrDefault(task => task.Task.Id == id);
    }

    private static string? ReadQueryValue(HttpRequest request, string key)
    {
        var value = request.Query[key].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadFormValue(IFormCollection form, string key)
    {
        var value = form[key].ToString();
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
