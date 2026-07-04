using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public class StatusRenameCommand(ProjectConfigService configService) : Command<StatusRenameCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = configService.RenameStatus(settings.Key, settings.Name);
        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{(result.Message ?? "Status rename failed.").EscapeMarkup()}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"Renamed status [green]{settings.Key.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Status key")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<name>")]
        [Description("Status display name")]
        public string Name { get; init; } = string.Empty;
    }
}
