using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiRenameCommand(WikiService wikiService) : Command<WikiRenameCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = wikiService.RenamePage(settings.Path, settings.NewPath, settings.Title);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Wiki page rename failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Renamed wiki page [green]{result.Payload!.Path.EscapeMarkup()}[/].");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Current wiki page path")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--path <new-path>")]
        [Description("New wiki page path")]
        public string NewPath { get; init; } = string.Empty;

        [CommandOption("--title <title>")]
        [Description("New wiki page title")]
        public string Title { get; init; } = string.Empty;
    }
}
