using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PM.Application;
using PM.Mcp;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

public sealed class CrossProjectMilestoneActivationEndToEndTests
{
    [Fact]
    public async Task TrustedLinkedMovePreservesAtomicStateAndStructuredReceipt()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        Assert.True(new ProjectConfigService(fixture.Starfall).AddStatus("doing", "Doing").Success);
        fixture.Starfall.SetTaskOrder(new TaskOrderScope("STAR", "todo", "m1"), ["STAR-0001"]);
        fixture.Starfall.SetTaskOrder(new TaskOrderScope("STAR", "doing", "m1"), []);
        var registry = fixture.Registry();
        Assert.True(registry.Remember(fixture.Starfall).Success);
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        var sourcePath = Path.Combine(fixture.Starfall.StatesPath, "todo", "STAR-0001.ref");
        var destinationPath = Path.Combine(fixture.Starfall.StatesPath, "doing", "STAR-0001.ref");
        var originalSource = File.ReadAllText(sourcePath);
        var originalOrder = File.ReadAllText(fixture.Starfall.TaskOrderPath);

        await using var client = await CreateMcpClient(fixture);
        var invalid = await Call(
            client,
            "move_task",
            ("taskId", "STAR-0001"),
            ("targetState", "missing"),
            ("project", "starfall"));
        using (var invalidDocument = JsonDocument.Parse(Json(invalid)))
        {
            Assert.False(invalidDocument.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("invalid_state", invalidDocument.RootElement.GetProperty("errorCode").GetString());
        }
        Assert.Equal(originalSource, File.ReadAllText(sourcePath));
        Assert.False(File.Exists(destinationPath));
        Assert.Equal(originalOrder, File.ReadAllText(fixture.Starfall.TaskOrderPath));

        var moved = await Call(
            client,
            "move_task",
            ("taskId", "STAR-0001"),
            ("targetState", "doing"),
            ("project", "starfall"));
        Assert.NotEqual(true, moved.IsError);
        using (var movedDocument = JsonDocument.Parse(Json(moved)))
        {
            var root = movedDocument.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            var receipt = root.GetProperty("data").GetProperty("mutation");
            Assert.Equal("prj_starfall", receipt.GetProperty("projectId").GetString());
            var changedPaths = receipt.GetProperty("changedPaths")
                .EnumerateArray().Select(path => path.GetString()).ToList();
            Assert.Contains(".pm/states/todo/STAR-0001.ref", changedPaths);
            Assert.Contains(".pm/states/doing/STAR-0001.ref", changedPaths);
            Assert.Contains(".pm/task_order.yaml", changedPaths);
        }

        Assert.False(File.Exists(sourcePath));
        var relativeTasks = Path.GetRelativePath(Path.GetDirectoryName(destinationPath)!, fixture.Starfall.TasksPath);
        Assert.Equal($"{relativeTasks}/STAR-0001.md", File.ReadAllText(destinationPath));
        Assert.Empty(fixture.Starfall.GetTaskOrder(new TaskOrderScope("STAR", "todo", "m1")));
        Assert.Equal(["STAR-0001"],
            fixture.Starfall.GetTaskOrder(new TaskOrderScope("STAR", "doing", "m1")));

        var reread = await Call(client, "get_task", ("taskId", "STAR-0001"), ("project", "starfall"));
        Assert.Equal("doing", Data(reread).GetProperty("state").GetString());
    }

    [Fact]
    public async Task TrustedLinkedMoveReturnsStructuredFailureWithoutOrphaningSourceState()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        Assert.True(new ProjectConfigService(fixture.Starfall).AddStatus("doing", "Doing").Success);
        var registry = fixture.Registry();
        Assert.True(registry.Remember(fixture.Starfall).Success);
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        var sourcePath = Path.Combine(fixture.Starfall.StatesPath, "todo", "STAR-0001.ref");
        var destinationPath = Path.Combine(fixture.Starfall.StatesPath, "doing", "STAR-0001.ref");
        var originalSource = File.ReadAllText(sourcePath);
        Directory.CreateDirectory(destinationPath);

        await using var client = await CreateMcpClient(fixture);
        var failed = await Call(
            client,
            "move_task",
            ("taskId", "STAR-0001"),
            ("targetState", "doing"),
            ("project", "starfall"));

        using var document = JsonDocument.Parse(Json(failed));
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("task_state_write_failed", document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(originalSource, File.ReadAllText(sourcePath));
        Assert.True(Directory.Exists(destinationPath));
    }

    [Fact]
    public async Task CrossProjectTaskCompletionLatchesOnlyTheOwningProjectsDuplicateTrigger()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        ConfigureTaskGate(fixture.Starfall, "STAR-GATE", "shared-entry");
        ConfigureTaskGate(fixture.Royale, "STAR-GATE", "shared-entry");
        WriteTask(fixture.Starfall, "STAR-GATE", "Starfall entry gate", "STAR", "todo", null);
        WriteTask(fixture.Royale, "STAR-GATE", "Royale duplicate gate", "ROYALE", "done", null);

        var registry = fixture.Registry();
        Assert.True(registry.Remember(fixture.Starfall).Success);
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        var before = SnapshotFamily(fixture);

        await using var client = await CreateMcpClient(fixture);
        var currentBefore = Data(await Call(client, "get_activation_switchboard"));
        Assert.Equal("prj_royale", Owner(await Call(client, "get_activation_switchboard")));
        AssertTrigger(currentBefore, "shared-entry", requirementsSatisfied: true, isActive: false);
        Assert.Equal(JsonValueKind.Null, currentBefore.GetProperty("activationTriggers")
            .EnumerateArray().Single().GetProperty("activation").ValueKind);
        Assert.All(currentBefore.GetProperty("milestones").EnumerateArray(), milestone =>
            Assert.True(milestone.TryGetProperty("delivery", out _)));
        Assert.All(currentBefore.GetProperty("issues").EnumerateArray(), issue =>
        {
            Assert.True(issue.TryGetProperty("taskId", out _));
            Assert.True(issue.TryGetProperty("wikiPath", out _));
            Assert.True(issue.TryGetProperty("state", out _));
            Assert.True(issue.TryGetProperty("projectId", out _));
            Assert.True(issue.TryGetProperty("projectAlias", out _));
        });

        var selectedBefore = await Call(
            client, "get_activation_switchboard", ("project", "starfall"));
        Assert.Equal("prj_starfall", Owner(selectedBefore));
        AssertTrigger(Data(selectedBefore), "shared-entry", requirementsSatisfied: false, isActive: false);
        Assert.DoesNotContain("STAR-0001", Json(await Call(
            client, "get_next_task", ("project", "starfall"), ("milestone", "m1"))));

        var moved = await Call(
            client,
            "move_task",
            ("taskId", "STAR-GATE"),
            ("targetState", "done"),
            ("project", "starfall"));
        Assert.NotEqual(true, moved.IsError);
        using (var movedDocument = JsonDocument.Parse(Json(moved)))
        {
            var receipt = movedDocument.RootElement.GetProperty("data").GetProperty("mutation");
            Assert.Equal("prj_starfall", receipt.GetProperty("projectId").GetString());
            var changedPaths = receipt.GetProperty("changedPaths")
                .EnumerateArray().Select(path => path.GetString()).ToList();
            Assert.Contains(".pm/pm_config.yaml", changedPaths);
            Assert.Contains(".pm/states/done/STAR-GATE.ref", changedPaths);
        }

        var selectedAfter = await Call(
            client, "get_activation_switchboard", ("project", "prj_starfall"));
        Assert.Equal("prj_starfall", Owner(selectedAfter));
        AssertTrigger(Data(selectedAfter), "shared-entry", requirementsSatisfied: true, isActive: true,
            activationMode: "automatic");
        Assert.Contains("STAR-0001", Json(await Call(
            client, "get_next_task", ("project", "starfall"), ("milestone", "m1"))));

        var currentAfter = Data(await Call(client, "get_activation_switchboard"));
        AssertTrigger(currentAfter, "shared-entry", requirementsSatisfied: true, isActive: false);
        AssertOnlyChanged(before, SnapshotFamily(fixture), "prj_starfall");

        await using var worker = await CreateMcpClient(fixture, "run-worker", "ROYALE-0001");
        var names = (await worker.ListToolsAsync()).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("get_activation_switchboard", names);
        Assert.DoesNotContain("activate_activation_trigger", names);
        Assert.DoesNotContain("redefine_activation_trigger", names);
        Assert.DoesNotContain("deliver_milestone", names);
        Assert.DoesNotContain("reconcile_activation_triggers", names);
        Assert.Equal("prj_royale", Owner(await Call(worker, "get_activation_switchboard")));
        Assert.Contains("mcp_project_scope_denied", Json(await Call(
            worker, "get_activation_switchboard", ("project", "starfall"))));
        Assert.NotNull(await Record.ExceptionAsync(async () =>
            await Call(worker, "activate_activation_trigger", ("key", "shared-entry"))));
    }

    [Fact]
    public async Task EverySelectedActivationToolRejectsAnUnavailableProjectWithoutFamilyWrites()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        var before = SnapshotFamily(fixture);
        await using var client = await CreateMcpClient(fixture);
        var requirements = new[] { new { kind = "task", source = "STAR-0001" } };
        var invocations = new (string Name, IReadOnlyDictionary<string, object?> Arguments)[]
        {
            ("get_project", Args(("project", "missing"))),
            ("list_milestones", Args(("project", "missing"))),
            ("get_activation_switchboard", Args(("project", "missing"))),
            ("add_milestone", Args(("key", "release"), ("title", "Release"), ("project", "missing"))),
            ("rename_milestone", Args(("key", "m1"), ("title", "Release"), ("project", "missing"))),
            ("remove_milestone", Args(("key", "m1"), ("project", "missing"))),
            ("set_milestone_priority", Args(("key", "m1"), ("priority", "high"), ("project", "missing"))),
            ("set_milestone_description", Args(("key", "m1"), ("description", "Outcome"), ("project", "missing"))),
            ("add_activation_trigger", Args(("key", "entry"), ("title", "Entry"),
                ("requirements", requirements), ("project", "missing"))),
            ("rename_activation_trigger", Args(("key", "entry"), ("title", "Entry"), ("project", "missing"))),
            ("remove_activation_trigger", Args(("key", "entry"), ("project", "missing"))),
            ("set_activation_trigger_requirements", Args(("key", "entry"),
                ("requirements", requirements), ("project", "missing"))),
            ("attach_activation_trigger_to_milestone", Args(("key", "entry"), ("milestone", "m1"),
                ("project", "missing"))),
            ("detach_activation_trigger_from_milestone", Args(("key", "entry"), ("milestone", "m1"),
                ("project", "missing"))),
            ("activate_activation_trigger", Args(("key", "entry"), ("project", "missing"))),
            ("override_activation_trigger", Args(("key", "entry"), ("reason", "Reviewed risk."),
                ("project", "missing"))),
            ("reset_activation_trigger", Args(("key", "entry"), ("project", "missing"))),
            ("reconcile_activation_triggers", Args(("dryRun", true), ("project", "missing"))),
            ("preview_activation_trigger_redefinition", Args(("key", "entry"),
                ("requirements", requirements), ("project", "missing"))),
            ("redefine_activation_trigger", Args(("key", "entry"), ("requirements", requirements),
                ("expectedRevision", "invalid"), ("project", "missing"))),
            ("preview_milestone_delivery", Args(("key", "m1"), ("project", "missing"))),
            ("deliver_milestone", Args(("key", "m1"), ("expectedRevision", "invalid"),
                ("project", "missing"))),
            ("reopen_milestone", Args(("key", "m1"), ("project", "missing"))),
        };

        foreach (var invocation in invocations)
        {
            var result = await client.CallToolAsync(invocation.Name, invocation.Arguments);
            Assert.Contains("linked_project_unavailable", Json(result));
            Assert.Equal(before, SnapshotFamily(fixture));
        }
    }

    [Fact]
    public async Task ReconciliationRepairsASelectedProjectsMissingAutomaticLatchWithoutTouchingItsFamily()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        ConfigureTaskGate(fixture.Starfall, "RECOVERY-GATE", "recovery-entry");
        var gate = WriteTask(
            fixture.Starfall, "RECOVERY-GATE", "Imported completed gate", "STAR", "todo", null);
        fixture.Starfall.UpdateTaskState(gate, "done");
        var registry = fixture.Registry();
        Assert.True(registry.Remember(fixture.Starfall).Success);
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);

        await using var client = await CreateMcpClient(fixture);
        var inconsistent = await Call(
            client, "get_activation_switchboard", ("project", "starfall"));
        var inconsistentData = Data(inconsistent);
        AssertTrigger(inconsistentData, "recovery-entry", requirementsSatisfied: true, isActive: false);
        Assert.Contains("activation_reconciliation_required", Json(inconsistent));

        var beforeDryRun = SnapshotFamily(fixture);
        var dryRun = Data(await Call(
            client, "reconcile_activation_triggers", ("dryRun", true), ("project", "starfall")));
        Assert.False(dryRun.GetProperty("changed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, dryRun.GetProperty("mutation").ValueKind);
        Assert.Contains("recovery-entry", dryRun.GetProperty("impact")
            .GetProperty("automaticActivation").GetProperty("activatedTriggers")
            .EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(beforeDryRun, SnapshotFamily(fixture));

        var applied = Data(await Call(
            client, "reconcile_activation_triggers", ("project", "prj_starfall")));
        Assert.Equal("prj_starfall", applied.GetProperty("mutation").GetProperty("projectId").GetString());
        Assert.Equal([".pm/pm_config.yaml"], applied.GetProperty("mutation").GetProperty("changedPaths")
            .EnumerateArray().Select(path => path.GetString()).ToList());
        AssertTrigger(applied.GetProperty("switchboard"), "recovery-entry",
            requirementsSatisfied: true, isActive: true, activationMode: "automatic");
        AssertOnlyChanged(beforeDryRun, SnapshotFamily(fixture), "prj_starfall");

        var reread = await Call(client, "get_activation_switchboard", ("project", "starfall"));
        Assert.Equal(applied.GetProperty("switchboard").GetRawText(), Data(reread).GetRawText());
        Assert.DoesNotContain("activation_reconciliation_required", Json(reread));
    }

    [Fact]
    public async Task DisappearedTrustedTargetFailsWithoutMutatingTheMovedRepositoryOrItsFamily()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        var registry = fixture.Registry();
        Assert.True(registry.Remember(fixture.Starfall).Success);
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        var before = SnapshotFamily(fixture);
        await using var client = await CreateMcpClient(fixture);

        var movedPath = Path.Combine(fixture.Workspace.Path, "starfall-disappeared");
        Directory.Move(fixture.Starfall.RepositoryPath, movedPath);

        var result = await Call(
            client, "activate_activation_trigger", ("key", "anything"), ("project", "starfall"));

        Assert.Contains("linked_project_unavailable", Json(result));
        Assert.Equal(before["prj_starfall"], SnapshotPmRoot(Path.Combine(movedPath, GlobalConfig.PmDirName)));
        Assert.Equal(before["prj_games"], SnapshotProject(fixture.Games));
        Assert.Equal(before["prj_royale"], SnapshotProject(fixture.Royale));
    }

    [Fact]
    public async Task TrustedBindingChangeDuringFamilyResolutionIsRejectedBeforeMutation()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        var registry = fixture.Registry();
        Assert.True(registry.Remember(fixture.Starfall).Success);
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        var before = SnapshotFamily(fixture);
        var reboundPath = Path.Combine(fixture.Workspace.Path, "starfall-rebound");
        CopyDirectory(fixture.Starfall.RepositoryPath, reboundPath);
        Assert.True(ProjectRoot.TryOpenExact(reboundPath, out var rebound));

        var inspector = new BlockingSubmoduleInspector("missing-game");
        var family = new LinkedProjectFamilyService(
            fixture.Royale,
            new LinkedProjectService(fixture.Royale),
            new LinkedProjectResolver(registry, inspector));
        var mutations = new LinkedProjectMutationService(
            fixture.Royale,
            new UnusedNextIdService(),
            family,
            registry,
            new TaskServiceFactory(TimeProvider.System),
            TimeProvider.System);

        var resolving = mutations.ResolveTargetAsync("starfall");
        await inspector.Reached.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(registry.Bind("prj_starfall", rebound.RepositoryPath, replace: true).Success);
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        inspector.Resume();

        var result = await resolving;

        Assert.False(result.Success);
        Assert.Equal("linked_project_binding_mismatch", result.ErrorCode);
        Assert.Equal(before, SnapshotFamily(fixture));
        Assert.Equal(before["prj_starfall"], SnapshotProject(rebound));
    }

    private static void ConfigureTaskGate(ProjectRoot root, string taskId, string triggerKey)
    {
        root.Config!.ActivationTriggers[triggerKey] = new ActivationTriggerDefinition
        {
            Title = "Shared entry",
            Requirements =
            [
                new ActivationRequirement
                {
                    Kind = ActivationRequirementKind.Task,
                    Source = taskId,
                },
            ],
        };
        root.Config.Milestones["m1"].RequiredActivationTriggers = [triggerKey];
        root.Config.WriteConfig(root);
    }

    private static TaskItem WriteTask(
        ProjectRoot root,
        string id,
        string title,
        string track,
        string state,
        string? milestone)
    {
        var task = TestData.Task(id, title, track: track, milestone: milestone);
        root.WriteTask(task);
        root.UpdateTaskState(task, state);
        return task;
    }

    private static async Task<McpClient> CreateMcpClient(
        LinkedProjectIntegrationFixture fixture,
        string? profile = null,
        string? taskId = null)
    {
        var arguments = new List<string> { typeof(PmMcpTools).Assembly.Location, "mcp" };
        if (profile != null)
            arguments.AddRange(["--profile", profile, "--task-id", taskId!]);
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["PM_PROJECT_REGISTRY_PATH"] = fixture.RegistryPath;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = $"PM cross-project activation acceptance {profile ?? "normal"}",
            Command = "dotnet",
            Arguments = arguments,
            WorkingDirectory = fixture.Royale.RepositoryPath,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
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

    private static IReadOnlyDictionary<string, object?> Args(params (string Name, object Value)[] arguments) =>
        arguments.ToDictionary(argument => argument.Name, argument => (object?)argument.Value);

    private static string Json(CallToolResult result) => JsonSerializer.Serialize(result.StructuredContent);

    private static JsonElement Data(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return JsonDocument.Parse(Json(result)).RootElement.GetProperty("data").Clone();
    }

    private static string Owner(CallToolResult result) =>
        JsonDocument.Parse(Json(result)).RootElement.GetProperty("project").GetProperty("projectId").GetString()!;

    private static void AssertTrigger(
        JsonElement switchboard,
        string key,
        bool requirementsSatisfied,
        bool isActive,
        string? activationMode = null)
    {
        var trigger = switchboard.GetProperty("activationTriggers").EnumerateArray()
            .Single(candidate => candidate.GetProperty("key").GetString() == key);
        Assert.Equal(requirementsSatisfied, trigger.GetProperty("requirementsSatisfied").GetBoolean());
        Assert.Equal(isActive, trigger.GetProperty("isActive").GetBoolean());
        if (activationMode != null)
            Assert.Equal(activationMode, trigger.GetProperty("activation").GetProperty("mode").GetString());
    }

    private static IReadOnlyDictionary<string, string> SnapshotFamily(LinkedProjectIntegrationFixture fixture) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prj_games"] = SnapshotProject(fixture.Games),
            ["prj_royale"] = SnapshotProject(fixture.Royale),
            ["prj_starfall"] = SnapshotProject(fixture.Starfall),
        };

    private static string SnapshotProject(ProjectRoot root) => SnapshotPmRoot(root.RootPath);

    private static string SnapshotPmRoot(string pmRoot) => string.Join(
        "\n---FILE---\n",
        Directory.EnumerateFiles(pmRoot, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{Path.GetRelativePath(pmRoot, path)}\n{File.ReadAllText(path)}"));

    private static void AssertOnlyChanged(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        string changedProjectId)
    {
        Assert.NotEqual(before[changedProjectId], after[changedProjectId]);
        foreach (var projectId in before.Keys.Where(projectId => projectId != changedProjectId))
            Assert.Equal(before[projectId], after[projectId]);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destinationFile = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile);
        }
    }

    private sealed class BlockingSubmoduleInspector(string pathHint) : ILinkedProjectSubmoduleInspector
    {
        private readonly TaskCompletionSource _reached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reached => _reached.Task;

        public void Resume() => _resume.TrySetResult();

        public async Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
            string repositoryPath,
            string inspectedPathHint,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(inspectedPathHint, pathHint, StringComparison.Ordinal))
            {
                _reached.TrySetResult();
                await _resume.Task.WaitAsync(cancellationToken);
            }

            return AppResult<LinkedProjectRepairAction?>.Ok(null);
        }
    }

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
}
