using System.ComponentModel;
using CodePunk.Highlight.Core.SyntaxHighlighting;
using CodePunk.Highlight.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Highlight.Core.SyntaxHighlighting.Languages;
using Microsoft.Extensions.DependencyInjection;
using PM;
using PM.Application;
using PM.Auth;
using PM.Mcp;
using PM.Project;
using PM.Site;
using PM.Tasks;
using PM.Web;
using PM.Wiki;
using PM.Worker;
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

serviceProvider.AddHttpClient<IPmWorkerClient, PmWorkerClient>();
serviceProvider.AddSingleton<INextIdService, NextIdService>();

serviceProvider.AddSingleton<IIdentityService, IdentityService>();
serviceProvider.AddSingleton<ProjectRoot>();
serviceProvider.AddSingleton<TaskService>();
serviceProvider.AddSingleton<ProjectCreationService>();
serviceProvider.AddSingleton<ProjectConfigService>();
serviceProvider.AddSingleton<BoardService>();
serviceProvider.AddSingleton<WikiService>();
serviceProvider.AddSingleton<ProjectValidationService>();
serviceProvider.AddSingleton<IProjectMembershipService, ProjectMembershipService>();
serviceProvider.AddSingleton<IProjectCommandPrompts, ProjectCommandPrompts>();
serviceProvider.AddSingleton<SiteSnapshotBuilder>();
serviceProvider.AddSingleton<SiteExportService>();
serviceProvider.AddSingleton<IEditorService, EditorService>();
serviceProvider.AddSingleton<ISyntaxHighlighter>(new SyntaxHighlighter([
    new YamlLanguageDefinition(), new MarkdownLanguageDefinition(),
]));

var registrar = new ServiceCollectionRegistrar(serviceProvider);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.UseStrictParsing();
    config.SetApplicationName(GlobalConfig.ApplicationName)
        .SetApplicationVersion(GlobalConfig.ApplicationVersion);

    config.SetInterceptor(new DryRunInterceptor());
    config.SetInterceptor(new TimingInterceptor());

    config.AddCommand<InitCommand>(GlobalConfig.InitCommandName);

    config.AddBranch(GlobalConfig.ProjectBranchName, project =>
    {
        project.SetDescription("Inspect project identity and manage remote membership");
        project.AddCommand<ProjectIdentityCommand>("identity");
        project.AddCommand<ProjectMembersCommand>("members");
        project.AddCommand<ProjectInvitationsCommand>("invitations");
        project.AddCommand<ProjectInviteCommand>("invite");
        project.AddCommand<ProjectJoinCommand>("join");
        project.AddCommand<ProjectRevokeInvitationCommand>("revoke-invite");
        project.AddCommand<ProjectSetRoleCommand>("set-role");
        project.AddCommand<ProjectRemoveMemberCommand>("remove-member");
    });

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
            milestone.AddCommand<MilestonePriorityCommand>(GlobalConfig.MilestonePriorityCommandName);
            milestone.AddCommand<MilestoneListCommand>(GlobalConfig.MilestoneListCommandName);
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
            task.AddCommand<TaskMetadataCommand>(GlobalConfig.TaskMetadataCommandName);
            task.AddCommand<TaskNoteCommand>(GlobalConfig.TaskNoteCommandName);
            task.AddCommand<TaskNextCommand>(GlobalConfig.TaskNextCommandName);
            task.AddCommand<TaskRemoveCommand>(GlobalConfig.TaskRemoveCommandName);
            task.AddCommand<TaskSearchCommand>(GlobalConfig.TaskSearchCommandName);
        });

    config.AddBranch(GlobalConfig.WikiBranchName,
        wiki =>
        {
            wiki.SetDescription("Manage wiki pages within a project");

            wiki.AddCommand<WikiListCommand>(GlobalConfig.WikiListCommandName);
            wiki.AddCommand<WikiSearchCommand>(GlobalConfig.WikiSearchCommandName);
            wiki.AddCommand<WikiShowCommand>(GlobalConfig.WikiShowCommandName);
            wiki.AddCommand<WikiCreateCommand>(GlobalConfig.WikiCreateCommandName);
            wiki.AddCommand<WikiEditCommand>(GlobalConfig.WikiEditCommandName);
            wiki.AddCommand<WikiRenameCommand>(GlobalConfig.WikiRenameCommandName);
            wiki.AddCommand<WikiRemoveCommand>(GlobalConfig.WikiRemoveCommandName);
        });

    config.AddCommand<MoveCommand>(GlobalConfig.MoveCommandName);

    config.AddCommand<ListCommand>(GlobalConfig.ListCommandName);
    config.AddCommand<WebCommand>(GlobalConfig.WebCommandName);
    config.AddBranch(GlobalConfig.SiteBranchName,
        site =>
        {
            site.SetDescription("Build a read-only static project site");
            site.AddCommand<SiteBuildCommand>(GlobalConfig.SiteBuildCommandName);
        });
    config.AddCommand<DoctorCommand>(GlobalConfig.DoctorCommandName);
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
