using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PM.Api;
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
    WikiService wikiService,
    ProjectValidationService validationService) : AsyncCommand<WebCommand.Settings>
{
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

        var ui = settings.Ui ?? "legacy";
        var angularAssets = CreateAngularAssetStore();
        if (!settings.Api && ui == "angular" && !angularAssets.HasAssets)
        {
            AnsiConsole.MarkupLine(
                "[red]Angular UI assets are not embedded. Build with [green]-p:EmbedAngularAssets=true[/] after running [green]npm run build[/] in web/.[/]");
            return 1;
        }

        var port = settings.Port ?? (settings.Api ? 51237 : GetAvailablePort());
        var url = $"http://127.0.0.1:{port}";

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);
        ConfigureApiServices(builder.Services);

        var app = builder.Build();
        MapApiEndpoints(app, projectRoot, configService, boardService, taskService);
        if (!settings.Api)
        {
            if (ui == "legacy")
                MapEndpoints(app, boardService, taskService, configService, wikiService, validationService);
            else
                app.MapAngularWeb(angularAssets);
        }

        await app.StartAsync(cancellationToken);
        var subject = settings.Api ? "API" : $"{ui} UI";
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
        BoardService boardService,
        TaskService taskService)
    {
        endpoints.MapApiV1(projectRoot, configService, boardService, taskService,
            new ResourceRevisionService(projectRoot, boardService));
        endpoints.MapOpenApi("/openapi/{documentName}.json");
    }

    public static void MapEndpoints(
        IEndpointRouteBuilder endpoints,
        BoardService boardService,
        TaskService taskService,
        ProjectConfigService configService,
        WikiService wikiService,
        ProjectValidationService validationService)
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
            var validation = CreateValidation(validationService);
            return Results.Content(BoardHtmlRenderer.RenderSettingsPage(board, settings, validation: validation),
                "text/html; charset=utf-8");
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
            var pagesResult = wikiService.ListPages();
            return !pagesResult.Success
                ? WikiError(pagesResult)
                : Results.Content(BoardHtmlRenderer.RenderWikiCreatePage(board, pagesResult.Payload!),
                    "text/html; charset=utf-8");
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
            {
                var pagesResult = wikiService.ListPages();
                if (!pagesResult.Success) return WikiError(pagesResult);

                return Results.Content(
                    BoardHtmlRenderer.RenderWikiCreatePage(board, pagesResult.Payload!, path, title, markdown,
                        result.Message),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Redirect($"/wiki/{BoardHtmlRenderer.WikiPathUrl(result.Payload!.Path)}");
        });

        endpoints.MapGet("/wiki/edit/{**path}", (string path) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var result = wikiService.ReadPage(path);
            if (!result.Success) return WikiError(result);

            var pagesResult = wikiService.ListPages();
            return !pagesResult.Success
                ? WikiError(pagesResult)
                : Results.Content(BoardHtmlRenderer.RenderWikiEditPage(board, result.Payload!, pagesResult.Payload!),
                    "text/html; charset=utf-8");
        });

        endpoints.MapPost("/wiki/edit/{**path}", async (string path, HttpRequest request) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var form = await request.ReadFormAsync();
            var markdown = form["markdown"].ToString();
            var result = wikiService.UpdatePageBody(path, markdown);
            if (!result.Success)
            {
                var readResult = wikiService.ReadPage(path);
                var title = readResult.Success ? readResult.Payload!.Title : path;
                var pagesResult = wikiService.ListPages();
                if (!pagesResult.Success) return WikiError(pagesResult);

                return Results.Content(
                    BoardHtmlRenderer.RenderWikiEditPage(board, path, title, markdown, pagesResult.Payload!,
                        result.Message),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Redirect($"/wiki/{BoardHtmlRenderer.WikiPathUrl(result.Payload!.Path)}");
        });

        endpoints.MapGet("/wiki/meta/{**path}", (string path) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var result = wikiService.ReadPage(path);
            if (!result.Success) return WikiError(result);

            var pagesResult = wikiService.ListPages();
            return !pagesResult.Success
                ? WikiError(pagesResult)
                : Results.Content(BoardHtmlRenderer.RenderWikiMetadataPage(board, result.Payload!, pagesResult.Payload!),
                    "text/html; charset=utf-8");
        });

        endpoints.MapPost("/wiki/meta/{**path}", async (string path, HttpRequest request) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var form = await request.ReadFormAsync();
            var newPath = form["path"].ToString();
            var title = form["title"].ToString();
            var result = wikiService.RenamePage(path, newPath, title);
            if (!result.Success)
            {
                var pagesResult = wikiService.ListPages();
                if (!pagesResult.Success) return WikiError(pagesResult);

                return Results.Content(
                    BoardHtmlRenderer.RenderWikiMetadataPage(board, path, newPath, title, pagesResult.Payload!,
                        result.Message),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Redirect($"/wiki/{BoardHtmlRenderer.WikiPathUrl(result.Payload!.Path)}");
        });

        endpoints.MapPost("/wiki/delete/{**path}", async (string path, HttpRequest request) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var form = await request.ReadFormAsync();
            var confirmation = form["confirm"].ToString();
            if (!string.Equals(confirmation, "delete", StringComparison.OrdinalIgnoreCase))
            {
                var readResult = wikiService.ReadPage(path);
                if (!readResult.Success) return WikiError(readResult);

                var pagesResult = wikiService.ListPages();
                if (!pagesResult.Success) return WikiError(pagesResult);

                return Results.Content(
                    BoardHtmlRenderer.RenderWikiMetadataPage(board, readResult.Payload!, pagesResult.Payload!,
                        "Type delete to confirm permanent removal."),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = wikiService.RemovePage(path);
            if (!result.Success)
            {
                var readResult = wikiService.ReadPage(path);
                var title = readResult.Success ? readResult.Payload!.Title : path;
                var pagesResult = wikiService.ListPages();
                if (!pagesResult.Success) return WikiError(pagesResult);

                return Results.Content(
                    BoardHtmlRenderer.RenderWikiMetadataPage(board, path, path, title, pagesResult.Payload!,
                        result.Message),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Redirect("/wiki");
        });

        endpoints.MapGet("/wiki/{**path}", (string path) =>
        {
            var board = CreateBoard(boardService, new BoardQuery());
            var result = wikiService.ReadPage(path);
            if (result.Success)
            {
                var pagesResult = wikiService.ListPages();
                if (!pagesResult.Success) return WikiError(pagesResult);

                return Results.Content(BoardHtmlRenderer.RenderWikiPage(board, result.Payload!, pagesResult.Payload!),
                    "text/html; charset=utf-8");
            }

            if (result.ErrorCode != "missing_wiki_page") return WikiError(result);

            var folderResult = wikiService.ListPagesUnder(path);
            if (!folderResult.Success || folderResult.Payload!.Count == 0) return WikiError(result);

            var allPagesResult = wikiService.ListPages();
            if (!allPagesResult.Success) return WikiError(allPagesResult);

            return Results.Content(BoardHtmlRenderer.RenderWikiFolderPage(board, path, folderResult.Payload!,
                    allPagesResult.Payload!),
                "text/html; charset=utf-8");
        });

        endpoints.MapPost("/settings/statuses", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, validationService, configService.AddStatus(
                form["key"].ToString(),
                form["name"].ToString()));
        });

        endpoints.MapPost("/settings/statuses/{key}/rename", async (string key, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, validationService,
                configService.RenameStatus(key, form["name"].ToString()));
        });

        endpoints.MapPost("/settings/statuses/{key}/remove", (string key) =>
            SettingsMutation(configService, validationService, configService.RemoveStatus(key)));

        endpoints.MapPost("/settings/tracks", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, validationService, configService.AddTrack(
                form["key"].ToString(),
                form["name"].ToString()));
        });

        endpoints.MapPost("/settings/tracks/{key}/rename", async (string key, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, validationService,
                configService.RenameTrack(key, form["name"].ToString()));
        });

        endpoints.MapPost("/settings/tracks/{key}/remove", (string key) =>
            SettingsMutation(configService, validationService, configService.RemoveTrack(key)));

        endpoints.MapPost("/settings/milestones", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, validationService, configService.AddMilestone(
                form["key"].ToString(),
                form["title"].ToString(),
                form["priority"].ToString()));
        });

        endpoints.MapPost("/settings/milestones/{key}/rename", async (string key, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, validationService,
                configService.RenameMilestone(key, form["title"].ToString()));
        });

        endpoints.MapPost("/settings/milestones/{key}/priority", async (string key, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            return SettingsMutation(configService, validationService,
                configService.SetMilestonePriority(key, form["priority"].ToString()));
        });

        endpoints.MapPost("/settings/milestones/{key}/remove", (string key) =>
            SettingsMutation(configService, validationService, configService.RemoveMilestone(key)));

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
            var result = boardService.GetBoard(new BoardQuery());
            if (!result.Success)
                return Results.Content(
                    BoardHtmlRenderer.RenderDialogError(result.Message ?? "Task not found.", "Unable to edit task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);

            var task = FindTask(result.Payload!, id);
            return task == null
                ? Results.Content(
                    BoardHtmlRenderer.RenderDialogError("Task not found.", "Unable to edit task"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest)
                : Results.Content(
                    BoardHtmlRenderer.RenderTaskEditForm(task, result.Payload!.States, ReadQuery(request)),
                    "text/html; charset=utf-8");
        });

        endpoints.MapPost("/task/{id}/edit", async (string id, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var result = taskService.UpdateTaskDetails(
                id,
                form["title"].ToString(),
                form["targetState"].ToString(),
                form["description"].ToString(),
                form["priority"].ToString());
            if (!result.Success)
            {
                var editBoard = boardService.GetBoard(new BoardQuery());
                if (!editBoard.Success)
                    return Results.Content(
                        BoardHtmlRenderer.RenderDialogError(editBoard.Message ?? "Task edit failed.",
                            "Unable to edit task"),
                        "text/html; charset=utf-8",
                        statusCode: StatusCodes.Status400BadRequest);

                var editTask = FindTask(editBoard.Payload!, id);
                if (editTask == null)
                    return Results.Content(
                        BoardHtmlRenderer.RenderDialogError(result.Message ?? "Task edit failed.",
                            "Unable to edit task"),
                        "text/html; charset=utf-8",
                        statusCode: StatusCodes.Status400BadRequest);

                return Results.Content(
                    BoardHtmlRenderer.RenderTaskEditForm(
                        editTask,
                        editBoard.Payload!.States,
                        CreateBoard(boardService, form).Query,
                        form["title"].ToString(),
                        form["targetState"].ToString(),
                        form["description"].ToString(),
                        form["priority"].ToString(),
                        result.Message),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

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

    private static ProjectValidationResult CreateValidation(ProjectValidationService validationService)
    {
        var result = validationService.ValidateProject();
        if (!result.Success) throw new InvalidOperationException(result.Message);

        return result.Payload!;
    }

    private static IResult SettingsMutation(
        ProjectConfigService configService,
        ProjectValidationService validationService,
        AppResult mutation)
    {
        var settings = CreateSettings(configService);
        var validation = CreateValidation(validationService);
        return Results.Content(
            BoardHtmlRenderer.RenderSettings(settings, mutation.Success ? null : mutation.Message, validation),
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

    protected virtual void OpenBrowser(string url)
    {
        OpenUrlInDefaultBrowser(url);
    }

    protected virtual IAngularAssetStore CreateAngularAssetStore() => new EmbeddedAngularAssetStore();

    private static string? ValidateSettings(Settings settings)
    {
        if (settings.Api && settings.Open) return "--open cannot be combined with --api.";
        if (settings.Api && settings.Ui != null) return "--ui cannot be combined with --api.";
        if (settings.Ui != null && settings.Ui is not ("legacy" or "angular"))
            return $"Unknown UI mode '{settings.Ui}'. Expected legacy or angular.";
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

        [CommandOption("--ui <MODE>")]
        [Description("UI to serve: legacy or angular. Defaults to legacy.")]
        public string? Ui { get; init; }
    }
}
