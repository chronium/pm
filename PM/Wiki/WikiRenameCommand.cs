using System.ComponentModel;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiRenameCommand(LinkedProjectMutationService mutations) : AsyncCommand<WikiRenameCommand.Settings>
{
    public int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        ExecuteAsync(context, settings, cancellationToken).GetAwaiter().GetResult();

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var target = await mutations.ResolveTargetAsync(settings.Project, cancellationToken: cancellationToken);
        if (!target.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{target.Message!.EscapeMarkup()}[/]");
            return 1;
        }

        using var mutation = mutations.Track(target.Payload!);
        var result = target.Payload!.Wiki.RenamePage(settings.Path, settings.NewPath, settings.Title);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Wiki page rename failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"Renamed wiki page [green]{result.Payload!.Path.EscapeMarkup()}[/].");
        LinkedProjectConsole.WriteReceipt(mutation.Receipt);
        return 0;
    }

    public sealed class Settings : LinkedProjectMutationSettings
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
