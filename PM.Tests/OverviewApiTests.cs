using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PM.Api;
using PM.Application;
using PM.Project;

namespace PM.Tests;

public partial class ApiContractTests
{
    [Fact]
    public async Task OverviewApiReturnsDiscriminatedReadyDocumentAndRevisionedTaskChanges()
    {
        using var workspace = new TempWorkingDirectory();
        var config = TestData.Config(
            name: "Northstar",
            milestones: new Dictionary<string, string> { ["beta"] = "Public beta" },
            milestonePriorities: new Dictionary<string, string> { ["beta"] = PriorityLevel.High });
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Title = "Northstar Overview",
            Description = "A project introduction.",
            Home = new OverviewHomeDefinition
            {
                Layout = OverviewLayouts.Split,
                Primary =
                [
                    OverviewSection(OverviewSectionKinds.Hero),
                    OverviewSection(OverviewSectionKinds.Markdown, source: "wiki:introduction"),
                ],
                Secondary =
                [
                    OverviewSection(OverviewSectionKinds.Milestone, milestone: "beta"),
                    OverviewSection(OverviewSectionKinds.Tasks, limit: 3),
                ],
                After =
                [
                    OverviewSection(OverviewSectionKinds.Wiki, pages: ["guide"]),
                    OverviewSection(OverviewSectionKinds.Copyright, notice: "Copyright 2026 Northstar."),
                ],
            },
        };
        var root = await workspace.CreateProject(config);
        Assert.True(new WikiService(root).CreatePage(
            "introduction", "Introduction", "Welcome to Northstar.").Success);
        Assert.True(new WikiService(root).CreatePage("guide", "Guide", "Read the guide.").Success);
        var task = TestData.Task("PM-0001", "Ship the beta", "Task body", milestone: "beta");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");

        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/overview");
            var json = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonDocument.Parse(json).RootElement;
            Assert.Equal("ready", body.GetProperty("status").GetString());
            Assert.Equal("Northstar", body.GetProperty("projectName").GetString());
            Assert.Equal("Northstar Overview", body.GetProperty("documentTitle").GetString());
            var composition = body.GetProperty("composition");
            Assert.Equal("split", composition.GetProperty("layout").GetString());
            Assert.Equal(["hero", "markdown"], SectionTypes(composition.GetProperty("primary")));
            Assert.Equal(["milestone", "tasks"], SectionTypes(composition.GetProperty("secondary")));
            Assert.Equal(["wiki", "copyright"], SectionTypes(composition.GetProperty("after")));
            var milestone = composition.GetProperty("secondary")[0].GetProperty("milestone");
            Assert.Equal("active", milestone.GetProperty("lifecycle").GetString());
            var returnedTask = composition.GetProperty("secondary")[1].GetProperty("tasks")[0];
            Assert.Equal("PM-0001", returnedTask.GetProperty("id").GetString());
            Assert.True(returnedTask.GetProperty("dependencies").GetProperty("ready").GetBoolean());
            Assert.True(returnedTask.GetProperty("activation").GetProperty("isEligible").GetBoolean());
            Assert.Equal("introduction",
                composition.GetProperty("primary")[1].GetProperty("sourcePath").GetString());
            Assert.Equal(ApiPreconditions.FormatETag(body.GetProperty("revision").GetString()!),
                response.Headers.ETag?.Tag);
            Assert.DoesNotContain(root.RepositoryPath, json, StringComparison.Ordinal);
            Assert.DoesNotContain("filePath", json, StringComparison.Ordinal);
            Assert.DoesNotContain("localMetadata", json, StringComparison.Ordinal);
            Assert.DoesNotContain("relationship", json, StringComparison.Ordinal);
            Assert.DoesNotContain("alias", json, StringComparison.Ordinal);

            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/overview");
            conditional.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            var notModified = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Equal(string.Empty, await notModified.Content.ReadAsStringAsync());
            Assert.Equal(response.Headers.ETag?.Tag, notModified.Headers.ETag?.Tag);

            root.WriteTask(TestData.Task(
                "PM-0001", "Ship the revised beta", "Task body", milestone: "beta"));
            using var stale = new HttpRequestMessage(HttpMethod.Get, "/api/v1/overview");
            stale.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            var changed = await client.SendAsync(stale);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.NotEqual(response.Headers.ETag?.Tag, changed.Headers.ETag?.Tag);
        }
    }

    [Fact]
    public async Task OverviewApiReturnsDisabledAndSemanticInvalidDocumentsButRejectsMalformedProjectYaml()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject(TestData.Config(name: "Reloaded project"));
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var disabledResponse = await client.GetAsync("/api/v1/overview");
            var disabled = JsonDocument.Parse(await disabledResponse.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(HttpStatusCode.OK, disabledResponse.StatusCode);
            Assert.Equal("disabled", disabled.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, disabled.GetProperty("composition").ValueKind);
            Assert.Empty(disabled.GetProperty("issues").EnumerateArray());

            var invalidConfig = TestData.Config(name: "Reloaded project");
            invalidConfig.Site = new OverviewSiteDefinition
            {
                Enabled = true,
                Title = " ",
                Home = new OverviewHomeDefinition
                {
                    Sections = [OverviewSection(OverviewSectionKinds.Tasks)],
                },
            };
            File.WriteAllText(root.ConfigPath, global::PM.YamlSerde.Serialize(invalidConfig));

            var invalidResponse = await client.GetAsync("/api/v1/overview");
            var invalid = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
            Assert.Equal("invalid", invalid.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, invalid.GetProperty("composition").ValueKind);
            Assert.Contains(invalid.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == "missing_overview_hero" &&
                issue.GetProperty("path").GetString() == "site.home.sections[0]");
            Assert.NotEqual(disabledResponse.Headers.ETag?.Tag, invalidResponse.Headers.ETag?.Tag);

            File.WriteAllText(root.ConfigPath, "name: [unterminated");
            var malformedResponse = await client.GetAsync("/api/v1/overview");
            var problem = await malformedResponse.Content.ReadFromJsonAsync<ApiProblemDetails>();
            Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
            Assert.Equal("invalid_project", problem!.ErrorCode);
        }
    }

    [Fact]
    public async Task LinkedOverviewUsesExactReadableProjectAndReloadsSelectedConfiguration()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await workspace.CreateProject(TestData.Config(name: "Games"));
        await WriteProjectId(active, "prj_games");
        var child = await CreateLinkedProject(
            Path.Combine(workspace.Path, "royale"), "prj_royale", "Royale");
        child.Config!.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Title = "Royale Home",
            Description = "Selected child project.",
        };
        File.WriteAllText(child.ConfigPath, global::PM.YamlSerde.Serialize(child.Config));
        Assert.True(child.TryReloadConfig());
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children =
            [
                Declaration("prj_royale", "royale", "royale"),
                Declaration("prj_missing", "missing", "missing"),
            ],
        });
        child.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Parent = Declaration("prj_games", "games", ".."),
        });

        var (app, client) = await CreateApiClient(
            active, linkedProjectFamilyService: LinkedFamily(active, workspace));
        await using (app)
        using (client)
        {
            var response = await client.GetAsync("/api/v1/projects/prj_royale/overview");
            var json = await response.Content.ReadAsStringAsync();
            var body = JsonDocument.Parse(json).RootElement;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("prj_royale", body.GetProperty("projectId").GetString());
            Assert.Equal("Royale", body.GetProperty("projectName").GetString());
            Assert.Equal("Royale Home", body.GetProperty("documentTitle").GetString());
            Assert.DoesNotContain(active.RepositoryPath, json, StringComparison.Ordinal);
            Assert.DoesNotContain(child.RepositoryPath, json, StringComparison.Ordinal);
            Assert.DoesNotContain("alias", json, StringComparison.Ordinal);
            Assert.DoesNotContain("relationship", json, StringComparison.Ordinal);

            child.Config!.Site!.Title = "Reloaded Royale Home";
            File.WriteAllText(child.ConfigPath, global::PM.YamlSerde.Serialize(child.Config));
            using var stale = new HttpRequestMessage(HttpMethod.Get,
                "/api/v1/projects/prj_royale/overview");
            stale.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag?.Tag);
            var reloaded = await client.SendAsync(stale);
            var reloadedBody = JsonDocument.Parse(await reloaded.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
            Assert.Equal("Reloaded Royale Home", reloadedBody.GetProperty("documentTitle").GetString());
            Assert.NotEqual(response.Headers.ETag?.Tag, reloaded.Headers.ETag?.Tag);

            var alias = await client.GetAsync("/api/v1/projects/royale/overview");
            Assert.Equal(HttpStatusCode.NotFound, alias.StatusCode);
            Assert.Equal("unknown_linked_project",
                (await alias.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);

            var unavailable = await client.GetAsync("/api/v1/projects/prj_missing/overview");
            Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
            Assert.Equal("linked_project_unavailable",
                (await unavailable.Content.ReadFromJsonAsync<ApiProblemDetails>())!.ErrorCode);
        }
    }

    [Fact]
    public async Task OpenApiPublishesRevisionedOverviewContractsForCurrentAndLinkedProjects()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var (app, client) = await CreateApiClient(root);
        await using (app)
        using (client)
        {
            var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
            var paths = document.GetProperty("paths");
            var current = paths.GetProperty("/api/v1/overview").GetProperty("get");
            var linked = paths.GetProperty("/api/v1/projects/{projectId}/overview").GetProperty("get");
            Assert.Equal("GetOverview", current.GetProperty("operationId").GetString());
            Assert.Equal("GetLinkedProjectOverview", linked.GetProperty("operationId").GetString());
            Assert.Contains(current.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "If-None-Match");
            Assert.True(current.GetProperty("responses").GetProperty("200")
                .GetProperty("headers").TryGetProperty("ETag", out _));
            Assert.True(current.GetProperty("responses").TryGetProperty("304", out _));
            Assert.True(linked.GetProperty("responses").TryGetProperty("409", out _));
            Assert.Contains(linked.GetProperty("parameters").EnumerateArray(), parameter =>
                parameter.GetProperty("name").GetString() == "projectId" &&
                parameter.GetProperty("in").GetString() == "path" &&
                parameter.GetProperty("required").GetBoolean());

            var schemas = document.GetProperty("components").GetProperty("schemas");
            var composition = schemas.GetProperty("OverviewCompositionResponse");
            Assert.Equal("layout", composition.GetProperty("discriminator").GetProperty("propertyName").GetString());
            Assert.Equal(2, composition.GetProperty("anyOf").GetArrayLength());
            var section = schemas.GetProperty("OverviewSectionResponse");
            Assert.Equal("type", section.GetProperty("discriminator").GetProperty("propertyName").GetString());
            Assert.Equal(6, section.GetProperty("anyOf").GetArrayLength());
            Assert.Contains("layout", schemas
                .GetProperty("OverviewCompositionResponseSingleOverviewCompositionResponse")
                .GetProperty("required").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("type", schemas
                .GetProperty("OverviewSectionResponseHeroOverviewSectionResponse")
                .GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        }
    }

    private static OverviewSectionDefinition OverviewSection(
        string type,
        string? source = null,
        string? milestone = null,
        int? limit = null,
        IReadOnlyList<string>? pages = null,
        string? notice = null) => new()
        {
            Type = type,
            Source = source,
            Milestone = milestone,
            Limit = limit,
            Pages = pages?.ToList(),
            Notice = notice,
        };

    private static string[] SectionTypes(JsonElement sections) =>
        sections.EnumerateArray().Select(section => section.GetProperty("type").GetString()!).ToArray();
}
