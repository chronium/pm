using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PM.Api;
using PM.Project;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task ActivationOpenApiPublishesSwitchboardAndRevisionGuardedMutations()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
            var paths = document.GetProperty("paths");
            var read = paths.GetProperty("/api/v1/activation").GetProperty("get");
            Assert.True(read.GetProperty("responses").GetProperty("200")
                .GetProperty("headers").TryGetProperty("ETag", out _));

            var apply = paths.GetProperty("/api/v1/activation/milestones/{key}/required-triggers")
                .GetProperty("put");
            var headerNames = apply.GetProperty("parameters").EnumerateArray()
                .Where(parameter => parameter.GetProperty("in").GetString() == "header")
                .Select(parameter => parameter.GetProperty("name").GetString())
                .ToList();
            Assert.Contains(ApiV1Endpoints.ClientHeader, headerNames);
            Assert.Contains("If-Match", headerNames);
            Assert.True(apply.GetProperty("responses").TryGetProperty("412", out _));
            Assert.True(apply.GetProperty("responses").TryGetProperty("428", out _));

            var switchboard = document.GetProperty("components").GetProperty("schemas")
                .GetProperty("ActivationSwitchboardResponse").GetProperty("properties");
            Assert.True(switchboard.TryGetProperty("activationTriggers", out _));
            Assert.True(switchboard.TryGetProperty("milestones", out _));
            Assert.True(switchboard.TryGetProperty("issues", out _));
            Assert.True(switchboard.TryGetProperty("revision", out _));
        }
    }

    [Fact]
    public async Task ActivationReadSeparatesDefinitionsFromResolvedStateAndReconcilesWithRevision()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(milestones: new() { ["beta"] = "Public beta" });
        config.Milestones["beta"].Description = "Deliver an installable public beta.";
        config.Milestones["beta"].RequiredActivationTriggers = ["beta-entry"];
        config.ActivationTriggers["beta-entry"] = new ActivationTriggerDefinition
        {
            Title = "Beta entry",
            Requirements =
            [
                new ActivationRequirement
                {
                    Kind = ActivationRequirementKind.Task,
                    Source = "PM-0001",
                },
            ],
        };
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Foundation");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
            var milestone = Assert.Single(settings.GetProperty("milestones").EnumerateArray());
            Assert.Equal("Deliver an installable public beta.", milestone.GetProperty("description").GetString());
            Assert.Equal("beta-entry", Assert.Single(milestone.GetProperty("requiredActivationTriggers").EnumerateArray()).GetString());
            Assert.Equal("task", settings.GetProperty("activationTriggers")[0]
                .GetProperty("requirements")[0].GetProperty("kind").GetString());

            var pendingResponse = await client.GetAsync("/api/v1/activation");
            var pending = JsonDocument.Parse(await pendingResponse.Content.ReadAsStringAsync()).RootElement;
            var initialRevision = pending.GetProperty("revision").GetString()!;
            Assert.Equal("inactive", pending.GetProperty("milestones")[0].GetProperty("lifecycle").GetString());
            Assert.False(pending.GetProperty("activationTriggers")[0].GetProperty("isActive").GetBoolean());

            root.UpdateTaskState(task, "done");
            var satisfiedResponse = await client.GetAsync("/api/v1/activation");
            var satisfied = JsonDocument.Parse(await satisfiedResponse.Content.ReadAsStringAsync()).RootElement;
            var satisfiedRevision = satisfied.GetProperty("revision").GetString()!;
            Assert.NotEqual(initialRevision, satisfiedRevision);
            Assert.True(satisfied.GetProperty("activationTriggers")[0].GetProperty("requirementsSatisfied").GetBoolean());
            Assert.False(satisfied.GetProperty("activationTriggers")[0].GetProperty("isActive").GetBoolean());

            using var reconcile = new HttpRequestMessage(HttpMethod.Post, "/api/v1/activation/reconcile")
            {
                Content = JsonContent.Create(new { dryRun = false }),
            };
            reconcile.Headers.Add(ApiV1Endpoints.ClientHeader, "activation-api-test");
            reconcile.Headers.TryAddWithoutValidation("If-Match", $"\"{satisfiedRevision}\"");
            var reconciledResponse = await client.SendAsync(reconcile);
            Assert.Equal(HttpStatusCode.OK, reconciledResponse.StatusCode);
            var reconciled = JsonDocument.Parse(await reconciledResponse.Content.ReadAsStringAsync()).RootElement;
            Assert.True(reconciled.GetProperty("changed").GetBoolean());
            var trigger = reconciled.GetProperty("switchboard").GetProperty("activationTriggers")[0];
            Assert.True(trigger.GetProperty("isActive").GetBoolean());
            Assert.Equal("automatic", trigger.GetProperty("activation").GetProperty("mode").GetString());
            Assert.Equal("active", reconciled.GetProperty("switchboard")
                .GetProperty("milestones")[0].GetProperty("lifecycle").GetString());
        }
    }

    [Fact]
    public async Task RequiredTriggerReplacementUsesImpactPreviewConfirmationAndStrongRevision()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new() { ["beta"] = "Public beta" },
            activationTriggers: new()
            {
                ["manual-entry"] = new ActivationTriggerDefinition
                {
                    Title = "Manual entry",
                    Requirements = [],
                },
            });
        var root = await workspace.CreateProject(config);
        var task = TestData.Task("PM-0001", "Beta work", milestone: "beta");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var read = await client.GetAsync("/api/v1/activation");
            var revision = JsonDocument.Parse(await read.Content.ReadAsStringAsync())
                .RootElement.GetProperty("revision").GetString()!;
            using var previewRequest = ActivationRequest(
                HttpMethod.Post,
                "/api/v1/activation/milestones/beta/required-triggers-preview",
                revision,
                new { triggerKeys = new[] { "manual-entry" } });
            var previewResponse = await client.SendAsync(previewRequest);
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync()).RootElement;
            Assert.True(preview.GetProperty("requiresConfirmation").GetBoolean());
            Assert.Equal("PM-0001", Assert.Single(preview.GetProperty("taskIdsLosingEligibility").EnumerateArray()).GetString());
            var previewRevision = preview.GetProperty("previewRevision").GetString()!;

            using var unconfirmed = ActivationRequest(
                HttpMethod.Put,
                "/api/v1/activation/milestones/beta/required-triggers",
                revision,
                new { triggerKeys = new[] { "manual-entry" }, previewRevision, allowDeactivation = false });
            var unconfirmedResponse = await client.SendAsync(unconfirmed);
            Assert.Equal(HttpStatusCode.Conflict, unconfirmedResponse.StatusCode);
            var problem = await unconfirmedResponse.Content.ReadFromJsonAsync<ApiProblemDetails>();
            Assert.Equal("milestone_required_triggers_confirmation_required", problem!.ErrorCode);

            using var confirmed = ActivationRequest(
                HttpMethod.Put,
                "/api/v1/activation/milestones/beta/required-triggers",
                revision,
                new { triggerKeys = new[] { "manual-entry" }, previewRevision, allowDeactivation = true });
            var confirmedResponse = await client.SendAsync(confirmed);
            Assert.Equal(HttpStatusCode.OK, confirmedResponse.StatusCode);
            var confirmedBody = JsonDocument.Parse(await confirmedResponse.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("inactive", confirmedBody.GetProperty("switchboard")
                .GetProperty("milestones")[0].GetProperty("lifecycle").GetString());
            Assert.NotEqual($"\"{revision}\"", confirmedResponse.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task ActivationTransitionEndpointsPreserveProvenanceGuardsAndAuthoritativeRereads()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new() { ["public-beta"] = "Public beta" },
            activationTriggers: new()
            {
                ["manual-entry"] = new ActivationTriggerDefinition
                {
                    Title = "Manual entry",
                    Requirements = [],
                },
                ["beta-entry"] = new ActivationTriggerDefinition
                {
                    Title = "Beta entry",
                    Requirements =
                    [
                        new ActivationRequirement
                        {
                            Kind = ActivationRequirementKind.Task,
                            Source = "PM-0001",
                        },
                    ],
                },
            });
        config.Milestones["public-beta"].RequiredActivationTriggers = ["beta-entry"];
        var root = await workspace.CreateProject(config);
        var prerequisite = TestData.Task("PM-0001", "Foundation capability");
        root.WriteTask(prerequisite);
        root.UpdateTaskState(prerequisite, "todo");
        var beta = TestData.Task("PM-0002", "Beta validation", milestone: "public-beta");
        root.WriteTask(beta);
        root.UpdateTaskState(beta, "todo");

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var manual = await SendActivationMutation(
                client,
                HttpMethod.Post,
                "/api/v1/activation/triggers/manual-entry/activate",
                new { });
            Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
            Assert.Equal("manual", Trigger(manual.Body, "manual-entry")
                .GetProperty("activation").GetProperty("mode").GetString());
            await AssertApiMutationMatchesReread(client, manual.Body);

            var manualReset = await SendActivationMutation(
                client,
                HttpMethod.Delete,
                "/api/v1/activation/triggers/manual-entry/activation");
            Assert.Equal(HttpStatusCode.OK, manualReset.StatusCode);
            Assert.Equal(JsonValueKind.Null, Trigger(manualReset.Body, "manual-entry")
                .GetProperty("activation").ValueKind);

            var overridden = await SendActivationMutation(
                client,
                HttpMethod.Post,
                "/api/v1/activation/triggers/beta-entry/override",
                new { reason = "Proceed with the reviewed beta risk." });
            Assert.Equal(HttpStatusCode.OK, overridden.StatusCode);
            var overrideTrigger = Trigger(overridden.Body, "beta-entry");
            Assert.Equal("override", overrideTrigger.GetProperty("activation").GetProperty("mode").GetString());
            Assert.Equal("PM-0001", Assert.Single(overrideTrigger.GetProperty("activation")
                .GetProperty("waivedRequirements").EnumerateArray()).GetProperty("source").GetString());
            Assert.Equal("active", Milestone(overridden.Body, "public-beta")
                .GetProperty("lifecycle").GetString());
            await AssertApiMutationMatchesReread(client, overridden.Body);

            var overrideReset = await SendActivationMutation(
                client,
                HttpMethod.Delete,
                "/api/v1/activation/triggers/beta-entry/activation");
            Assert.Equal(HttpStatusCode.OK, overrideReset.StatusCode);
            Assert.Equal("inactive", Milestone(overrideReset.Body, "public-beta")
                .GetProperty("lifecycle").GetString());

            root.UpdateTaskState(prerequisite, "done");
            var reconciled = await SendActivationMutation(
                client,
                HttpMethod.Post,
                "/api/v1/activation/reconcile",
                new { dryRun = false });
            Assert.Equal(HttpStatusCode.OK, reconciled.StatusCode);
            Assert.Equal("automatic", Trigger(reconciled.Body, "beta-entry")
                .GetProperty("activation").GetProperty("mode").GetString());

            var blocked = await SendActivationMutation(
                client,
                HttpMethod.Delete,
                "/api/v1/activation/triggers/beta-entry/activation");
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("activation_trigger_reset_blocked",
                blocked.Body.GetProperty("errorCode").GetString());
        }
    }

    [Fact]
    public async Task RedefinitionAndExceptionalDeliveryEndpointsUsePreviewRevisions()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            milestones: new() { ["public-beta"] = "Public beta" },
            activationTriggers: new()
            {
                ["beta-entry"] = new ActivationTriggerDefinition
                {
                    Title = "Beta entry",
                    Requirements = [],
                    Activation = new ActivationRecord
                    {
                        At = DateTimeOffset.Parse("2026-08-07T08:00:00Z"),
                        Mode = ActivationMode.Manual,
                    },
                },
            });
        config.Milestones["public-beta"].RequiredActivationTriggers = ["beta-entry"];
        var root = await workspace.CreateProject(config);
        var replacement = TestData.Task("PM-0001", "Replacement requirement");
        root.WriteTask(replacement);
        root.UpdateTaskState(replacement, "todo");
        var beta = TestData.Task("PM-0002", "Beta validation", milestone: "public-beta");
        root.WriteTask(beta);
        root.UpdateTaskState(beta, "todo");

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var requirements = new[] { new { kind = "task", source = "PM-0001" } };
            var preview = await SendActivationMutation(
                client,
                HttpMethod.Post,
                "/api/v1/activation/triggers/beta-entry/redefinition-preview",
                new { requirements });
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.True(preview.Body.GetProperty("requiresConfirmation").GetBoolean());
            var previewRevision = preview.Body.GetProperty("previewRevision").GetString()!;

            var redefine = await SendActivationMutation(
                client,
                HttpMethod.Put,
                "/api/v1/activation/triggers/beta-entry/redefinition",
                new { requirements, previewRevision, allowDeactivation = true });
            Assert.Equal(HttpStatusCode.OK, redefine.StatusCode);
            Assert.Equal("inactive", Milestone(redefine.Body, "public-beta")
                .GetProperty("lifecycle").GetString());

            var overridden = await SendActivationMutation(
                client,
                HttpMethod.Post,
                "/api/v1/activation/triggers/beta-entry/override",
                new { reason = "Accept the reviewed beta entry risk." });
            Assert.Equal(HttpStatusCode.OK, overridden.StatusCode);

            var deliveryPreview = await SendActivationMutation(
                client,
                HttpMethod.Post,
                "/api/v1/activation/milestones/public-beta/delivery-preview",
                new { reason = "Ship the remaining validation as dogfood follow-up." });
            Assert.Equal(HttpStatusCode.OK, deliveryPreview.StatusCode);
            Assert.True(deliveryPreview.Body.GetProperty("requiresConfirmation").GetBoolean());
            Assert.Equal("PM-0002", Assert.Single(deliveryPreview.Body
                .GetProperty("unfinishedTaskIds").EnumerateArray()).GetString());
            var deliveryRevision = deliveryPreview.Body.GetProperty("previewRevision").GetString()!;

            var delivered = await SendActivationMutation(
                client,
                HttpMethod.Put,
                "/api/v1/activation/milestones/public-beta/delivery",
                new
                {
                    reason = "Ship the remaining validation as dogfood follow-up.",
                    previewRevision = deliveryRevision,
                    allowExceptional = true,
                });
            Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
            var delivery = Milestone(delivered.Body, "public-beta").GetProperty("delivery");
            Assert.Equal("exceptional", delivery.GetProperty("mode").GetString());
            Assert.Equal("PM-0002", Assert.Single(delivery.GetProperty("acceptedTaskIds")
                .EnumerateArray()).GetString());
            await AssertApiMutationMatchesReread(client, delivered.Body);

            var reopened = await SendActivationMutation(
                client,
                HttpMethod.Delete,
                "/api/v1/activation/milestones/public-beta/delivery");
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
            Assert.Equal(JsonValueKind.Null, Milestone(reopened.Body, "public-beta")
                .GetProperty("delivery").ValueKind);
            Assert.Equal("active", Milestone(reopened.Body, "public-beta")
                .GetProperty("lifecycle").GetString());
        }
    }

    private static HttpRequestMessage ActivationRequest(
        HttpMethod method,
        string path,
        string revision,
        object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "activation-api-test");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{revision}\"");
        return request;
    }

    private static async Task<ActivationHttpResult> SendActivationMutation(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null)
    {
        var read = await client.GetFromJsonAsync<JsonElement>("/api/v1/activation");
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body);
        request.Headers.Add(ApiV1Endpoints.ClientHeader, "activation-api-test");
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            $"\"{read.GetProperty("revision").GetString()}\"");
        using var response = await client.SendAsync(request);
        var responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.Clone();
        return new ActivationHttpResult(response.StatusCode, responseBody);
    }

    private static async Task AssertApiMutationMatchesReread(HttpClient client, JsonElement mutation)
    {
        var reread = await client.GetFromJsonAsync<JsonElement>("/api/v1/activation");
        Assert.Equal(
            reread.GetRawText(),
            mutation.GetProperty("switchboard").GetRawText());
    }

    private static JsonElement Trigger(JsonElement mutation, string key) =>
        Assert.Single(mutation.GetProperty("switchboard").GetProperty("activationTriggers")
            .EnumerateArray(), trigger => trigger.GetProperty("key").GetString() == key);

    private static JsonElement Milestone(JsonElement mutation, string key) =>
        Assert.Single(mutation.GetProperty("switchboard").GetProperty("milestones")
            .EnumerateArray(), milestone => milestone.GetProperty("key").GetString() == key);

    private sealed record ActivationHttpResult(HttpStatusCode StatusCode, JsonElement Body);
}
