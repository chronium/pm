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
}
