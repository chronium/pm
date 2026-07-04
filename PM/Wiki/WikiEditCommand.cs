using System.ComponentModel;
using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Application;
using PM.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Wiki;

public sealed class WikiEditCommand(WikiService wikiService, IEditorService editorService, ISyntaxHighlighter highlighter)
    : AsyncCommand<WikiEditCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var readResult = wikiService.ReadPage(settings.Path);
        if (!readResult.Success)
        {
            RenderError(readResult.Message, "Wiki page not found.");
            return 1;
        }

        var page = readResult.Payload!;
        if (settings.DryRun)
        {
            RenderPagePanel(page);
            return 0;
        }

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"pm-wiki-edit-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(tempFilePath, page.Markdown, cancellationToken);

        try
        {
            var exitCode = await editorService.EditFile(tempFilePath, cancellationToken);
            if (exitCode != 0)
            {
                AnsiConsole.MarkupLine($"[red]Editor exited with code {exitCode}. Wiki page edit aborted.[/]");
                return 1;
            }

            var editedContent = await File.ReadAllTextAsync(tempFilePath, cancellationToken);
            var saveResult = wikiService.UpdatePageMarkdown(settings.Path, editedContent);
            if (!saveResult.Success)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]{(saveResult.Message ?? "Wiki page edit aborted.").EscapeMarkup()} Wiki page edit aborted.[/]");
                return 1;
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Unable to launch editor: {ex.Message.EscapeMarkup()}[/]");
            return 1;
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

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Wiki page path")]
        public string Path { get; init; } = string.Empty;
    }
}
