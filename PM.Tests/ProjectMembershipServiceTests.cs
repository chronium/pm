using System.Net;
using System.Text;
using PM.Application;
using PM.Auth;
using PM.Project;
using PM.Worker;

namespace PM.Tests;

public sealed class ProjectMembershipServiceTests
{
    [Fact]
    public void LocalIdentityContainsOnlyShareableFieldsAndStableFingerprint()
    {
        using var workspace = new TempWorkingDirectory();
        var identityService = new IdentityService(new IdentityServiceOptions
            { IdentityPath = Path.Combine(workspace.Path, "identity.json") });
        var service = new ProjectMembershipService(new ProjectRoot(), identityService,
            new PmWorkerClient(new HttpClient(new RecordingHandler(_ => Ok("{}")))));

        var result = service.GetLocalIdentity();

        Assert.True(result.Success);
        Assert.StartsWith("usr_", result.Payload!.UserId);
        Assert.Equal(64, result.Payload.Fingerprint.Length);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(result.Payload),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProjectMembershipService.Fingerprint(result.Payload.PublicKey), result.Payload.Fingerprint);
    }

    [Fact]
    public async Task ListMembersSignsRequestAndIdentifiesLocalMember()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await Project(workspace);
        var identityService = Identity(workspace);
        var identity = identityService.GetOrCreateIdentity();
        var handler = new RecordingHandler(_ => Ok($$"""
        {"currentUserId":"{{identity.UserId}}","currentRole":"admin","members":[
          {"userId":"{{identity.UserId}}","displayName":"Local","publicKey":"{{identity.PublicKey}}","role":"admin"}
        ]}
        """));
        var service = new ProjectMembershipService(projectRoot, identityService,
            new PmWorkerClient(new HttpClient(handler)));

        var result = await service.ListMembers();

        Assert.True(result.Success);
        Assert.True(Assert.Single(result.Payload!.Members).IsLocal);
        Assert.Equal("project-id", result.Payload.ProjectId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ids.example.test/projects/project-id/members", request.Uri.ToString());
        Assert.Contains("PM-Signature", request.Headers.Keys);
        Assert.Contains("PM-Public-Key", request.Headers.Keys);
    }

    [Fact]
    public async Task CreateInvitationReturnsOneTimeSecretAndSendsRole()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await Project(workspace);
        var handler = new RecordingHandler(_ => Ok("""
        {"invitation":{"invitationId":"pminv_1","role":"user","createdByUserId":"usr_1",
        "createdAt":"2026-07-27T00:00:00Z","expiresAt":"2026-07-28T00:00:00Z"},"token":"pmi_secret"}
        """));
        var service = new ProjectMembershipService(projectRoot, Identity(workspace),
            new PmWorkerClient(new HttpClient(handler)));

        var result = await service.CreateInvitation("USER");

        Assert.True(result.Success);
        Assert.Equal("pmi_secret", result.Payload!.Token);
        Assert.Equal("user", result.Payload.Invitation.Role);
        Assert.Equal("{\"role\":\"user\"}", handler.Requests.Single().Body);
    }

    [Fact]
    public async Task StructuredWorkerFailuresAreMappedWithoutLeakingResponseContent()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await Project(workspace);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"errorCode\":\"final_admin\",\"message\":\"The final admin cannot be removed.\"}",
                Encoding.UTF8, "application/json"),
        });
        var service = new ProjectMembershipService(projectRoot, Identity(workspace),
            new PmWorkerClient(new HttpClient(handler)));

        var result = await service.RemoveMember("usr_admin");

        Assert.False(result.Success);
        Assert.Equal("final_admin", result.ErrorCode);
        Assert.Equal("The final admin cannot be removed.", result.Message);
    }

    [Fact]
    public async Task MissingProjectIdFailsBeforeExternalAccess()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not send"));
        var service = new ProjectMembershipService(projectRoot, Identity(workspace),
            new PmWorkerClient(new HttpClient(handler)));

        var result = await service.ListMembers();

        Assert.False(result.Success);
        Assert.Equal("missing_project_id", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    private static async Task<ProjectRoot> Project(TempWorkingDirectory workspace)
    {
        var projectRoot = await workspace.CreateProject(TestData.Config(nextIdServiceUrl: "http://ids.example.test"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile), "project-id\n");
        return projectRoot;
    }

    private static IdentityService Identity(TempWorkingDirectory workspace) =>
        new(new IdentityServiceOptions { IdentityPath = Path.Combine(workspace.Path, "identity.json") });

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.RequestUri!,
                request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray()),
                request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return response(request);
        }
    }

    private sealed record RecordedRequest(Uri Uri, Dictionary<string, string[]> Headers, string Body);
}
