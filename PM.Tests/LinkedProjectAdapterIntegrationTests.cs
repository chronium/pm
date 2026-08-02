using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PM.Application;
using PM.Mcp;

namespace PM.Tests;

public sealed class LinkedProjectAdapterIntegrationTests
{
    [Fact]
    public async Task CliTraversesFamilyAndEnforcesTrustedWritesAcrossSiblingProjects()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();

        var links = await RunCli(fixture, fixture.Royale.RepositoryPath, "project", "links");
        Assert.Equal(0, links.ExitCode);
        Assert.Contains("games", links.Output);
        Assert.Contains("current", links.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("starfall", links.Output);
        Assert.Contains("uninitialized", links.Output, StringComparison.OrdinalIgnoreCase);

        var tasks = await RunCli(fixture, fixture.Royale.RepositoryPath, "list", "--family");
        Assert.Equal(0, tasks.ExitCode);
        Assert.Contains("Games / games", tasks.Output);
        Assert.Contains("Royale / current", tasks.Output);
        Assert.Contains("Starfall / starfall", tasks.Output);

        var taskSearch = await RunCli(
            fixture, fixture.Royale.RepositoryPath, "task", "search", "family-e2e", "--family");
        Assert.Equal(0, taskSearch.ExitCode);
        Assert.Contains("Games", taskSearch.Output);
        Assert.Contains("Royale", taskSearch.Output);
        Assert.Contains("Starfall", taskSearch.Output);

        var wikiSearch = await RunCli(
            fixture, fixture.Royale.RepositoryPath, "wiki", "search", "family-e2e", "--family");
        Assert.Equal(0, wikiSearch.ExitCode);
        Assert.Contains("Games / games (prj_games)", wikiSearch.Output);
        Assert.Contains("Royale / current (prj_royale)", wikiSearch.Output);
        Assert.Contains("Starfall / starfall (prj_starfall)", wikiSearch.Output);

        var siblingWiki = await RunCli(
            fixture, fixture.Royale.RepositoryPath, "wiki", "show", "architecture/starfall", "--project", "starfall");
        Assert.Equal(0, siblingWiki.ExitCode);
        Assert.Contains("Starfall architecture", siblingWiki.Output);

        var denied = await RunCli(
            fixture, fixture.Royale.RepositoryPath, "task", "note", "STAR-0001", "CLI integration note", "--project", "starfall");
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Contains("read-only", denied.Output, StringComparison.OrdinalIgnoreCase);

        var trust = await RunCli(fixture, fixture.Royale.RepositoryPath, "project", "trust", "starfall");
        Assert.Equal(0, trust.ExitCode);
        var mutated = await RunCli(
            fixture, fixture.Royale.RepositoryPath, "task", "note", "STAR-0001", "CLI integration note", "--project", "starfall");
        Assert.Equal(0, mutated.ExitCode);
        Assert.Contains("Project prj_starfall", mutated.Output);
        Assert.Contains("CLI integration note", ReadTaskMarkdown(fixture.Starfall, "STAR-0001"));
        Assert.DoesNotContain("CLI integration note", ReadTaskMarkdown(fixture.Royale, "ROYALE-0001"));

        var standalone = await RunCli(fixture, fixture.Standalone.RepositoryPath, "project", "links");
        Assert.Equal(0, standalone.ExitCode);
        Assert.Contains("current", standalone.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prj_games", standalone.Output);
    }

    [Fact]
    public async Task StdioMcpAdvertisesAndExecutesLinkedReadsAndTrustedSiblingMutation()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["PM_PROJECT_REGISTRY_PATH"] = fixture.RegistryPath;
        var errors = new List<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "PM linked-project integration",
            Command = "dotnet",
            Arguments = [typeof(PmMcpTools).Assembly.Location, "mcp"],
            WorkingDirectory = fixture.Royale.RepositoryPath,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            StandardErrorLines = errors.Add,
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        AssertSchemaProperty(tools, "list_tasks", "project");
        AssertSchemaProperty(tools, "list_tasks", "family");
        AssertSchemaProperty(tools, "append_task_note", "project");
        Assert.Contains(tools, tool => tool.Name == "list_linked_projects");

        var family = await Call(client, "list_linked_projects");
        Assert.NotEqual(true, family.IsError);
        Assert.Contains("prj_games", Json(family));
        Assert.Contains("prj_royale", Json(family));
        Assert.Contains("prj_starfall", Json(family));
        Assert.Contains("prj_missing", Json(family));

        var tasks = await Call(client, "list_tasks", ("family", true));
        Assert.NotEqual(true, tasks.IsError);
        Assert.Contains("SHARED-0001", Json(tasks));
        Assert.Contains("ROYALE-0002", Json(tasks));
        Assert.Contains("STAR-0001", Json(tasks));

        var siblingTask = await Call(
            client, "get_task", ("taskId", "STAR-0001"), ("project", "starfall"));
        Assert.NotEqual(true, siblingTask.IsError);
        Assert.Contains("prj_starfall", Json(siblingTask));
        Assert.Contains("pm://project/prj_games/task/SHARED-0001", Json(siblingTask));

        var wiki = await Call(
            client, "search_wiki_pages", ("query", "family-e2e"), ("family", true));
        Assert.NotEqual(true, wiki.IsError);
        Assert.Contains("architecture/family", Json(wiki));
        Assert.Contains("architecture/royale", Json(wiki));
        Assert.Contains("architecture/starfall", Json(wiki));

        var denied = await Call(
            client, "append_task_note", ("taskId", "STAR-0001"), ("note", "MCP integration note"),
            ("project", "starfall"));
        Assert.Contains("\"success\":false", Json(denied));
        Assert.Contains("linked_project_write_untrusted", Json(denied));

        var registry = fixture.Registry();
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        var reopened = registry.Remember(fixture.Starfall);
        Assert.True(reopened.Success);
        Assert.True(reopened.Payload!.WriteTrusted);
        var trustedFamily = await Call(client, "list_linked_projects");
        Assert.NotEqual(true, trustedFamily.IsError);
        using (var document = JsonDocument.Parse(Json(trustedFamily)))
        {
            var starfall = Assert.Single(document.RootElement
                .GetProperty("data")
                .GetProperty("members")
                .EnumerateArray(), member => member.GetProperty("projectId").GetString() == "prj_starfall");
            Assert.True(starfall.GetProperty("writeTrusted").GetBoolean());
        }
        var mutated = await Call(
            client, "append_task_note", ("taskId", "STAR-0001"), ("note", "MCP integration note"),
            ("project", "starfall"));
        Assert.NotEqual(true, mutated.IsError);
        Assert.Contains("prj_starfall", Json(mutated));
        AssertResolvedSharedDependency(mutated);
        var metadata = await Call(
            client, "update_task_metadata", ("taskId", "STAR-0001"), ("priority", "low"),
            ("project", "starfall"));
        Assert.NotEqual(true, metadata.IsError);
        AssertResolvedSharedDependency(metadata);
        var reread = await Call(
            client, "get_task", ("taskId", "STAR-0001"), ("project", "starfall"));
        AssertResolvedSharedDependency(reread);
        Assert.Contains("MCP integration note", ReadTaskMarkdown(fixture.Starfall, "STAR-0001"));
        Assert.DoesNotContain(errors, line => line.Contains("fail", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ProcessResult> RunCli(
        LinkedProjectIntegrationFixture fixture,
        string workingDirectory,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(PmMcpTools).Assembly.Location);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["PM_PROJECT_REGISTRY_PATH"] = fixture.RegistryPath;
        start.Environment["NO_COLOR"] = "1";
        start.Environment["COLUMNS"] = "240";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PM CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout + await stderr);
    }

    private static void AssertSchemaProperty(
        IList<McpClientTool> tools,
        string toolName,
        string propertyName)
    {
        var tool = Assert.Single(tools, candidate => candidate.Name == toolName);
        Assert.True(tool.JsonSchema.GetProperty("properties").TryGetProperty(propertyName, out _),
            $"Tool {toolName} does not advertise {propertyName}.");
    }

    private static ValueTask<CallToolResult> Call(
        McpClient client,
        string name,
        params (string Name, object Value)[] arguments) =>
        client.CallToolAsync(name, arguments.ToDictionary(argument => argument.Name, argument => (object?)argument.Value));

    private static string Json(CallToolResult result) => JsonSerializer.Serialize(result.StructuredContent);

    private static void AssertResolvedSharedDependency(CallToolResult result)
    {
        using var document = JsonDocument.Parse(Json(result));
        var data = document.RootElement.GetProperty("data");
        var task = data.TryGetProperty("task", out var mutationTask) ? mutationTask : data;
        Assert.True(task.GetProperty("dependenciesReady").GetBoolean());
        Assert.Equal(
            ["pm://project/prj_games/task/SHARED-0001"],
            task.GetProperty("completedDependencies").EnumerateArray().Select(item => item.GetString()).ToList());
        Assert.Empty(task.GetProperty("unavailableDependencies").EnumerateArray());
    }

    private static string ReadTaskMarkdown(PM.Project.ProjectRoot root, string taskId) =>
        File.ReadAllText(Path.Combine(root.TasksPath, $"{taskId}.{GlobalConfig.DefaultTaskExtension}"));

    private sealed record ProcessResult(int ExitCode, string Output);
}
