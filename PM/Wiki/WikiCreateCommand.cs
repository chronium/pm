using System.ComponentModel;
using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Application;
using PM.Project;
using PM.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Wiki;

public sealed class WikiCreateCommand(
    LinkedProjectMutationService mutations,
    IEditorService editorService,
    ISyntaxHighlighter highlighter)
    : AsyncCommand<WikiCreateCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var target = await mutations.ResolveTargetAsync(settings.Project, cancellationToken: cancellationToken);
        if (!target.Success)
        {
            RenderError(target.Message, "Wiki project could not be resolved.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Title))
        {
            RenderError("Wiki page title is required.", "Wiki page creation failed.");
            return 1;
        }

        using var mutation = mutations.Track(target.Payload!);
        AppResult<WikiPageData> result;
        if (settings.Edit)
        {
            var markdown = await ResolveEditedMarkdown(settings, cancellationToken);
            if (markdown == null) return 1;
            result = target.Payload!.Wiki.CreatePageMarkdown(settings.Path, markdown);
        }
        else
        {
            result = target.Payload!.Wiki.CreatePage(settings.Path, settings.Title, settings.Body ?? string.Empty);
        }

        if (!result.Success)
        {
            RenderError(result.Message, "Wiki page creation failed.");
            return 1;
        }

        var page = result.Payload!;
        AnsiConsole.MarkupLineInterpolated($"[green]Wiki page created: [/]{page.Path.EscapeMarkup()}");
        RenderPagePanel(page);
        LinkedProjectConsole.WriteReceipt(mutation.Receipt);
        return 0;
    }

    private async Task<string?> ResolveEditedMarkdown(Settings settings, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var draft = new WikiPage
        {
            Path = settings.Path,
            Title = settings.Title,
            CreatedAt = now,
            ModifiedAt = now,
            Body = settings.Body ?? string.Empty,
        }.ToMarkdown();

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"pm-wiki-create-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFilePath, draft, cancellationToken);

        try
        {
            var exitCode = await editorService.EditFile(tempFilePath, cancellationToken);
            if (exitCode != 0)
            {
                AnsiConsole.MarkupLine($"[red]Editor exited with code {exitCode}. Wiki page creation aborted.[/]");
                return null;
            }

            return await File.ReadAllTextAsync(tempFilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Unable to launch editor: {ex.Message.EscapeMarkup()}[/]");
            return null;
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    private void RenderPagePanel(WikiPageData page)
    {
        var sb = new StringBuilder();
        highlighter.Highlight(page.Markdown, "md", new MarkupTokenRenderer(sb));

        var panel = new Panel(sb.ToString())
        {
            Header = new($"{page.Path}.{GlobalConfig.DefaultTaskExtension}"),
            Border = new RoundedBoxBorder(),
        };

        AnsiConsole.Write(panel);
    }

    private static void RenderError(string? message, string fallback)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{(message ?? fallback).EscapeMarkup()}[/]");
    }

    public sealed class Settings : LinkedProjectMutationSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Wiki page path")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--title <title>")]
        [Description("Wiki page title")]
        public string Title { get; init; } = string.Empty;

        [CommandOption("--body <text>")]
        [Description("Markdown body for the wiki page")]
        public string? Body { get; init; }

        [CommandOption("--edit")]
        [Description("Open an editor for the full wiki markdown document")]
        public bool Edit { get; init; }
    }
}
