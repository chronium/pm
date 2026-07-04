using System.ComponentModel;
using CodePunk.Highlight.Core.SyntaxHighlighting;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Core.SyntaxHighlighting.Languages;
using Microsoft.Extensions.DependencyInjection;
using PM;
using PM.Application;
using PM.Mcp;
using PM.Project;
using PM.Tasks;
using PM.Web;
using PM.Wiki;
using Spectre.Console;
using Spectre.Console.Cli;

var serviceProvider = new ServiceCollection();
var cancellationTokenSource = new CancellationTokenSource();

if (args is [var command, ..] && string.Equals(command, GlobalConfig.McpCommandName, StringComparison.Ordinal))
{
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellationTokenSource.Cancel();
        Console.Error.WriteLine("Aborting...");
    };

    return await McpServerHost.RunAsync(args[1..], cancellationTokenSource.Token);
}

serviceProvider.AddHttpClient<INextIdService, NextIdService>();

serviceProvider.AddSingleton<ProjectRoot>();
serviceProvider.AddSingleton<TaskService>();
serviceProvider.AddSingleton<ProjectCreationService>();
serviceProvider.AddSingleton<ProjectConfigService>();
serviceProvider.AddSingleton<BoardService>();
serviceProvider.AddSingleton<WikiService>();
serviceProvider.AddSingleton<IEditorService, EditorService>();
serviceProvider.AddSingleton<ISyntaxHighlighter>(new SyntaxHighlighter([
    new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
]));

var registrar = new ServiceCollectionRegistrar(serviceProvider);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName(GlobalConfig.ApplicationName)
        .SetApplicationVersion(GlobalConfig.ApplicationVersion);

    config.SetInterceptor(new DryRunInterceptor());
    config.SetInterceptor(new TimingInterceptor());

    config.AddCommand<InitCommand>(GlobalConfig.InitCommandName);

    config.AddBranch(GlobalConfig.TrackBranchName,
        track =>
        {
            track.SetDescription("Manage tracks within a project");
            track.AddCommand<TrackAddCommand>(GlobalConfig.TrackAddCommandName);
            track.AddCommand<TrackRenameCommand>(GlobalConfig.TrackRenameCommandName);
            track.AddCommand<TrackRemoveCommand>(GlobalConfig.TrackRemoveCommandName);
        });

    config.AddBranch(GlobalConfig.MilestoneBranchName,
        milestone =>
        {
            milestone.SetDescription("Manage milestones within a project");
            milestone.AddCommand<MilestoneAddCommand>(GlobalConfig.MilestoneAddCommandName);
            milestone.AddCommand<MilestoneRenameCommand>(GlobalConfig.MilestoneRenameCommandName);
            milestone.AddCommand<MilestoneRemoveCommand>(GlobalConfig.MilestoneRemoveCommandName);
        });

    config.AddBranch(GlobalConfig.StatusBranchName,
        status =>
        {
            status.SetDescription("Manage task statuses within a project");
            status.AddCommand<StatusAddCommand>(GlobalConfig.StatusAddCommandName);
            status.AddCommand<StatusRenameCommand>(GlobalConfig.StatusRenameCommandName);
            status.AddCommand<StatusRemoveCommand>(GlobalConfig.StatusRemoveCommandName);
        });

    config.AddBranch(GlobalConfig.TaskBranchName,
        task =>
        {
            task.SetDescription("Manage tasks within a project");

            task.AddCommand<TaskAddCommand>(GlobalConfig.TaskAddCommandName);
            task.AddCommand<TaskEditCommand>(GlobalConfig.TaskEditCommandName);
            task.AddCommand<TaskRemoveCommand>(GlobalConfig.TaskRemoveCommandName);
        });

    config.AddBranch(GlobalConfig.WikiBranchName,
        wiki =>
        {
            wiki.SetDescription("Manage wiki pages within a project");

            wiki.AddCommand<WikiListCommand>(GlobalConfig.WikiListCommandName);
            wiki.AddCommand<WikiShowCommand>(GlobalConfig.WikiShowCommandName);
            wiki.AddCommand<WikiCreateCommand>(GlobalConfig.WikiCreateCommandName);
            wiki.AddCommand<WikiEditCommand>(GlobalConfig.WikiEditCommandName);
        });

    config.AddCommand<MoveCommand>(GlobalConfig.MoveCommandName);

    config.AddCommand<ListCommand>(GlobalConfig.ListCommandName);
    config.AddCommand<WebCommand>(GlobalConfig.WebCommandName);
});

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
    AnsiConsole.WriteLine("\nAborting...");
};

return await app.RunAsync(args, cancellationTokenSource.Token);

public class CommonSettings : CommandSettings
{
    [CommandOption("--dry-run")]
    [Description("Preview changes without applying them")]
    public bool DryRun { get; init; }
}
