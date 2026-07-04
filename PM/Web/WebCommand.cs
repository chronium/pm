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

public class WebCommand(
    ProjectRoot projectRoot,
    BoardService boardService,
    TaskService taskService,
    ProjectConfigService configService,
    WikiService wikiService) : AsyncCommand<WebCommand.Settings>
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
        MapEndpoints(app, boardService, taskService, configService, wikiService);

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

    public static void MapEndpoints(
        IEndpointRouteBuilder endpoints,
        BoardService boardService,
        TaskService taskService,
        ProjectConfigService configService,
        WikiService wikiService)
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

        endpoints.MapGet("/settings", () =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var settings = CreateSettings(configService);
            return Results.Content(BoardHtmlRenderer.RenderSettingsPage(board, settings), "text/html; charset=utf-8");
        });

        endpoints.MapGet("/wiki", () =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var result = wikiService.ListPages();
            return !result.Success
                ? WikiError(result)
                : Results.Content(BoardHtmlRenderer.RenderWikiIndexPage(board, result.Payload!),
                    "text/html; charset=utf-8");
        });

        endpoints.MapGet("/wiki/new", () =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            return Results.Content(BoardHtmlRenderer.RenderWikiCreatePage(board), "text/html; charset=utf-8");
        });

        endpoints.MapPost("/wiki/new", async (HttpRequest request) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var form = await request.ReadFormAsync();
            var path = form["path"].ToString();
            var title = form["title"].ToString();
            var markdown = form["markdown"].ToString();
            var result = wikiService.CreatePage(path, title, markdown);
            if (!result.Success)
                return Results.Content(
                    BoardHtmlRenderer.RenderWikiCreatePage(board, path, title, markdown, result.Message),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);

            return Results.Redirect($"/wiki/{BoardHtmlRenderer.WikiPathUrl(result.Payload!.Path)}");
        });

        endpoints.MapGet("/wiki/edit/{**path}", (string path) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var result = wikiService.ReadPage(path);
            return !result.Success
                ? WikiError(result)
                : Results.Content(BoardHtmlRenderer.RenderWikiEditPage(board, result.Payload!),
                    "text/html; charset=utf-8");
        });

        endpoints.MapPost("/wiki/edit/{**path}", async (string path, HttpRequest request) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var form = await request.ReadFormAsync();
            var markdown = form["markdown"].ToString();
            var result = wikiService.UpdatePageMarkdown(path, markdown);
            if (!result.Success)
            {
                var readResult = wikiService.ReadPage(path);
                var title = readResult.Success ? readResult.Payload!.Title : path;
                return Results.Content(
                    BoardHtmlRenderer.RenderWikiEditPage(board, path, title, markdown, result.Message),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Redirect($"/wiki/{BoardHtmlRenderer.WikiPathUrl(result.Payload!.Path)}");
        });

        endpoints.MapGet("/wiki/{**path}", (string path) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var result = wikiService.ReadPage(path);
            return !result.Success
                ? WikiError(result)
                : Results.Content(BoardHtmlRenderer.RenderWikiPage(board, result.Payload!),
                    "text/html; charset=utf-8");
        });

        endpoints.MapPost("/settings/statuses", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, configService.AddStatus(
                form["key"].ToString(),
                form["name"].ToString()));
        });

        endpoints.MapPost("/settings/statuses/{key}/rename", async (string key, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, configService.RenameStatus(key, form["name"].ToString()));
        });

        endpoints.MapPost("/settings/statuses/{key}/remove", (string key) =>
            SettingsMutation(configService, configService.RemoveStatus(key)));

        endpoints.MapPost("/settings/tracks", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, configService.AddTrack(
                form["key"].ToString(),
                form["name"].ToString()));
        });

        endpoints.MapPost("/settings/tracks/{key}/rename", async (string key, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, configService.RenameTrack(key, form["name"].ToString()));
        });

        endpoints.MapPost("/settings/tracks/{key}/remove", (string key) =>
            SettingsMutation(configService, configService.RemoveTrack(key)));

        endpoints.MapPost("/settings/milestones", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, configService.AddMilestone(
                form["key"].ToString(),
                form["title"].ToString()));
        });

        endpoints.MapPost("/settings/milestones/{key}/rename", async (string key, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, configService.RenameMilestone(key, form["title"].ToString()));
        });

        endpoints.MapPost("/settings/milestones/{key}/remove", (string key) =>
            SettingsMutation(configService, configService.RemoveMilestone(key)));

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

        endpoints.MapGet("/task/new", (HttpRequest request) =>
        {
            var board = CreateBoard(boardService, request);
            return Results.Content(BoardHtmlRenderer.RenderTaskCreateForm(board), "text/html; charset=utf-8");
        });

        endpoints.MapPost("/task/new", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var result = await taskService.CreateTask(
                form["title"].ToString(),
                form["track"].ToString(),
                ReadFormValue(form, "milestone"),
                form["description"].ToString(),
                false,
                request.HttpContext.RequestAborted);

            if (!result.Success)
                return Results.Content(
                    BoardHtmlRenderer.RenderDialogError(result.Message ?? "Task creation failed.",
                        "Unable to create task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);

            var filteredBoard = CreateBoard(boardService, form);
            var taskBoard = boardService.GetBoard(new BoardQuery());
            if (!taskBoard.Success)
                return Results.Content(
                    BoardHtmlRenderer.RenderDialogError(taskBoard.Message ?? "Task not found.",
                        "Unable to create task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);

            var task = FindTask(taskBoard.Payload!, result.Payload!.Id);
            return task == null
                ? Results.Content(
                    BoardHtmlRenderer.RenderDialogError("Created task was not found.", "Unable to create task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest)
                : Results.Content(BoardHtmlRenderer.RenderTaskCreated(filteredBoard, task), "text/html; charset=utf-8");
        });

        endpoints.MapGet("/task/{id}/edit", (string id, HttpRequest request) =>
        {
            var result = taskService.ReadTaskMarkdown(id);
            return !result.Success
                ? Results.Content(
                    BoardHtmlRenderer.RenderDialogError(result.Message ?? "Task not found.", "Unable to edit task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest)
                : Results.Content(
                    BoardHtmlRenderer.RenderTaskEditForm(id, result.Payload!, ReadQuery(request)),
                    "text/html; charset=utf-8");
        });

        endpoints.MapPost("/task/{id}/edit", async (string id, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var result = taskService.SaveEditedTaskContent(id, form["markdown"].ToString());
            if (!result.Success)
                return Results.Content(
                    BoardHtmlRenderer.RenderDialogError(result.Message ?? "Task edit failed.", "Unable to edit task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);

            var filteredBoard = CreateBoard(boardService, form);
            var taskBoard = boardService.GetBoard(new BoardQuery());
            if (!taskBoard.Success)
                return Results.Content(
                    BoardHtmlRenderer.RenderDialogError(taskBoard.Message ?? "Task not found.",
                        "Unable to edit task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);

            var task = FindTask(taskBoard.Payload!, id);
            return task == null
                ? Results.Content(BoardHtmlRenderer.RenderDialogError("Task not found.", "Unable to edit task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest)
                : Results.Content(BoardHtmlRenderer.RenderTaskUpdate(filteredBoard, task), "text/html; charset=utf-8");
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
        var query = ReadQuery(request);

        var result = boardService.GetBoard(query);
        if (!result.Success) throw new InvalidOperationException(result.Message);

        return result.Payload!;
    }

    private static BoardData CreateBoard(BoardService boardService, BoardQuery query)
    {
        var result = boardService.GetBoard(query);
        if (!result.Success) throw new InvalidOperationException(result.Message);

        return result.Payload!;
    }

    private static BoardData CreateBoard(BoardService boardService, IFormCollection form)
    {
        var query = new BoardQuery(
            ReadFormValue(form, "filterTrack") ?? ReadFormValue(form, "track"),
            ReadFormValue(form, "filterMilestone") ?? ReadFormValue(form, "milestone"),
            ReadFormValue(form, "filterState") ?? ReadFormValue(form, "state"));

        var result = boardService.GetBoard(query);
        if (!result.Success) throw new InvalidOperationException(result.Message);

        return result.Payload!;
    }

    private static BoardQuery ReadQuery(HttpRequest request)
    {
        return new BoardQuery(
            ReadQueryValue(request, "track"),
            ReadQueryValue(request, "milestone"),
            ReadQueryValue(request, "state"));
    }

    private static ProjectSettingsData CreateSettings(ProjectConfigService configService)
    {
        var result = configService.GetSettings();
        if (!result.Success) throw new InvalidOperationException(result.Message);

        return result.Payload!;
    }

    private static IResult SettingsMutation(ProjectConfigService configService, AppResult mutation)
    {
        var settings = CreateSettings(configService);
        return Results.Content(
            BoardHtmlRenderer.RenderSettings(settings, mutation.Success ? null : mutation.Message),
            "text/html; charset=utf-8",
            statusCode: mutation.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
    }

    private static IResult WikiError<T>(AppResult<T> result)
    {
        var statusCode = result.ErrorCode == "missing_wiki_page"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return Results.Content(
            BoardHtmlRenderer.RenderDialogError(result.Message ?? "Wiki page unavailable.", "Unable to open wiki"),
            "text/html; charset=utf-8",
            statusCode: statusCode);
    }

    private static BoardTask? FindTask(BoardData board, string id)
    {
        return board.Tasks.FirstOrDefault(task => task.Task.Id == id);
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
