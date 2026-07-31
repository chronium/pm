using System.ComponentModel;
using PM.Application;
using PM.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Wiki;

public sealed class WikiRemoveCommand(LinkedProjectMutationService mutations) : AsyncCommand<WikiRemoveCommand.Settings>
{
    public int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        ExecuteAsync(context, settings, cancellationToken).GetAwaiter().GetResult();

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.Yes)
        {
            AnsiConsole.MarkupLine("[red]Pass --yes to permanently remove the wiki page.[/]");
            return 1;
        }

        var target = await mutations.ResolveTargetAsync(settings.Project, cancellationToken: cancellationToken);
        if (!target.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{target.Message!.EscapeMarkup()}[/]");
            return 1;
        }

        using var mutation = mutations.Track(target.Payload!);
        var result = target.Payload!.Wiki.RemovePage(settings.Path);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Wiki page removal failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Removed wiki page [green]{settings.Path.Trim().EscapeMarkup()}[/].");
        LinkedProjectConsole.WriteReceipt(mutation.Receipt);
        return 0;
    }

    public sealed class Settings : LinkedProjectMutationSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Wiki page path")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("--yes")]
        [Description("Confirm permanent wiki page removal")]
        public bool Yes { get; init; }
    }
}
