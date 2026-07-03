using System.Text;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Spectre.Rendering;
using PM.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace PM.Project;

public class InitCommand(ProjectRoot projectRoot, INextIdService nextIdService, ISyntaxHighlighter highlighter)
    : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings commonSettings,
        CancellationToken cancellationToken)
    {
        if (await ValidateProjectInitialization(cancellationToken) != 0) return 1;

        var config = await GatherProjectConfiguration(cancellationToken);

        return await GenerateProjectConfigurationDisplay(config, cancellationToken);
    }

    private async Task<int> GenerateProjectConfigurationDisplay(ProjectConfig config,
        CancellationToken cancellationToken)
    {
        var yamlText = YamlSerde.Serialize(config);

        var sb = new StringBuilder();
        highlighter.Highlight(yamlText, "yaml", new MarkupTokenRenderer(sb));

        var configPanel = new Panel(sb.ToString())
        {
            Header = new($"Project configuration {GlobalConfig.PmConfigFile}:"),
            Border = new RoundedBoxBorder(),
        };

        AnsiConsole.Write(configPanel);

        if (!await AnsiConsole.ConfirmAsync("Initialize project?", true, cancellationToken))
        {
            AnsiConsole.MarkupLine("[yellow]Aborted project initialization.[/]");
            return 1;
        }

        AnsiConsole.WriteLine();

        await projectRoot.CreateProject(config, cancellationToken);

        AnsiConsole.MarkupLineInterpolated(
            $"Project initialized in [green]{Path.GetRelativePath(Directory.GetCurrentDirectory(), projectRoot.RootPath)}/[/]");

        return 0;
    }

    private static async Task<ProjectConfig> GatherProjectConfiguration(CancellationToken cancellationToken)
    {
        var assumedName = Directory.GetCurrentDirectory().Split(Path.DirectorySeparatorChar).Last();

        var projectName = await AnsiConsole.AskAsync("Project name ", assumedName, cancellationToken);
        var idWidth = await AnsiConsole.AskAsync("Project ID width ", 4, cancellationToken);
        var idPrefix = await AnsiConsole.AskAsync("Project ID prefix ", "TASK", cancellationToken);

        var tasksPrompt = new MultiSelectionPrompt<string>()
            .Title("What [green]task states[/] should be created?")
            .Required()
            .UseConverter(key => $"{GlobalConfig.DefaultTaskStates[key]} ({key})")
            .AddChoices(GlobalConfig.DefaultTaskStates.Keys);

        foreach (var key in GlobalConfig.DefaultTaskStates.Keys) tasksPrompt.Select(key);

        var taskStates = await AnsiConsole.PromptAsync(tasksPrompt, cancellationToken);

        var config = new ProjectConfig
        {
            Name = projectName,
            IdWidth = idWidth,
            IdPrefix = idPrefix,
            TaskStates = taskStates.ToDictionary(key => key, key => GlobalConfig.DefaultTaskStates[key]),
        };
        return config;
    }

    private async Task<int> ValidateProjectInitialization(CancellationToken cancellationToken)
    {
        if (projectRoot.Exists)
        {
            AnsiConsole.MarkupLine("[red]A project is already initialized in this directory or a parent directory.[/]");
            return 1;
        }

        if (!await nextIdService.Healthy(cancellationToken))
        {
            AnsiConsole.MarkupLine("[red]Unable to reach the next ID service.[/]");
            return 1;
        }

        return 0;
    }
}