using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PM.AgentRuns;
using PM.Auth;

namespace PM.Tests;

public class AgentRunnerClientTests
{
    [Fact]
    public void RunnerSigningCoversExactBodyPathAndQueryBytes()
    {
        var credential = AgentRunnerCredential.Generate("Test client");
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_785_146_400);
        const string nonce = "nonce_1234567890123456";

        var compact = AgentRunnerRequestSigning.Sign(credential, HttpMethod.Post,
            "/v1/runs?cursor=a%2Fb", "{\"value\":1}"u8, AgentRunProtocol.Current, timestamp, nonce);
        var whitespace = AgentRunnerRequestSigning.Sign(credential, HttpMethod.Post,
            "/v1/runs?cursor=a%2Fb", "{ \"value\": 1 }"u8, AgentRunProtocol.Current, timestamp, nonce);
        var query = AgentRunnerRequestSigning.Sign(credential, HttpMethod.Post,
            "/v1/runs?cursor=a%2Fc", "{\"value\":1}"u8, AgentRunProtocol.Current, timestamp, nonce);
        var lineEnding = AgentRunnerRequestSigning.Sign(credential, HttpMethod.Post,
            "/v1/runs?cursor=a%2Fb", "line one\r\nline two"u8, AgentRunProtocol.Current, timestamp, nonce);
        var normalized = AgentRunnerRequestSigning.Sign(credential, HttpMethod.Post,
            "/v1/runs?cursor=a%2Fb", Encoding.UTF8.GetBytes("line one\nline two"),
            AgentRunProtocol.Current, timestamp, nonce);

        Assert.True(VerifySignature(credential.PublicKey, compact.Signature, Canonical(
            "/v1/runs?cursor=a%2Fb", "{\"value\":1}"u8, timestamp, nonce, credential.ClientId)));
        Assert.False(VerifySignature(credential.PublicKey, compact.Signature, Canonical(
            "/v1/runs?cursor=a%2Fb", "{ \"value\": 1 }"u8, timestamp, nonce, credential.ClientId)));
        Assert.False(VerifySignature(credential.PublicKey, compact.Signature, Canonical(
            "/v1/runs?cursor=a%2Fc", "{\"value\":1}"u8, timestamp, nonce, credential.ClientId)));
        Assert.False(VerifySignature(credential.PublicKey, lineEnding.Signature, Canonical(
            "/v1/runs?cursor=a%2Fb", Encoding.UTF8.GetBytes("line one\nline two"),
            timestamp, nonce, credential.ClientId)));
        Assert.True(VerifySignature(credential.PublicKey, whitespace.Signature, Canonical(
            "/v1/runs?cursor=a%2Fb", "{ \"value\": 1 }"u8, timestamp, nonce, credential.ClientId)));
        Assert.True(VerifySignature(credential.PublicKey, query.Signature, Canonical(
            "/v1/runs?cursor=a%2Fc", "{\"value\":1}"u8, timestamp, nonce, credential.ClientId)));
        Assert.True(VerifySignature(credential.PublicKey, normalized.Signature, Canonical(
            "/v1/runs?cursor=a%2Fb", Encoding.UTF8.GetBytes("line one\nline two"),
            timestamp, nonce, credential.ClientId)));
    }

    [Fact]
    public async Task PairingPersistsPrivateMultiRunnerRegistrationsAndRejectsWrongPins()
    {
        await using var first = await FakeRunnerServer.Start("runner-first");
        await using var second = await FakeRunnerServer.Start("runner-second");
        using var workspace = new TempWorkingDirectory();
        var runnerRoot = Path.Combine(workspace.Path, "runners");
        var identityPath = Path.Combine(workspace.Path, "identity.json");
        var client = Client(runnerRoot, identityPath);

        var wrongPin = await client.Pair(new AgentRunnerPairingRequest(first.Endpoint,
            first.RunnerId, $"sha256:{new string('0', 64)}", first.PairingCode));
        Assert.False(wrongPin.Success);
        Assert.Equal("runner_tls_mismatch", wrongPin.ErrorCode);
        Assert.Empty(client.Registrations().Payload!);

        var pairedFirst = await Pair(client, first);
        var pairedSecond = await Pair(client, second);
        Assert.Equal(first.RunnerId, pairedFirst.RunnerId);
        Assert.Equal(second.RunnerId, pairedSecond.RunnerId);
        Assert.Equal(2, client.Registrations().Payload!.Count);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(pairedFirst),
            StringComparison.OrdinalIgnoreCase);

        var files = Directory.GetFiles(runnerRoot, "*.json");
        Assert.Equal(2, files.Length);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(runnerRoot));
            foreach (var file in files)
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(file));
        }

        var restarted = Client(runnerRoot, identityPath);
        Assert.Equal(2, restarted.Registrations().Payload!.Count);
        Assert.Equal(pairedFirst.ClientId, restarted.Registration(first.RunnerId).Payload!.ClientId);
    }

    [Fact]
    public async Task RegistrationStoreRejectsPermissiveFilesAndSymlinkRoots()
    {
        if (OperatingSystem.IsWindows()) return;
        await using var server = await FakeRunnerServer.Start("runner-private");
        using var workspace = new TempWorkingDirectory();
        var root = Path.Combine(workspace.Path, "runners");
        var client = Client(root, Path.Combine(workspace.Path, "identity.json"));
        await Pair(client, server);
        var file = Directory.GetFiles(root, "*.json").Single();
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                  UnixFileMode.GroupRead);
        var insecure = client.Registration(server.RunnerId);
        Assert.False(insecure.Success);
        Assert.Equal("insecure_runner_storage", insecure.ErrorCode);

        var target = Path.Combine(workspace.Path, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(workspace.Path, "linked-runners");
        Directory.CreateSymbolicLink(link, target);
        var linked = new AgentRunnerRegistrationStore(new AgentRunnerRegistrationStoreOptions
            { RootPath = link }).List();
        Assert.False(linked.Success);
        Assert.Equal("insecure_runner_storage", linked.ErrorCode);
    }

    [Fact]
    public async Task RegistrationStoreRejectsMalformedCredentialWithoutThrowing()
    {
        await using var server = await FakeRunnerServer.Start("runner-malformed");
        using var workspace = new TempWorkingDirectory();
        var root = Path.Combine(workspace.Path, "runners");
        var client = Client(root, Path.Combine(workspace.Path, "identity.json"));
        await Pair(client, server);
        var file = Directory.GetFiles(root, "*.json").Single();
        var document = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
        document["credential"]!["privateKey"] = null;
        await File.WriteAllTextAsync(file, document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var malformed = client.Registration(server.RunnerId);

        Assert.False(malformed.Success);
        Assert.Equal("invalid_runner_registration", malformed.ErrorCode);
    }

    [Fact]
    public async Task SignedClientCoversRunCommandsReplayStreamingRotationAndRevocation()
    {
        await using var server = await FakeRunnerServer.Start("runner-flow");
        using var workspace = new TempWorkingDirectory();
        var client = Client(Path.Combine(workspace.Path, "runners"),
            Path.Combine(workspace.Path, "identity.json"));
        var registration = await Pair(client, server);

        var health = await client.Health(server.RunnerId);
        var capabilities = await client.Capabilities(server.RunnerId);
        Assert.True(health.Success);
        Assert.Equal("online", health.Payload!.Status);
        Assert.Equal("0.1.0", health.Payload.Build!.Version);
        Assert.Equal(new string('a', 40), health.Payload.Build.SourceRevision);
        Assert.True(capabilities.Success);
        Assert.Equal(server.RunnerId, capabilities.Payload!.RunnerId);
        Assert.Equal(AgentRunProtocol.Current, client.Registration(server.RunnerId).Payload!.ProtocolVersion);

        var request = Request(server.RunnerId, "run-client-flow");
        var started = await client.Start(server.RunnerId, request);
        var duplicate = await client.Start(server.RunnerId, request);
        Assert.Equal(AgentRunRemoteStartDisposition.New, started.Payload!.Disposition);
        Assert.Equal(AgentRunRemoteStartDisposition.Existing, duplicate.Payload!.Disposition);

        var changedSpecification = request.Specification with
        {
            Task = request.Specification.Task with { Title = "Conflicting title" },
        };
        var conflict = await client.Start(server.RunnerId,
            new AgentRunRequest(AgentRunCanonicalJson.ComputeSpecificationHash(changedSpecification),
                changedSpecification));
        Assert.False(conflict.Success);
        Assert.Equal("run_id_conflict", conflict.ErrorCode);

        var inspected = await client.Inspect(server.RunnerId, request.Specification.RunId);
        var active = await client.ActiveRuns(server.RunnerId, 1);
        Assert.True(inspected.Success);
        Assert.Single(active.Payload!.Runs);

        var page = await client.Events(server.RunnerId, request.Specification.RunId, 0, 2);
        Assert.True(page.Success, page.Message);
        Assert.Equal([1L, 2L], page.Payload!.Events.Select(item => item.Sequence));
        var next = await client.Events(server.RunnerId, request.Specification.RunId,
            page.Payload.NextAfterSequence, 2);
        Assert.True(next.Success, next.Message);
        Assert.Equal(3, next.Payload!.Events.Single().Sequence);

        var firstStream = await client.OpenEventStream(server.RunnerId, request.Specification.RunId, 0);
        Assert.True(firstStream.Success);
        await using (var stream = firstStream.Payload!)
        {
            var messages = new List<AgentRunStreamMessage>();
            var error = await Assert.ThrowsAsync<AgentRunnerStreamException>(async () =>
            {
                await foreach (var message in stream.ReadAllAsync()) messages.Add(message);
            });
            Assert.Equal("runner_stream_disconnected", error.ErrorCode);
            Assert.Equal(1, messages.Single().Event!.Sequence);
        }

        var resumed = await client.OpenEventStream(server.RunnerId, request.Specification.RunId, 1);
        Assert.True(resumed.Success);
        await using (var stream = resumed.Payload!)
        {
            var messages = new List<AgentRunStreamMessage>();
            await foreach (var message in stream.ReadAllAsync()) messages.Add(message);
            Assert.Equal([2L, 3L], messages.Where(item => item.Event != null)
                .Select(item => item.Event!.Sequence));
            Assert.Equal(3, messages.Single(item => item.End != null).End!.LastSequence);
        }

        var artifactList = await client.Artifacts(server.RunnerId, request.Specification.RunId);
        var artifact = await client.Artifact(server.RunnerId, request.Specification.RunId,
            artifactList.Payload!.Single().ArtifactId);
        Assert.True(artifact.Success);
        var artifactContent = await client.ArtifactContent(server.RunnerId, request.Specification.RunId,
            artifact.Payload!.ArtifactId);
        Assert.True(artifactContent.Success, artifactContent.Message);
        await using (var content = artifactContent.Payload!)
        {
            using var memory = new MemoryStream();
            await content.Content.CopyToAsync(memory);
            Assert.Equal(Encoding.UTF8.GetBytes(new string('a', 42)), memory.ToArray());
        }

        var beforeRotation = registration.ClientId;
        var rotated = await client.Rotate(server.RunnerId);
        Assert.True(rotated.Success);
        Assert.NotEqual(beforeRotation, rotated.Payload!.ClientId);
        Assert.True((await client.Health(server.RunnerId)).Success);

        var cancelled = await client.Cancel(server.RunnerId, request.Specification.RunId);
        Assert.True(cancelled.Success);
        Assert.Equal(AgentRunState.Cancelled, cancelled.Payload!.Run.State);

        var revoked = await client.Revoke(server.RunnerId);
        Assert.True(revoked.Success);
        Assert.False(client.Registration(server.RunnerId).Success);
        Assert.False((await client.Health(server.RunnerId)).Success);
    }

    [Fact]
    public async Task ClientSurfacesClockSkewAndRejectsInvalidSemanticResponses()
    {
        await using var server = await FakeRunnerServer.Start("runner-errors");
        using var workspace = new TempWorkingDirectory();
        var client = Client(Path.Combine(workspace.Path, "runners"),
            Path.Combine(workspace.Path, "identity.json"));
        await Pair(client, server);

        server.ClockSkew = true;
        var skew = await client.Health(server.RunnerId);
        Assert.False(skew.Success);
        Assert.Equal("runner_clock_skew", skew.ErrorCode);
        Assert.DoesNotContain(server.PairingCode, skew.Message!);

        server.ClockSkew = false;
        server.InvalidCapabilities = true;
        var invalid = await client.Capabilities(server.RunnerId);
        Assert.False(invalid.Success);
        Assert.Equal("invalid_runner_response", invalid.ErrorCode);
    }

    private static AgentRunnerClient Client(string root, string identityPath) =>
        new(new AgentRunnerRegistrationStore(new AgentRunnerRegistrationStoreOptions { RootPath = root }),
            new IdentityService(new IdentityServiceOptions { IdentityPath = identityPath }));

    private static async Task<AgentRunnerRegistration> Pair(AgentRunnerClient client,
        FakeRunnerServer server)
    {
        var result = await client.Pair(new AgentRunnerPairingRequest(server.Endpoint, server.RunnerId,
            server.Fingerprint, server.PairingCode));
        Assert.True(result.Success, result.Message);
        return result.Payload!;
    }

    private static AgentRunRequest Request(string runnerId, string runId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AgentRunContracts", "v1", "run-request.json");
        var fixture = JsonSerializer.Deserialize<AgentRunRequest>(File.ReadAllText(path), AgentRunJson.Options)!;
        var specification = fixture.Specification with
        {
            RunId = runId,
            Runtime = fixture.Specification.Runtime with { RunnerId = runnerId },
        };
        return new AgentRunRequest(AgentRunCanonicalJson.ComputeSpecificationHash(specification), specification);
    }

    private static byte[] Canonical(string pathAndQuery, ReadOnlySpan<byte> body,
        DateTimeOffset timestamp, string nonce, string clientId) => Encoding.UTF8.GetBytes(string.Join('\n',
        "pm-runner-auth-v1", "POST", pathAndQuery, AgentRunProtocol.Current.ToString(),
        timestamp.ToUnixTimeSeconds(), nonce,
        clientId, AgentRunnerRequestSigning.Sha256Hex(body)));

    private static bool VerifySignature(string publicKey, string signature, byte[] canonical)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(DecodeBase64Url(publicKey), out _);
        return key.VerifyData(canonical, DecodeBase64Url(signature), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight(
            normalized.Length + (4 - normalized.Length % 4) % 4, '='));
    }

    private sealed class FakeRunnerServer : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private readonly X509Certificate2 _certificate;
        private readonly Dictionary<string, AgentRunnerRun> _runs = [];
        private string? _clientId;
        private string? _clientPublicKey;

        private FakeRunnerServer(WebApplication application, X509Certificate2 certificate,
            string runnerId, AgentRunnerCapabilities capabilities)
        {
            _application = application;
            _certificate = certificate;
            RunnerId = runnerId;
            Capabilities = capabilities;
            Fingerprint = $"sha256:{AgentRunnerRequestSigning.Sha256Hex(certificate.RawData)}";
        }

        public string RunnerId { get; }
        public string PairingCode { get; } = "ABCD-EFGH-JKMP";
        public string Fingerprint { get; }
        public Uri Endpoint { get; private set; } = null!;
        public AgentRunnerCapabilities Capabilities { get; }
        public bool ClockSkew { get; set; }
        public bool InvalidCapabilities { get; set; }

        public static async Task<FakeRunnerServer> Start(string runnerId)
        {
            var certificate = Certificate();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            var application = builder.Build();
            var capabilities = CapabilitiesFor(runnerId);
            var server = new FakeRunnerServer(application, certificate, runnerId, capabilities);
            application.Run(server.Handle);
            await application.StartAsync();
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses;
            server.Endpoint = new Uri(addresses.Single().Replace("localhost", "127.0.0.1"));
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
            _certificate.Dispose();
        }

        private async Task Handle(HttpContext context)
        {
            var body = await ReadBody(context.Request);
            var path = context.Request.Path.Value ?? string.Empty;
            if (path == "/v1/pairing/complete" && context.Request.Method == "POST")
            {
                await Pair(context, body);
                return;
            }

            if (ClockSkew)
            {
                context.Response.Headers["PM-Runner-Server-Time"] =
                    DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString();
                await Error(context, 401, "unauthorized", "Authentication failed.");
                return;
            }
            if (!Authenticate(context.Request, body))
            {
                await Error(context, 401, "unauthorized", "Authentication failed.");
                return;
            }

            if (path == "/v1/health" && context.Request.Method == "GET")
            {
                await Json(context, new
                {
                    runnerId = RunnerId,
                    status = "online",
                    protocolVersion = "1.0",
                    timestamp = DateTimeOffset.UtcNow,
                    build = new
                    {
                        version = "0.1.0",
                        sourceRevision = new string('a', 40),
                        imageDigest = $"sha256:{new string('b', 64)}",
                    },
                    futureField = true,
                });
                return;
            }
            if (path == "/v1/capabilities" && context.Request.Method == "GET")
            {
                if (InvalidCapabilities)
                    await Json(context, Capabilities with { RunnerId = "wrong-runner" });
                else
                    await Json(context, Capabilities);
                return;
            }
            if (path == "/v1/client/rotate" && context.Request.Method == "POST")
            {
                await Rotate(context, body);
                return;
            }
            if (path == "/v1/client" && context.Request.Method == "DELETE")
            {
                _clientId = null;
                _clientPublicKey = null;
                context.Response.StatusCode = 204;
                return;
            }
            if (path == "/v1/runs" && context.Request.Method == "POST")
            {
                await StartRun(context, body);
                return;
            }
            if (path == "/v1/runs" && context.Request.Method == "GET")
            {
                await Json(context, new
                {
                    runs = _runs.Values.Select(ToSummary).ToList(),
                    nextCursor = (string?)null,
                    hasMore = false,
                });
                return;
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments is ["v1", "runs", var runId, ..] && _runs.TryGetValue(runId, out var run))
            {
                if (segments.Length == 3 && context.Request.Method == "GET")
                {
                    await Json(context, new { run });
                    return;
                }
                if (segments is ["v1", "runs", _, "events"])
                {
                    await EventPage(context, runId);
                    return;
                }
                if (segments is ["v1", "runs", _, "events", "stream"])
                {
                    await Stream(context, runId);
                    return;
                }
                if (segments is ["v1", "runs", _, "cancel"] && context.Request.Method == "POST")
                {
                    var cancelled = run with
                    {
                        State = AgentRunState.Cancelled,
                        TerminalAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    };
                    _runs[runId] = cancelled;
                    await Json(context, new { disposition = "cancelled", run = cancelled });
                    return;
                }
                if (segments is ["v1", "runs", _, "artifacts"])
                {
                    await Json(context, new { artifacts = new[] { ArtifactFor(runId) } });
                    return;
                }
                if (segments is ["v1", "runs", _, "artifacts", var contentArtifactId, "content"])
                {
                    var artifact = ArtifactFor(runId);
                    if (artifact.ArtifactId != contentArtifactId)
                    {
                        await Error(context, 404, "artifact_not_found", "Artifact not found.");
                        return;
                    }
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = artifact.MediaType;
                    context.Response.ContentLength = artifact.ByteLength;
                    context.Response.Headers["PM-Artifact-Id"] = artifact.ArtifactId;
                    context.Response.Headers["PM-Artifact-SHA256"] = artifact.Sha256;
                    context.Response.Headers.ETag = $"\"sha256:{artifact.Sha256}\"";
                    await context.Response.Body.WriteAsync(ArtifactBytes());
                    return;
                }
                if (segments is ["v1", "runs", _, "artifacts", var artifactId])
                {
                    var artifact = ArtifactFor(runId);
                    if (artifact.ArtifactId != artifactId)
                    {
                        await Error(context, 404, "artifact_not_found", "Artifact not found.");
                        return;
                    }
                    await Json(context, new { artifact });
                    return;
                }
            }

            await Error(context, 404, "not_found", "Not found.");
        }

        private async Task Pair(HttpContext context, byte[] body)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.GetProperty("code").GetString() != PairingCode)
            {
                await Error(context, 401, "pairing_failed", "Pairing failed.");
                return;
            }
            var client = root.GetProperty("client");
            _clientId = client.GetProperty("clientId").GetString();
            _clientPublicKey = client.GetProperty("publicKey").GetString();
            context.Response.StatusCode = 201;
            await Json(context, new
            {
                runnerId = RunnerId,
                protocolVersion = "1.0",
                tlsFingerprint = Fingerprint,
                client = new
                {
                    clientId = _clientId,
                    displayName = client.GetProperty("displayName").GetString(),
                    fingerprint = AgentRunnerRequestSigning.PublicKeyFingerprint(_clientPublicKey!),
                },
                capabilities = Capabilities,
                futureField = "ignored",
            }, 201);
        }

        private bool Authenticate(HttpRequest request, byte[] body)
        {
            var clientId = request.Headers["PM-Runner-Client-Id"].SingleOrDefault();
            var timestamp = request.Headers["PM-Runner-Timestamp"].SingleOrDefault();
            var nonce = request.Headers["PM-Runner-Nonce"].SingleOrDefault();
            var signature = request.Headers["PM-Runner-Signature"].SingleOrDefault();
            var version = request.Headers["PM-Runner-Protocol-Version"].SingleOrDefault();
            if (clientId == null || clientId != _clientId || timestamp == null || nonce == null ||
                signature == null || version is not ("1.0" or "1.1") || _clientPublicKey == null) return false;
            var canonical = string.Join('\n', "pm-runner-auth-v1", request.Method,
                request.Path + request.QueryString, version, timestamp, nonce, clientId,
                AgentRunnerRequestSigning.Sha256Hex(body));
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Base64UrlDecode(_clientPublicKey), out _);
                return key.VerifyData(Encoding.UTF8.GetBytes(canonical), Base64UrlDecode(signature),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private async Task StartRun(HttpContext context, byte[] body)
        {
            var request = JsonSerializer.Deserialize<AgentRunRequest>(body, AgentRunJson.Options)!;
            if (_runs.TryGetValue(request.Specification.RunId, out var existing))
            {
                if (existing.SpecificationHash != request.SpecificationHash)
                {
                    await Error(context, 409, "run_id_conflict", "Run ID conflict.");
                    return;
                }
                await Json(context, new { disposition = "existing", run = existing });
                return;
            }
            var now = DateTimeOffset.UtcNow;
            var run = new AgentRunnerRun(request.Specification.RunId, request.SpecificationHash,
                request.Specification, AgentRunState.Queued, 3, now, now, null, null, null);
            _runs.Add(run.RunId, run);
            await Json(context, new { disposition = "new", run }, 202);
        }

        private async Task EventPage(HttpContext context, string runId)
        {
            var after = long.Parse(context.Request.Query["afterSequence"].SingleOrDefault() ?? "0");
            var limit = int.Parse(context.Request.Query["limit"].SingleOrDefault() ?? "100");
            var events = EventsFor(runId).Where(item => item.Sequence > after).Take(limit).ToList();
            await Json(context, new
            {
                events,
                nextAfterSequence = events.LastOrDefault()?.Sequence ?? after,
                hasMore = after + events.Count < 3,
                terminal = true,
            });
        }

        private async Task Stream(HttpContext context, string runId)
        {
            var after = long.Parse(context.Request.Query["afterSequence"].SingleOrDefault() ?? "0");
            context.Response.ContentType = "text/event-stream";
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("retry: 2000\n\n: heartbeat\n\n");
            var events = EventsFor(runId).Where(item => item.Sequence > after).ToList();
            if (after == 0)
            {
                var first = events[0];
                await context.Response.WriteAsync(
                    $"id: {first.Sequence}\nevent: run-event\ndata: {JsonSerializer.Serialize(first, CompactJson)}\n\n");
                return;
            }
            foreach (var runEvent in events)
                await context.Response.WriteAsync(
                    $"id: {runEvent.Sequence}\nevent: run-event\ndata: {JsonSerializer.Serialize(runEvent, CompactJson)}\n\n");
            await context.Response.WriteAsync(
                $"event: stream-end\ndata: {JsonSerializer.Serialize(new { state = "completed", lastSequence = 3 })}\n\n");
        }

        private async Task Rotate(HttpContext context, byte[] body)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var newClientId = root.GetProperty("clientId").GetString()!;
            var displayName = root.GetProperty("displayName").GetString()!;
            var publicKey = root.GetProperty("publicKey").GetString()!;
            var proof = root.GetProperty("newKeySignature").GetString()!;
            var nonce = context.Request.Headers["PM-Runner-Nonce"].Single();
            var canonical = string.Join('\n', "pm-runner-rotation-v1", RunnerId, _clientId,
                newClientId, publicKey, nonce);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Base64UrlDecode(publicKey), out _);
            if (!key.VerifyData(Encoding.UTF8.GetBytes(canonical), Base64UrlDecode(proof),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                await Error(context, 400, "invalid_rotation_proof", "Invalid rotation proof.");
                return;
            }
            _clientId = newClientId;
            _clientPublicKey = publicKey;
            await Json(context, new
            {
                clientId = newClientId,
                displayName,
                fingerprint = AgentRunnerRequestSigning.PublicKeyFingerprint(publicKey),
                rotatedAt = DateTimeOffset.UtcNow,
            });
        }

        private static AgentRunnerCapabilities CapabilitiesFor(string runnerId)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "AgentRunContracts", "v1",
                "runner-capabilities.json");
            var fixture = JsonSerializer.Deserialize<AgentRunnerCapabilities>(
                File.ReadAllText(path), AgentRunJson.Options)!;
            return fixture with { RunnerId = runnerId };
        }

        private static IReadOnlyList<AgentRunEvent> EventsFor(string runId) =>
        [
            Event(runId, 1, AgentRunState.Accepted),
            Event(runId, 2, AgentRunState.Queued),
            Event(runId, 3, AgentRunState.Completed),
        ];

        private static AgentRunEvent Event(string runId, long sequence, AgentRunState state) =>
            new(AgentRunProtocol.Current, runId, sequence, CanonicalNow().AddSeconds(sequence),
                "run.state_changed", state, $"State {state}",
                JsonSerializer.SerializeToElement(new { nextState = state }));

        private static AgentRunnerRunSummary ToSummary(AgentRunnerRun run) =>
            new(run.RunId, run.Specification.Task.TaskId, run.Specification.Task.Title, run.State,
                run.LastEventSequence, run.AcceptedAt, run.UpdatedAt, run.CancellationRequestedAt);

        private static AgentRunArtifact ArtifactFor(string runId)
        {
            var bytes = ArtifactBytes();
            return new AgentRunArtifact("artifact-patch", "git_patch", $"{runId}.patch", "text/x-diff",
                bytes.Length, AgentRunnerRequestSigning.Sha256Hex(bytes), CanonicalNow());
        }

        private static byte[] ArtifactBytes() => Encoding.UTF8.GetBytes(new string('a', 42));

        private static DateTimeOffset CanonicalNow()
        {
            var now = DateTimeOffset.UtcNow;
            return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        }

        private static async Task<byte[]> ReadBody(HttpRequest request)
        {
            using var memory = new MemoryStream();
            await request.Body.CopyToAsync(memory);
            return memory.ToArray();
        }

        private static async Task Json(HttpContext context, object value, int status = 200)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(value,
                AgentRunJson.Options));
        }

        private static Task Error(HttpContext context, int status, string errorCode, string message) =>
            Json(context, new { errorCode, message }, status);

        private static X509Certificate2 Certificate()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,
                false));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1));
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(normalized.PadRight(
                normalized.Length + (4 - normalized.Length % 4) % 4, '='));
        }

        private static JsonSerializerOptions CompactJson { get; } = new(AgentRunJson.Options)
        {
            WriteIndented = false,
        };
    }
}
