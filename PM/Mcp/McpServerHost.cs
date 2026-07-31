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
        var parsed = McpServerStartupOptions.Parse(args);
        if (!parsed.Success)
            throw new ArgumentException(parsed.Message, nameof(args));

        return CreateBuilder(parsed.Payload!);
    }

    public static HostApplicationBuilder CreateBuilder(McpServerStartupOptions options)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = [],
            DisableDefaults = true,
        };
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new McpCapabilityContext(options.Profile, options.AssignedTaskId));
        builder.Services.AddHttpClient<IPmWorkerClient, PmWorkerClient>();
        builder.Services.AddSingleton<INextIdService, NextIdService>();
        builder.Services.Configure<NextIdServiceOptions>(options => options.WriteFailuresToConsole = false);
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        builder.Services.AddSingleton<LinkedProjectRegistryStore>();
        builder.Services.AddSingleton<ILinkedProjectSubmoduleInspector, GitLinkedProjectSubmoduleInspector>();
        builder.Services.AddSingleton<LinkedProjectResolver>();
        builder.Services.AddSingleton(provider =>
        {
            var projectRoot = new ProjectRoot();
            if (projectRoot.Exists)
                _ = provider.GetRequiredService<LinkedProjectRegistryStore>().Remember(projectRoot);
            return projectRoot;
        });
        builder.Services.AddSingleton<TaskService>();
        builder.Services.AddSingleton<ProjectCreationService>();
        builder.Services.AddSingleton<ProjectConfigService>();
        builder.Services.AddSingleton<LinkedProjectService>();
        builder.Services.AddSingleton<LinkedProjectFamilyService>();
        builder.Services.AddSingleton<ILinkedProjectGitInspector, LinkedProjectGitInspector>();
        builder.Services.AddSingleton<LinkedProjectReadService>();
        builder.Services.AddSingleton<BoardService>();
        builder.Services.AddSingleton<WikiService>();
        builder.Services.AddSingleton<ProjectValidationService>();
        builder.Services.AddSingleton<IProjectMembershipService, ProjectMembershipService>();
        builder.Services.AddSingleton<PmMcpTools>();

        var mcpBuilder = builder.Services
            .AddMcpServer()
            .WithStdioServerTransport();

        if (options.Profile == McpCapabilityProfile.RunWorker)
            mcpBuilder.WithTools(McpToolCatalog.CreateRunWorkerTools());
        else
            mcpBuilder.WithToolsFromAssembly();

        return builder;
    }

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parsed = McpServerStartupOptions.Parse(args);
        if (!parsed.Success)
        {
            Console.Error.WriteLine(parsed.Message);
            return 2;
        }

        using var host = CreateBuilder(parsed.Payload!).Build();
        var validation = ValidateStartup(host.Services);
        if (!validation.Success)
        {
            Console.Error.WriteLine(validation.Message);
            return 2;
        }

        await host.RunAsync(cancellationToken);
        return 0;
    }

    public static AppResult ValidateStartup(IServiceProvider services)
    {
        var options = services.GetRequiredService<McpServerStartupOptions>();
        if (options.Profile != McpCapabilityProfile.RunWorker)
            return AppResult.Ok();

        var projectRoot = services.GetRequiredService<ProjectRoot>();
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryGetById(options.AssignedTaskId!, out _))
            return AppResult.Fail("missing_task", $"Task with ID {options.AssignedTaskId} not found.");

        return AppResult.Ok();
    }
}
