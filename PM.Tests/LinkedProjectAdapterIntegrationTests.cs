using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PM.Application;
using PM.Mcp;
using PM.Project;

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
        var duplicate = TestData.Task(
            "STAR-0001", "Completed duplicate", track: "ROYALE", milestone: "m1");
        fixture.Royale.WriteTask(duplicate);
        fixture.Royale.UpdateTaskState(duplicate, "done");
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
        foreach (var tool in new[]
                 {
                     "add_milestone",
                     "rename_milestone",
                     "remove_milestone",
                     "set_milestone_priority",
                     "set_milestone_description",
                     "add_activation_trigger",
                     "rename_activation_trigger",
                     "remove_activation_trigger",
                     "set_activation_trigger_requirements",
                     "attach_activation_trigger_to_milestone",
                     "detach_activation_trigger_from_milestone",
                 })
            AssertSchemaProperty(tools, tool, "project");
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
        var beforeDeniedDefinition = File.ReadAllText(fixture.Starfall.ConfigPath);
        var deniedDefinition = await Call(
            client,
            "add_milestone",
            ("key", "linked-release"),
            ("title", "Linked release"),
            ("project", "starfall"));
        Assert.Contains("linked_project_write_untrusted", Json(deniedDefinition));
        Assert.Equal(beforeDeniedDefinition, File.ReadAllText(fixture.Starfall.ConfigPath));

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

        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "add_milestone",
            ("key", "linked-release"),
            ("title", "Linked release"),
            ("project", "starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "set_milestone_priority",
            ("key", "linked-release"),
            ("priority", "high"),
            ("project", "prj_starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "set_milestone_description",
            ("key", "linked-release"),
            ("description", "Deliver the linked Starfall release."),
            ("project", "starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "rename_milestone",
            ("key", "linked-release"),
            ("title", "Starfall linked release"),
            ("project", "starfall")), "prj_starfall");

        var requirements = new[] { new { kind = "task", source = "STAR-0001" } };
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "add_activation_trigger",
            ("key", "linked-entry"),
            ("title", "Linked entry"),
            ("requirements", requirements),
            ("project", "starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "rename_activation_trigger",
            ("key", "linked-entry"),
            ("title", "Starfall linked entry"),
            ("project", "prj_starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "set_activation_trigger_requirements",
            ("key", "linked-entry"),
            ("requirements", requirements),
            ("project", "starfall")), "prj_starfall");
        var attached = await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "attach_activation_trigger_to_milestone",
            ("key", "linked-entry"),
            ("milestone", "linked-release"),
            ("project", "starfall")), "prj_starfall");
        var trigger = Assert.Single(attached.GetProperty("switchboard")
            .GetProperty("activationTriggers").EnumerateArray());
        Assert.False(trigger.GetProperty("requirementsSatisfied").GetBoolean());
        Assert.Equal("inactive", attached.GetProperty("switchboard")
            .GetProperty("milestones").EnumerateArray()
            .Single(milestone => milestone.GetProperty("key").GetString() == "linked-release")
            .GetProperty("lifecycle").GetString());

        var beforeCycle = File.ReadAllText(fixture.Starfall.ConfigPath);
        var cycle = await Call(
            client,
            "attach_activation_trigger_to_milestone",
            ("key", "linked-entry"),
            ("milestone", "m1"),
            ("project", "starfall"));
        Assert.Contains("activation_cycle", Json(cycle));
        Assert.Equal(beforeCycle, File.ReadAllText(fixture.Starfall.ConfigPath));

        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "detach_activation_trigger_from_milestone",
            ("key", "linked-entry"),
            ("milestone", "linked-release"),
            ("project", "starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "set_activation_trigger_requirements",
            ("key", "linked-entry"),
            ("requirements", Array.Empty<object>()),
            ("project", "starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "remove_activation_trigger",
            ("key", "linked-entry"),
            ("project", "starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "remove_milestone",
            ("key", "linked-release"),
            ("project", "starfall")), "prj_starfall");

        Assert.True(registry.GrantWriteTrust("prj_games").Success);
        var parentMutation = await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "set_milestone_description",
            ("key", "m1"),
            ("description", "Parent-owned deliverable."),
            ("project", "parent")), "prj_games");
        Assert.Equal("Parent-owned deliverable.", parentMutation.GetProperty("switchboard")
            .GetProperty("milestones").EnumerateArray().Single()
            .GetProperty("description").GetString());
        Assert.NotEqual("Parent-owned deliverable.", fixture.Royale.Config!.Milestones["m1"].Description);

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

    [Fact]
    public async Task StdioMcpControlsActivationLifecyclesInSelectedWriteTrustedProjects()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        fixture.Starfall.Config!.ActivationTriggers["manual-entry"] = new ActivationTriggerDefinition
        {
            Title = "Manual entry",
        };
        fixture.Starfall.Config.ActivationTriggers["override-entry"] = new ActivationTriggerDefinition
        {
            Title = "Override entry",
            Requirements =
            [
                new ActivationRequirement
                {
                    Kind = ActivationRequirementKind.Task,
                    Source = "GATE-0001",
                },
            ],
        };
        fixture.Starfall.Config.ActivationTriggers["redefine-entry"] = new ActivationTriggerDefinition
        {
            Title = "Redefine entry",
            Activation = new ActivationRecord
            {
                At = DateTimeOffset.Parse("2026-08-07T08:00:00Z"),
                Mode = ActivationMode.Manual,
            },
        };
        fixture.Starfall.Config.ActivationTriggers["automatic-entry"] = new ActivationTriggerDefinition
        {
            Title = "Automatic entry",
            Requirements =
            [
                new ActivationRequirement
                {
                    Kind = ActivationRequirementKind.Task,
                    Source = "AUTO-0001",
                },
            ],
        };
        fixture.Starfall.Config.WriteConfig(fixture.Starfall);
        var gate = TestData.Task("GATE-0001", "Starfall gate", track: "STAR", milestone: null);
        fixture.Starfall.WriteTask(gate);
        fixture.Starfall.UpdateTaskState(gate, "todo");
        var automatic = TestData.Task("AUTO-0001", "Automatic gate", track: "STAR", milestone: null);
        fixture.Starfall.WriteTask(automatic);
        fixture.Starfall.UpdateTaskState(automatic, "done");
        var completedDuplicate = TestData.Task(
            "GATE-0001", "Completed Royale duplicate", track: "ROYALE", milestone: null);
        fixture.Royale.WriteTask(completedDuplicate);
        fixture.Royale.UpdateTaskState(completedDuplicate, "done");
        fixture.Games.Config!.ActivationTriggers["parent-entry"] = new ActivationTriggerDefinition
        {
            Title = "Parent entry",
        };
        fixture.Games.Config.WriteConfig(fixture.Games);
        var untouchedRoyaleConfig = File.ReadAllText(fixture.Royale.ConfigPath);

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["PM_PROJECT_REGISTRY_PATH"] = fixture.RegistryPath;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "PM linked activation lifecycle integration",
            Command = "dotnet",
            Arguments = [typeof(PmMcpTools).Assembly.Location, "mcp"],
            WorkingDirectory = fixture.Royale.RepositoryPath,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        foreach (var tool in new[]
                 {
                     "activate_activation_trigger",
                     "override_activation_trigger",
                     "reset_activation_trigger",
                     "reconcile_activation_triggers",
                     "preview_activation_trigger_redefinition",
                     "redefine_activation_trigger",
                 })
            AssertSchemaProperty(tools, tool, "project");

        Assert.NotEqual(true, (await Call(client, "list_linked_projects")).IsError);
        var beforeDenied = File.ReadAllText(fixture.Starfall.ConfigPath);
        var denied = await Call(
            client,
            "activate_activation_trigger",
            ("key", "manual-entry"),
            ("project", "starfall"));
        Assert.Contains("linked_project_write_untrusted", Json(denied));
        var deniedPreview = await Call(
            client,
            "preview_activation_trigger_redefinition",
            ("key", "redefine-entry"),
            ("requirements", Array.Empty<object>()),
            ("project", "starfall"));
        Assert.Contains("linked_project_write_untrusted", Json(deniedPreview));
        Assert.Equal(beforeDenied, File.ReadAllText(fixture.Starfall.ConfigPath));

        var unavailable = await Call(
            client,
            "activate_activation_trigger",
            ("key", "manual-entry"),
            ("project", "missing"));
        Assert.Contains("linked_project_unavailable", Json(unavailable));
        Assert.Equal(beforeDenied, File.ReadAllText(fixture.Starfall.ConfigPath));

        var registry = fixture.Registry();
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        Assert.True(registry.GrantWriteTrust("prj_games").Success);

        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "activate_activation_trigger",
            ("key", "manual-entry"),
            ("project", "starfall")), "prj_starfall");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "reset_activation_trigger",
            ("key", "manual-entry"),
            ("project", "prj_starfall")), "prj_starfall");

        var overridden = await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "override_activation_trigger",
            ("key", "override-entry"),
            ("reason", "Proceed while Starfall finishes its local gate."),
            ("project", "starfall")), "prj_starfall");
        var overriddenTrigger = overridden.GetProperty("switchboard")
            .GetProperty("activationTriggers")
            .EnumerateArray()
            .Single(trigger => trigger.GetProperty("key").GetString() == "override-entry");
        Assert.Equal("override", overriddenTrigger.GetProperty("activation").GetProperty("mode").GetString());
        Assert.False(overriddenTrigger.GetProperty("requirementsSatisfied").GetBoolean());
        var waived = Assert.Single(overriddenTrigger.GetProperty("activation")
            .GetProperty("waivedRequirements").EnumerateArray());
        Assert.Equal("GATE-0001", waived.GetProperty("source").GetString());

        var redefineRequirements = new[] { new { kind = "task", source = "GATE-0001" } };
        var preview = await Call(
            client,
            "preview_activation_trigger_redefinition",
            ("key", "redefine-entry"),
            ("requirements", redefineRequirements),
            ("project", "starfall"));
        Assert.NotEqual(true, preview.IsError);
        string revision;
        using (var previewDocument = JsonDocument.Parse(Json(preview)))
            revision = previewDocument.RootElement.GetProperty("data").GetProperty("revision").GetString()!;
        var redefined = await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "redefine_activation_trigger",
            ("key", "redefine-entry"),
            ("requirements", redefineRequirements),
            ("expectedRevision", revision),
            ("project", "prj_starfall")), "prj_starfall");
        var redefinedTrigger = redefined.GetProperty("switchboard")
            .GetProperty("activationTriggers")
            .EnumerateArray()
            .Single(trigger => trigger.GetProperty("key").GetString() == "redefine-entry");
        Assert.False(redefinedTrigger.GetProperty("isActive").GetBoolean());

        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "override_activation_trigger",
            ("key", "redefine-entry"),
            ("reason", "Exercise stale linked-project preview handling."),
            ("project", "starfall")), "prj_starfall");
        var stalePreview = await Call(
            client,
            "preview_activation_trigger_redefinition",
            ("key", "redefine-entry"),
            ("requirements", Array.Empty<object>()),
            ("project", "starfall"));
        string staleRevision;
        using (var stalePreviewDocument = JsonDocument.Parse(Json(stalePreview)))
            staleRevision = stalePreviewDocument.RootElement.GetProperty("data").GetProperty("revision").GetString()!;
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "activate_activation_trigger",
            ("key", "manual-entry"),
            ("project", "starfall")), "prj_starfall");
        var beforeStaleApply = File.ReadAllText(fixture.Starfall.ConfigPath);
        var staleApply = await Call(
            client,
            "redefine_activation_trigger",
            ("key", "redefine-entry"),
            ("requirements", Array.Empty<object>()),
            ("expectedRevision", staleRevision),
            ("project", "starfall"));
        Assert.Contains("activation_trigger_redefine_stale", Json(staleApply));
        Assert.Equal(beforeStaleApply, File.ReadAllText(fixture.Starfall.ConfigPath));

        var dryRunBefore = File.ReadAllText(fixture.Starfall.ConfigPath);
        var dryRun = await Call(
            client,
            "reconcile_activation_triggers",
            ("dryRun", true),
            ("project", "starfall"));
        Assert.NotEqual(true, dryRun.IsError);
        using (var dryRunDocument = JsonDocument.Parse(Json(dryRun)))
        {
            var data = dryRunDocument.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("changed").GetBoolean());
            Assert.False(data.TryGetProperty("mutation", out _));
            Assert.Contains("automatic-entry", data.GetProperty("impact")
                .GetProperty("automaticActivation")
                .GetProperty("activatedTriggers")
                .EnumerateArray()
                .Select(item => item.GetString()));
        }
        Assert.Equal(dryRunBefore, File.ReadAllText(fixture.Starfall.ConfigPath));

        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "reconcile_activation_triggers",
            ("project", "prj_starfall")), "prj_starfall");
        fixture.Starfall.UpdateTaskState(automatic, "todo");
        var latched = await Call(
            client,
            "reconcile_activation_triggers",
            ("project", "starfall"));
        Assert.NotEqual(true, latched.IsError);
        using (var latchedDocument = JsonDocument.Parse(Json(latched)))
        {
            var data = latchedDocument.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("changed").GetBoolean());
            Assert.False(data.TryGetProperty("mutation", out _));
            var trigger = data.GetProperty("switchboard").GetProperty("activationTriggers")
                .EnumerateArray()
                .Single(item => item.GetProperty("key").GetString() == "automatic-entry");
            Assert.True(trigger.GetProperty("isActive").GetBoolean());
            Assert.False(trigger.GetProperty("requirementsSatisfied").GetBoolean());
            Assert.Equal("automatic", trigger.GetProperty("activation").GetProperty("mode").GetString());
        }

        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "activate_activation_trigger",
            ("key", "parent-entry"),
            ("project", "parent")), "prj_games");
        Assert.Equal(untouchedRoyaleConfig, File.ReadAllText(fixture.Royale.ConfigPath));
    }

    [Fact]
    public async Task StdioMcpDeliversAndReopensMilestonesInSelectedLinkedProjects()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        fixture.Starfall.Config!.ActivationTriggers["starfall-delivered"] = new ActivationTriggerDefinition
        {
            Title = "Starfall delivered",
            Requirements =
            [
                new ActivationRequirement
                {
                    Kind = ActivationRequirementKind.Milestone,
                    Source = "m1",
                },
            ],
        };
        fixture.Starfall.Config.WriteConfig(fixture.Starfall);
        var parentOpen = TestData.Task(
            "PARENT-0002", "Parent release follow-up", track: "SHARED", milestone: "m1");
        fixture.Games.WriteTask(parentOpen);
        fixture.Games.UpdateTaskState(parentOpen, "todo");
        var completedDuplicate = TestData.Task(
            "STAR-0001", "Completed Royale duplicate", track: "ROYALE", milestone: "m1");
        fixture.Royale.WriteTask(completedDuplicate);
        fixture.Royale.UpdateTaskState(completedDuplicate, "done");
        var independentRoyale = TestData.Task(
            "ROYALE-LOCAL", "Independent Royale work", track: "ROYALE", milestone: "m1");
        fixture.Royale.WriteTask(independentRoyale);
        fixture.Royale.UpdateTaskState(independentRoyale, "todo");

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["PM_PROJECT_REGISTRY_PATH"] = fixture.RegistryPath;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "PM linked milestone delivery integration",
            Command = "dotnet",
            Arguments = [typeof(PmMcpTools).Assembly.Location, "mcp"],
            WorkingDirectory = fixture.Royale.RepositoryPath,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        foreach (var tool in new[]
                 {
                     "preview_milestone_delivery",
                     "deliver_milestone",
                     "reopen_milestone",
                 })
            AssertSchemaProperty(tools, tool, "project");

        Assert.NotEqual(true, (await Call(client, "list_linked_projects")).IsError);
        var reason = "Accept the unfinished Starfall work for linked dogfood.";
        var beforeUntrusted = File.ReadAllText(fixture.Starfall.ConfigPath);
        var preview = await Call(
            client,
            "preview_milestone_delivery",
            ("key", "m1"),
            ("reason", reason),
            ("project", "starfall"));
        Assert.NotEqual(true, preview.IsError);
        string previewRevision;
        using (var previewDocument = JsonDocument.Parse(Json(preview)))
        {
            var data = previewDocument.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("requiresConfirmation").GetBoolean());
            Assert.Equal(["STAR-0001"], data.GetProperty("unfinishedTaskIds")
                .EnumerateArray().Select(item => item.GetString()).ToList());
            previewRevision = data.GetProperty("revision").GetString()!;
        }

        var denied = await Call(
            client,
            "deliver_milestone",
            ("key", "m1"),
            ("expectedRevision", previewRevision),
            ("reason", reason),
            ("allowExceptional", true),
            ("project", "starfall"));
        Assert.Contains("linked_project_write_untrusted", Json(denied));
        Assert.Equal(beforeUntrusted, File.ReadAllText(fixture.Starfall.ConfigPath));
        var unavailable = await Call(
            client,
            "preview_milestone_delivery",
            ("key", "m1"),
            ("reason", reason),
            ("project", "missing"));
        Assert.Contains("linked_project_unavailable", Json(unavailable));

        var registry = fixture.Registry();
        Assert.True(registry.GrantWriteTrust("prj_starfall").Success);
        Assert.True(registry.GrantWriteTrust("prj_games").Success);
        var parentBeforeCrossProject = File.ReadAllText(fixture.Games.ConfigPath);
        var crossProject = await Call(
            client,
            "deliver_milestone",
            ("key", "m1"),
            ("expectedRevision", previewRevision),
            ("reason", reason),
            ("allowExceptional", true),
            ("project", "parent"));
        Assert.Contains("milestone_delivery_stale", Json(crossProject));
        Assert.Equal(parentBeforeCrossProject, File.ReadAllText(fixture.Games.ConfigPath));

        var beforeDeliveryRecommendation = await Call(
            client,
            "get_next_task",
            ("project", "starfall"),
            ("milestone", "m1"));
        Assert.Contains("STAR-0001", Json(beforeDeliveryRecommendation));
        var delivered = await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "deliver_milestone",
            ("key", "m1"),
            ("expectedRevision", previewRevision),
            ("reason", reason),
            ("allowExceptional", true),
            ("project", "starfall")), "prj_starfall");
        var deliveredMilestone = delivered.GetProperty("switchboard")
            .GetProperty("milestones")
            .EnumerateArray()
            .Single(milestone => milestone.GetProperty("key").GetString() == "m1");
        Assert.Equal("exceptional", deliveredMilestone.GetProperty("delivery").GetProperty("mode").GetString());
        Assert.Equal(["STAR-0001"], deliveredMilestone.GetProperty("delivery")
            .GetProperty("acceptedTaskIds").EnumerateArray().Select(item => item.GetString()).ToList());
        Assert.Contains("starfall-delivered", delivered.GetProperty("impact")
            .GetProperty("automaticActivation")
            .GetProperty("activatedTriggers")
            .EnumerateArray()
            .Select(item => item.GetString()));
        Assert.DoesNotContain(
            "starfall-delivered",
            ProjectConfig.ReadConfig(fixture.Games).ActivationTriggers.Keys);

        var afterDeliveryRecommendation = await Call(
            client,
            "get_next_task",
            ("project", "starfall"),
            ("milestone", "m1"));
        Assert.DoesNotContain("\"id\":\"STAR-0001\"", Json(afterDeliveryRecommendation));
        Assert.Contains("ROYALE-LOCAL", Json(await Call(client, "get_next_task")));

        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "reopen_milestone",
            ("key", "m1"),
            ("project", "prj_starfall")), "prj_starfall");
        Assert.Contains("STAR-0001", Json(await Call(
            client,
            "get_next_task",
            ("project", "starfall"),
            ("milestone", "m1"))));

        var stalePreview = await Call(
            client,
            "preview_milestone_delivery",
            ("key", "m1"),
            ("reason", reason),
            ("project", "starfall"));
        string staleRevision;
        using (var staleDocument = JsonDocument.Parse(Json(stalePreview)))
            staleRevision = staleDocument.RootElement.GetProperty("data").GetProperty("revision").GetString()!;
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "set_milestone_description",
            ("key", "m1"),
            ("description", "Updated after the delivery preview."),
            ("project", "starfall")), "prj_starfall");
        var beforeStaleApply = File.ReadAllText(fixture.Starfall.ConfigPath);
        var stale = await Call(
            client,
            "deliver_milestone",
            ("key", "m1"),
            ("expectedRevision", staleRevision),
            ("reason", reason),
            ("allowExceptional", true),
            ("project", "starfall"));
        Assert.Contains("milestone_delivery_stale", Json(stale));
        Assert.Equal(beforeStaleApply, File.ReadAllText(fixture.Starfall.ConfigPath));

        fixture.Games.UpdateTaskState(parentOpen, "done");
        var parentPreview = await Call(
            client,
            "preview_milestone_delivery",
            ("key", "m1"),
            ("project", "parent"));
        string parentRevision;
        using (var parentPreviewDocument = JsonDocument.Parse(Json(parentPreview)))
        {
            var data = parentPreviewDocument.RootElement.GetProperty("data");
            Assert.Equal("ordinary", data.GetProperty("mode").GetString());
            parentRevision = data.GetProperty("revision").GetString()!;
        }
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "deliver_milestone",
            ("key", "m1"),
            ("expectedRevision", parentRevision),
            ("project", "parent")), "prj_games");
        await AssertSelectedMutationMatchesReread(client, await Call(
            client,
            "reopen_milestone",
            ("key", "m1"),
            ("project", "parent")), "prj_games");

        fixture.Games.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                new LinkedProjectDeclaration
                {
                    ProjectId = "prj_royale",
                    Alias = "royale",
                    RepositoryUrl = "https://github.com/chronium/pm-link-fixture-royale.git",
                    PathHint = "royale",
                },
                new LinkedProjectDeclaration
                {
                    ProjectId = "prj_starfall",
                    Alias = "shared",
                    RepositoryUrl = "https://github.com/chronium/pm-link-fixture-starfall.git",
                    PathHint = "starfall",
                },
            ],
        });
        fixture.Royale.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = new LinkedProjectDeclaration
            {
                ProjectId = "prj_games",
                Alias = "shared",
                RepositoryUrl = "https://github.com/chronium/pm-link-fixture-games.git",
                PathHint = "..",
            },
        });
        var ambiguous = await Call(
            client,
            "preview_milestone_delivery",
            ("key", "m1"),
            ("project", "shared"));
        Assert.Contains("ambiguous_linked_project", Json(ambiguous));
    }

    [Fact]
    public async Task CliAndStdioMcpApplyActivationEligibilityWithinTheOwningLinkedProject()
    {
        using var fixture = await LinkedProjectIntegrationFixture.CreateAsync();
        fixture.Starfall.Config!.ActivationTriggers["starfall-entry"] = new ActivationTriggerDefinition
        {
            Title = "Starfall entry",
            Requirements =
            [
                new ActivationRequirement
                {
                    Kind = ActivationRequirementKind.Task,
                    Source = "GATE-0001",
                },
            ],
        };
        fixture.Starfall.Config.Milestones["m1"].RequiredActivationTriggers = ["starfall-entry"];
        fixture.Starfall.Config.WriteConfig(fixture.Starfall);
        var gate = TestData.Task(
            "GATE-0001", "Starfall gate", track: "STAR", milestone: null);
        fixture.Starfall.WriteTask(gate);
        fixture.Starfall.UpdateTaskState(gate, "todo");
        var duplicate = TestData.Task(
            "GATE-0001", "Completed duplicate", track: "ROYALE", milestone: null);
        fixture.Royale.WriteTask(duplicate);
        fixture.Royale.UpdateTaskState(duplicate, "done");

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["PM_PROJECT_REGISTRY_PATH"] = fixture.RegistryPath;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "PM linked activation integration",
            Command = "dotnet",
            Arguments = [typeof(PmMcpTools).Assembly.Location, "mcp"],
            WorkingDirectory = fixture.Royale.RepositoryPath,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        AssertSchemaProperty(tools, "get_project", "project");
        AssertSchemaProperty(tools, "list_milestones", "project");
        AssertSchemaProperty(tools, "get_activation_switchboard", "project");

        var parent = await Call(client, "get_project", ("project", "parent"));
        var siblingById = await Call(client, "get_project", ("project", "prj_starfall"));
        var milestones = await Call(client, "list_milestones", ("project", "starfall"));
        var inactiveSwitchboard = await Call(
            client, "get_activation_switchboard", ("project", "starfall"));
        var unavailable = await Call(client, "get_project", ("project", "missing"));

        AssertProject(parent, "prj_games", "parent");
        AssertProject(siblingById, "prj_starfall", "sibling");
        AssertProject(milestones, "prj_starfall", "sibling");
        using (var document = JsonDocument.Parse(Json(milestones)))
            Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("data").ValueKind);
        using (var document = JsonDocument.Parse(Json(inactiveSwitchboard)))
        {
            var root = document.RootElement;
            Assert.Equal("prj_starfall", root.GetProperty("project").GetProperty("projectId").GetString());
            var trigger = Assert.Single(root.GetProperty("data").GetProperty("activationTriggers").EnumerateArray());
            Assert.False(trigger.GetProperty("requirementsSatisfied").GetBoolean());
            Assert.False(trigger.GetProperty("isActive").GetBoolean());
            Assert.Equal("inactive", Assert.Single(root.GetProperty("data").GetProperty("milestones")
                .EnumerateArray()).GetProperty("lifecycle").GetString());
            Assert.Contains(root.GetProperty("warnings").EnumerateArray(), warning =>
                warning.GetProperty("targetProjectId").GetString() == "prj_missing");
        }
        Assert.Contains("linked_project_unavailable", Json(unavailable));

        var inactive = await RunCli(fixture, fixture.Royale.RepositoryPath, "task", "next", "--family");
        Assert.Equal(0, inactive.ExitCode);
        Assert.DoesNotContain("STAR-0001", inactive.Output);

        var activated = TestMilestoneActivationServices.Create(fixture.Starfall)
            .Triggers.ActivateTrigger("starfall-entry", "Proceed with family integration validation.");
        Assert.True(activated.Success, activated.Message);

        var eligible = await RunCli(fixture, fixture.Royale.RepositoryPath, "task", "next", "--family");
        Assert.Equal(0, eligible.ExitCode);
        Assert.Contains("Publish", eligible.Output);
        Assert.Contains("prj_starfall", eligible.Output);
        Assert.Contains("Starfall", eligible.Output);

        var recommendation = await Call(client, "get_next_task", ("family", true));
        var activeSwitchboard = await Call(
            client, "get_activation_switchboard", ("project", "starfall"));

        Assert.NotEqual(true, recommendation.IsError);
        Assert.Contains("STAR-0001", Json(recommendation));
        Assert.Contains("prj_starfall", Json(recommendation));
        Assert.Contains("prj_missing", Json(recommendation));
        Assert.Contains("\"mode\":\"override\"", Json(activeSwitchboard));
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

    private static async Task<JsonElement> AssertSelectedMutationMatchesReread(
        McpClient client,
        CallToolResult mutation,
        string projectId)
    {
        Assert.NotEqual(true, mutation.IsError);
        using var mutationDocument = JsonDocument.Parse(Json(mutation));
        var data = mutationDocument.RootElement.GetProperty("data");
        var receipt = data.GetProperty("mutation");
        Assert.Equal(projectId, receipt.GetProperty("projectId").GetString());
        Assert.All(receipt.GetProperty("changedPaths").EnumerateArray(), path =>
            Assert.StartsWith(".pm/", path.GetString(), StringComparison.Ordinal));

        var reread = await Call(
            client, "get_activation_switchboard", ("project", projectId));
        using var rereadDocument = JsonDocument.Parse(Json(reread));
        Assert.Equal(
            data.GetProperty("switchboard").GetRawText(),
            rereadDocument.RootElement.GetProperty("data").GetRawText());
        return data.Clone();
    }

    private static void AssertProject(CallToolResult result, string projectId, string relationship)
    {
        Assert.NotEqual(true, result.IsError);
        using var document = JsonDocument.Parse(Json(result));
        var project = document.RootElement.GetProperty("project");
        Assert.Equal(projectId, project.GetProperty("projectId").GetString());
        Assert.Equal(relationship, project.GetProperty("relationship").GetString());
        Assert.True(project.TryGetProperty("revision", out _));
        Assert.True(project.TryGetProperty("dirty", out _));
    }

    private static string ReadTaskMarkdown(PM.Project.ProjectRoot root, string taskId) =>
        File.ReadAllText(Path.Combine(root.TasksPath, $"{taskId}.{GlobalConfig.DefaultTaskExtension}"));

    private sealed record ProcessResult(int ExitCode, string Output);
}
