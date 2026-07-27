using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PM.Application;
using PM.Auth;
using PM.Project;
using PM.Tasks;
using PM.Worker;

namespace PM.Mcp;

public static class McpServerHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        };
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Logging.ClearProviders();

        builder.Services.AddHttpClient<IPmWorkerClient, PmWorkerClient>();
        builder.Services.AddSingleton<INextIdService, NextIdService>();
        builder.Services.Configure<NextIdServiceOptions>(options => options.WriteFailuresToConsole = false);
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        builder.Services.AddSingleton<ProjectRoot>();
        builder.Services.AddSingleton<TaskService>();
        builder.Services.AddSingleton<ProjectCreationService>();
        builder.Services.AddSingleton<ProjectConfigService>();
        builder.Services.AddSingleton<BoardService>();
        builder.Services.AddSingleton<WikiService>();
        builder.Services.AddSingleton<ProjectValidationService>();
        builder.Services.AddSingleton<IProjectMembershipService, ProjectMembershipService>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        return builder;
    }

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        await CreateBuilder(args).Build().RunAsync(cancellationToken);
        return 0;
    }
}
