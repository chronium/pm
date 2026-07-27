using System.ComponentModel;
using PM.Application;
using PM.Project;
using PM.Web;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Site;

public class SiteBuildCommand(
    ProjectRoot projectRoot,
    ProjectValidationService validationService,
    SiteExportService exportService) : Command<SiteBuildCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]Project not found. Run [green]pm init[/] first.[/]");
            return 1;
        }

        var validation = validationService.ValidateProject();
        if (!validation.Success || !validation.Payload!.Valid)
        {
            var detail = validation.Success
                ? $"Project validation failed with {validation.Payload!.Issues.Count} issue(s). Run pm doctor for details."
                : validation.Message!;
            AnsiConsole.MarkupLineInterpolated($"[red]{detail.EscapeMarkup()}[/]");
            return 1;
        }

        var result = exportService.Build(settings.Output, settings.Force, CreateAngularAssetStore(), DateTimeOffset.UtcNow);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{result.Message!.EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Static site written to [green]{result.Payload!.EscapeMarkup()}[/]");
        return 0;
    }

    protected virtual IAngularAssetStore CreateAngularAssetStore() => new EmbeddedAngularAssetStore();

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--output <PATH>")]
        [Description("Output directory. Defaults to dist/pm-site.")]
        public string? Output { get; init; }

        [CommandOption("--force")]
        [Description("Replace an existing non-empty output directory.")]
        public bool Force { get; init; }
    }
}
