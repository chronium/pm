using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PM.Application;
using PM.Mcp;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class MilestoneActivationAdapterIntegrationTests
{
    [Fact]
    public async Task CliExercisesPartialActivationLatchingGuardsDeliveryAndCycles()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateCliFixture(workspace);

        var initiallyInactive = await RunCli(root, "task", "next", "--milestone", "public-beta");
        Assert.Equal(0, initiallyInactive.ExitCode);
        Assert.Contains("inactive", initiallyInactive.Output, StringComparison.OrdinalIgnoreCase);

        var riskOverride = await RunCli(
            root,
            "trigger", "activate", "risk-entry",
            "--reason", "Recovery can complete during beta hardening.");
        Assert.Equal(0, riskOverride.ExitCode);
        Assert.Contains("override", riskOverride.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PM-0005", riskOverride.Output);

        var architecture = await RunCli(root, "milestone", "deliver", "architecture-approved");
        Assert.Equal(0, architecture.ExitCode);
        Assert.Contains("Delivered milestone architecture-approved", architecture.Output);

        Assert.True(MoveTask(root, "PM-0001", "done").Success);
        var finalRequiredTask = MoveTask(root, "PM-0002", "done");
        Assert.True(finalRequiredTask.Success);
        Assert.Equal("beta-entry", Assert.Single(
            finalRequiredTask.Payload!.ActivationImpact.ActivatedTriggers).Key);

        var betaEligible = await RunCli(root, "task", "next", "--milestone", "public-beta");
        Assert.Equal(0, betaEligible.ExitCode);
        Assert.Contains("PM-0006", betaEligible.Output);
        var remainingFoundation = await RunCli(root, "task", "next", "--milestone", "foundation");
        Assert.Equal(0, remainingFoundation.ExitCode);
        Assert.Contains("PM-0003", remainingFoundation.Output);

        Assert.True(MoveTask(root, "PM-0002", "todo").Success);
        var remainsLatched = await RunCli(root, "task", "next", "--milestone", "public-beta");
        Assert.Contains("PM-0006", remainsLatched.Output);

        var reset = await RunCli(root, "trigger", "reset", "beta-entry");
        Assert.Equal(0, reset.ExitCode);
        Assert.Contains("Reset activation trigger beta-entry", reset.Output);
        var inactiveAfterReset = await RunCli(root, "task", "next", "--milestone", "public-beta");
        Assert.DoesNotContain("PM-0006", inactiveAfterReset.Output);
        Assert.Contains("inactive", inactiveAfterReset.Output, StringComparison.OrdinalIgnoreCase);

        var relatch = MoveTask(root, "PM-0002", "done");
        Assert.Equal("beta-entry", Assert.Single(relatch.Payload!.ActivationImpact.ActivatedTriggers).Key);
        var resetBlocked = await RunCli(root, "trigger", "reset", "beta-entry");
        Assert.NotEqual(0, resetBlocked.ExitCode);
        Assert.Contains("cannot be reset", resetBlocked.Output, StringComparison.OrdinalIgnoreCase);

        var redefine = await RunCli(
            root,
            "trigger", "redefine", "beta-entry",
            "--requirements", "task:PM-0001,task:PM-0002,task:PM-0003,milestone:architecture-approved",
            "--yes");
        Assert.Equal(0, redefine.ExitCode);
        Assert.Contains("Activation: pending", redefine.Output);
        Assert.Contains("PM-0006", redefine.Output);
        Assert.True(MoveTask(root, "PM-0003", "done").Success);
        Assert.Contains("PM-0006", (await RunCli(root, "task", "next", "--milestone", "public-beta")).Output);

        var addCycle = await RunCli(
            root,
            "trigger", "add", "beta-cycle", "Beta cycle",
            "--requirements", "task:PM-0006");
        Assert.Equal(0, addCycle.ExitCode);
        var beforeCycle = File.ReadAllText(root.ConfigPath);
        var cycle = await RunCli(root, "trigger", "attach", "beta-cycle", "public-beta");
        Assert.NotEqual(0, cycle.ExitCode);
        Assert.Contains("cycle", cycle.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeCycle, File.ReadAllText(root.ConfigPath));
        Assert.Equal(0, (await RunCli(root, "trigger", "remove", "beta-cycle")).ExitCode);

        var delivered = await RunCli(
            root,
            "milestone", "deliver", "public-beta",
            "--reason", "The remaining beta validation is accepted for dogfood.",
            "--yes");
        Assert.Equal(0, delivered.ExitCode);
        Assert.Contains("exceptional", delivered.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PM-0006", delivered.Output);
        Assert.Contains("delivered", (await RunCli(
            root, "task", "next", "--milestone", "public-beta")).Output,
            StringComparison.OrdinalIgnoreCase);

        var reopened = await RunCli(root, "milestone", "reopen", "public-beta");
        Assert.Equal(0, reopened.ExitCode);
        Assert.Contains("Lifecycle: Active", reopened.Output);
        Assert.Contains("PM-0006", (await RunCli(
            root, "task", "next", "--milestone", "public-beta")).Output);
    }

    [Fact]
    public async Task StdioMcpExercisesTrustedLifecycleRecoveryAndRunWorkerBoundary()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateMcpFixture(workspace);

        await using (var client = await CreateMcpClient(root))
        {
            var tools = await client.ListToolsAsync();
            Assert.Contains(tools, tool => tool.Name == "override_activation_trigger");
            Assert.Contains(tools, tool => tool.Name == "deliver_milestone");

            var pending = await Call(client, "get_activation_switchboard");
            Assert.Contains("\"lifecycle\":\"inactive\"", Json(pending));

            await AssertMutationMatchesReread(client, await Call(
                client,
                "override_activation_trigger",
                ("key", "beta-entry"),
                ("reason", "Proceed with reviewed beta risk.")));
            var reset = await AssertMutationMatchesReread(
                client,
                await Call(client, "reset_activation_trigger", ("key", "beta-entry")));
            Assert.True(reset.GetProperty("changed").GetBoolean());

            var automatic = await Call(
                client,
                "move_task",
                ("taskId", "PM-0001"),
                ("targetState", "done"));
            Assert.NotEqual(true, automatic.IsError);
            Assert.Contains("automatic", Json(await Call(client, "get_activation_switchboard")));
            var blockedReset = await Call(client, "reset_activation_trigger", ("key", "beta-entry"));
            Assert.Contains("activation_trigger_reset_blocked", Json(blockedReset));

            var requirements = new[] { new { kind = "task", source = "PM-0003" } };
            var preview = await Call(
                client,
                "preview_activation_trigger_redefinition",
                ("key", "beta-entry"),
                ("requirements", requirements));
            var previewRevision = Data(preview).GetProperty("revision").GetString()!;
            await AssertMutationMatchesReread(client, await Call(
                client,
                "redefine_activation_trigger",
                ("key", "beta-entry"),
                ("requirements", requirements),
                ("expectedRevision", previewRevision),
                ("allowDeactivation", true)));
            Assert.Contains("inactive", Json(await Call(client, "get_activation_switchboard")));

            await Call(
                client,
                "move_task",
                ("taskId", "PM-0003"),
                ("targetState", "done"));
            Assert.Contains("automatic", Json(await Call(client, "get_activation_switchboard")));

            var deliveryPreview = await Call(
                client,
                "preview_milestone_delivery",
                ("key", "public-beta"),
                ("reason", "Accept the remaining beta work for dogfood."));
            Assert.True(Data(deliveryPreview).GetProperty("requiresConfirmation").GetBoolean());
            var deliveryRevision = Data(deliveryPreview).GetProperty("revision").GetString()!;
            await AssertMutationMatchesReread(client, await Call(
                client,
                "deliver_milestone",
                ("key", "public-beta"),
                ("expectedRevision", deliveryRevision),
                ("reason", "Accept the remaining beta work for dogfood."),
                ("allowExceptional", true)));
            Assert.Contains("delivered", Json(await Call(client, "get_activation_switchboard")));
            await AssertMutationMatchesReread(
                client,
                await Call(client, "reopen_milestone", ("key", "public-beta")));

            var cycleRequirements = new[] { new { kind = "task", source = "PM-0002" } };
            await AssertMutationMatchesReread(client, await Call(
                client,
                "add_activation_trigger",
                ("key", "beta-cycle"),
                ("title", "Beta cycle"),
                ("requirements", cycleRequirements)));
            var beforeCycle = File.ReadAllText(root.ConfigPath);
            var cycle = await Call(
                client,
                "attach_activation_trigger_to_milestone",
                ("key", "beta-cycle"),
                ("milestone", "public-beta"));
            Assert.Contains("activation_cycle", Json(cycle));
            Assert.Equal(beforeCycle, File.ReadAllText(root.ConfigPath));
            await AssertMutationMatchesReread(
                client,
                await Call(client, "remove_activation_trigger", ("key", "beta-cycle")));
        }

        var config = ProjectConfig.ReadConfig(root);
        config.ActivationTriggers["beta-entry"].Activation = null;
        config.WriteConfig(root);
        var inconsistent = File.ReadAllText(root.ConfigPath);

        await using (var recovery = await CreateMcpClient(root))
        {
            var dryRun = await Call(
                recovery,
                "reconcile_activation_triggers",
                ("dryRun", true));
            Assert.Contains("beta-entry", Json(dryRun));
            Assert.Equal(inconsistent, File.ReadAllText(root.ConfigPath));
            var applied = await AssertMutationMatchesReread(recovery, await Call(
                recovery,
                "reconcile_activation_triggers",
                ("dryRun", false)));
            Assert.Contains(".pm/pm_config.yaml", MutationPaths(applied));

            var validation = await Call(recovery, "validate_project");
            Assert.Contains("\"valid\":true", Json(validation));
        }

        await using (var worker = await CreateMcpClient(root, "run-worker", "PM-0002"))
        {
            var tools = await worker.ListToolsAsync();
            var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("get_activation_switchboard", names);
            Assert.DoesNotContain("activate_activation_trigger", names);
            Assert.DoesNotContain("redefine_activation_trigger", names);
            Assert.DoesNotContain("deliver_milestone", names);
            Assert.DoesNotContain("reconcile_activation_triggers", names);
            Assert.NotEqual(true, (await Call(worker, "get_activation_switchboard")).IsError);
            var deniedRead = await Call(
                worker, "get_activation_switchboard", ("project", "parent"));
            Assert.Contains("mcp_project_scope_denied", Json(deniedRead));

            var unadvertised = await Record.ExceptionAsync(async () =>
                await Call(worker, "activate_activation_trigger", ("key", "beta-entry")));
            Assert.NotNull(unadvertised);
        }
    }

    private static async Task<ProjectRoot> CreateCliFixture(TempWorkingDirectory workspace)
    {
        var config = TestData.Config(milestones: new()
        {
            ["foundation"] = "Foundation",
            ["architecture-approved"] = "Architecture approved",
            ["public-beta"] = "Public beta",
        });
        config.ActivationTriggers["beta-entry"] = new ActivationTriggerDefinition
        {
            Title = "Beta entry",
            Requirements =
            [
                new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0001" },
                new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0002" },
                new ActivationRequirement
                {
                    Kind = ActivationRequirementKind.Milestone,
                    Source = "architecture-approved",
                },
            ],
        };
        config.ActivationTriggers["risk-entry"] = new ActivationTriggerDefinition
        {
            Title = "Risk accepted",
            Requirements =
            [
                new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0005" },
            ],
        };
        config.Milestones["public-beta"].RequiredActivationTriggers = ["beta-entry", "risk-entry"];
        var root = await workspace.CreateProject(config);
        AddTask(root, "PM-0001", "Core storage", "foundation", "todo");
        AddTask(root, "PM-0002", "Domain model", "foundation", "todo");
        AddTask(root, "PM-0003", "Import path", "foundation", "todo");
        AddTask(root, "PM-0004", "Approve architecture", "architecture-approved", "done");
        AddTask(root, "PM-0005", "Recovery tools", "foundation", "todo");
        AddTask(root, "PM-0006", "Beta validation", "public-beta", "todo");
        return root;
    }

    private static async Task<ProjectRoot> CreateMcpFixture(TempWorkingDirectory workspace)
    {
        var config = TestData.Config(milestones: new() { ["public-beta"] = "Public beta" });
        config.ActivationTriggers["beta-entry"] = new ActivationTriggerDefinition
        {
            Title = "Beta entry",
            Requirements =
            [
                new ActivationRequirement { Kind = ActivationRequirementKind.Task, Source = "PM-0001" },
            ],
        };
        config.Milestones["public-beta"].RequiredActivationTriggers = ["beta-entry"];
        var root = await workspace.CreateProject(config);
        AddTask(root, "PM-0001", "Foundation capability", null, "todo");
        AddTask(root, "PM-0002", "Beta validation", "public-beta", "todo");
        AddTask(root, "PM-0003", "Replacement requirement", null, "todo");
        return root;
    }

    private static void AddTask(
        ProjectRoot root,
        string id,
        string title,
        string? milestone,
        string state)
    {
        var task = TestData.Task(id, title, milestone: milestone);
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
    }

    private static async Task<ProcessResult> RunCli(ProjectRoot root, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root.RepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(PmMcpTools).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["NO_COLOR"] = "1";
        start.Environment["COLUMNS"] = "240";

        using var process = Process.Start(start) ??
                            throw new InvalidOperationException("Could not start PM CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdout + await stderr);
    }

    private static AppResult<LifecycleMutationResult<TaskItem>> MoveTask(
        ProjectRoot root,
        string taskId,
        string targetState)
    {
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = root.RepositoryPath;
        try
        {
            var refreshed = new ProjectRoot();
            var result = TestTaskServices.Create(refreshed, new UnusedNextIdService())
                .MoveTask(taskId, targetState);
            Assert.True(result.Success, result.Message);
            return result;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private static async Task<McpClient> CreateMcpClient(
        ProjectRoot root,
        string? profile = null,
        string? taskId = null)
    {
        var arguments = new List<string> { typeof(PmMcpTools).Assembly.Location, "mcp" };
        if (profile != null)
        {
            arguments.AddRange(["--profile", profile, "--task-id", taskId!]);
        }
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = $"PM activation integration {profile ?? "normal"}",
            Command = "dotnet",
            Arguments = arguments,
            WorkingDirectory = root.RepositoryPath,
            InheritEnvironmentVariables = true,
        });
        return await McpClient.CreateAsync(transport);
    }

    private static ValueTask<CallToolResult> Call(
        McpClient client,
        string name,
        params (string Name, object Value)[] arguments) =>
        client.CallToolAsync(
            name,
            arguments.ToDictionary(argument => argument.Name, argument => (object?)argument.Value));

    private static async Task<JsonElement> AssertMutationMatchesReread(
        McpClient client,
        CallToolResult mutation)
    {
        Assert.NotEqual(true, mutation.IsError);
        var data = Data(mutation);
        var reread = await Call(client, "get_activation_switchboard");
        Assert.NotEqual(true, reread.IsError);
        Assert.Equal(
            Data(reread).GetRawText(),
            data.GetProperty("switchboard").GetRawText());
        return data;
    }

    private static JsonElement Data(CallToolResult result) =>
        JsonDocument.Parse(Json(result)).RootElement.GetProperty("data").Clone();

    private static string MutationPaths(JsonElement data) =>
        string.Join(",", data.GetProperty("mutation").GetProperty("changedPaths")
            .EnumerateArray().Select(path => path.GetString()));

    private static string Json(CallToolResult result) =>
        JsonSerializer.Serialize(result.StructuredContent);

    private sealed class UnusedNextIdService : INextIdService
    {
        public Task<int> GetNextId(
            ProjectRoot projectRoot,
            string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> PeekNextId(
            ProjectRoot projectRoot,
            string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int?> PeekExistingNextId(
            ProjectRoot projectRoot,
            string track,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProjectRegistration> RegisterProject(
            ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> Healthy(
            ProjectConfig config,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
