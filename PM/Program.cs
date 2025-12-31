using System.ComponentModel;
using CodePunk.Highlight.Core.SyntaxHighlighting;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Core.SyntaxHighlighting.Languages;
using Microsoft.Extensions.DependencyInjection;
using PM;
using PM.InitCommand;
using PM.Project;
using PM.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

var serviceProvider = new ServiceCollection();

serviceProvider.AddHttpClient<INextIdService, NextIdService>(client =>
{
    client.BaseAddress = new("http://localhost:8080");
});

serviceProvider.AddSingleton<ProjectRoot>();
serviceProvider.AddSingleton<ISyntaxHighlighter>(new SyntaxHighlighter([
    new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
]));

var registrar = new ServiceCollectionRegistrar(serviceProvider);
var app = new CommandApp(registrar);

var cancellationTokenSource = new CancellationTokenSource();

app.Configure(config =>
{
    config.SetApplicationName(GlobalConfig.ApplicationName)
        .SetApplicationVersion(GlobalConfig.ApplicationVersion);

    config.SetInterceptor(new DryRunInterceptor());
    config.SetInterceptor(new TimingInterceptor());

    config.AddCommand<InitCommand>(GlobalConfig.InitCommandName);

    config.AddBranch(GlobalConfig.TaskBranchName,
        task =>
        {
            task.SetDescription("Manage tasks within a project");

            task.AddCommand<TaskAddCommand>(GlobalConfig.TaskAddCommandName);
        });
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