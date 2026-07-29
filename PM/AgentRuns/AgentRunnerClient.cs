using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using PM.Application;
using PM.Auth;

namespace PM.AgentRuns;

public interface IAgentRunnerClient
{
    AppResult<IReadOnlyList<AgentRunnerRegistration>> Registrations();
    AppResult<AgentRunnerRegistration> Registration(string runnerId);
    Task<AppResult<AgentRunnerRegistration>> Pair(AgentRunnerPairingRequest request,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunnerHealth>> Health(string runnerId,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunnerCapabilities>> Capabilities(string runnerId,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunRemoteStart>> Start(string runnerId, AgentRunRequest request,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunnerRun>> Inspect(string runnerId, string runId,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunnerRunPage>> ActiveRuns(string runnerId, int limit = 100,
        string? cursor = null, CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunEventPage>> Events(string runnerId, string runId, long afterSequence = 0,
        int limit = 100, CancellationToken cancellationToken = default);
    Task<AppResult<IAgentRunnerEventStream>> OpenEventStream(string runnerId, string runId,
        long afterSequence = 0, CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunCancellation>> Cancel(string runnerId, string runId,
        CancellationToken cancellationToken = default);
    Task<AppResult<IReadOnlyList<AgentRunArtifact>>> Artifacts(string runnerId, string runId,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunArtifact>> Artifact(string runnerId, string runId, string artifactId,
        CancellationToken cancellationToken = default);
    Task<AppResult<IAgentRunArtifactContent>> ArtifactContent(string runnerId, string runId, string artifactId,
        CancellationToken cancellationToken = default);
    Task<AppResult<AgentRunnerRegistration>> Rotate(string runnerId,
        CancellationToken cancellationToken = default);
    Task<AppResult> Revoke(string runnerId, CancellationToken cancellationToken = default);
}

public sealed class AgentRunnerClient(
    AgentRunnerRegistrationStore registrations,
    IIdentityService identityService) : IAgentRunnerClient
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(AgentRunJson.Options)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
    };

    public AppResult<IReadOnlyList<AgentRunnerRegistration>> Registrations() => registrations.List();

    public AppResult<AgentRunnerRegistration> Registration(string runnerId) => registrations.Get(runnerId);

    public async Task<AppResult<AgentRunnerRegistration>> Pair(
        AgentRunnerPairingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Endpoint.IsAbsoluteUri ||
            !AgentRunnerRegistrationStore.TryNormalizeEndpoint(request.Endpoint.AbsoluteUri, out var endpoint) ||
            request.ExpectedRunnerId.Length == 0 || request.PairingCode.Trim().Length == 0 ||
            !IsFingerprint(request.ExpectedTlsFingerprint))
            return AppResult<AgentRunnerRegistration>.Fail("invalid_runner_pairing", "Runner pairing details are invalid.");
        var existing = registrations.GetStored(request.ExpectedRunnerId);
        if (existing.Success && !request.ReplaceExisting)
            return AppResult<AgentRunnerRegistration>.Fail("runner_already_registered",
                $"Runner {request.ExpectedRunnerId} is already registered.");
        if (!existing.Success && existing.ErrorCode is not "runner_not_registered")
            return AppResult<AgentRunnerRegistration>.Fail(existing.ErrorCode!, existing.Message!);

        AgentRunnerCredential credential;
        try
        {
            credential = AgentRunnerCredential.FromPmIdentity(identityService.GetOrCreateIdentity());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return AppResult<AgentRunnerRegistration>.Fail("invalid_identity", "The PM identity could not be loaded.");
        }

        var body = Serialize(new PairingBody(request.PairingCode.Trim(),
            AgentRunProtocol.Supported.Select(item => item.ToString()).ToArray(),
            new PairingClient(credential.ClientId, credential.DisplayName, credential.PublicKey)));
        var raw = await Send(endpoint, request.ExpectedTlsFingerprint, HttpMethod.Post,
            "/v1/pairing/complete", body, null, cancellationToken);
        if (!raw.Success)
            return AppResult<AgentRunnerRegistration>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.Created)
            return Failure<AgentRunnerRegistration>(raw.Payload);

        var response = Deserialize<PairingResponse>(raw.Payload.Body);
        if (!response.Success) return AppResult<AgentRunnerRegistration>.Fail(response.ErrorCode!, response.Message!);
        var paired = response.Payload!;
        var capabilitiesValidation = AgentRunContractValidator.ValidateCapabilities(paired.Capabilities);
        if (paired.RunnerId != request.ExpectedRunnerId ||
            !AgentRunProtocol.Supported.Contains(paired.ProtocolVersion) ||
            paired.TlsFingerprint != request.ExpectedTlsFingerprint ||
            paired.Client.ClientId != credential.ClientId ||
            paired.Client.Fingerprint != credential.Fingerprint ||
            paired.Capabilities.RunnerId != paired.RunnerId || !capabilitiesValidation.Success)
            return AppResult<AgentRunnerRegistration>.Fail("invalid_runner_response",
                "The runner pairing response did not match the verified identity.");

        var stored = AgentRunnerRegistrationStore.CreateStored(
            paired.RunnerId,
            paired.Capabilities.DisplayName,
            endpoint,
            paired.TlsFingerprint,
            paired.ProtocolVersion,
            credential,
            paired.Client.Fingerprint,
            DateTimeOffset.UtcNow);
        var saved = registrations.Save(stored, request.ReplaceExisting);
        return saved.Success
            ? registrations.Get(paired.RunnerId)
            : AppResult<AgentRunnerRegistration>.Fail(saved.ErrorCode!, saved.Message!);
    }

    public async Task<AppResult<AgentRunnerHealth>> Health(string runnerId,
        CancellationToken cancellationToken = default)
    {
        var raw = await SendRegistered(runnerId, HttpMethod.Get, "/v1/health", [], cancellationToken);
        return ParseRegistered<AgentRunnerHealth>(raw, runnerId, health =>
            health.RunnerId == runnerId && health.Status == "online" &&
            AgentRunProtocol.IsCompatible(health.ProtocolVersion, AgentRunProtocol.Current));
    }

    public async Task<AppResult<AgentRunnerCapabilities>> Capabilities(string runnerId,
        CancellationToken cancellationToken = default)
    {
        var raw = await SendRegistered(runnerId, HttpMethod.Get, "/v1/capabilities", [], cancellationToken);
        var parsed = ParseRegistered<AgentRunnerCapabilities>(raw, runnerId, capabilities =>
            capabilities.RunnerId == runnerId && AgentRunContractValidator.ValidateCapabilities(capabilities).Success);
        if (!parsed.Success) return parsed;
        var selected = AgentRunProtocol.HighestCommon(parsed.Payload!.ProtocolVersions);
        if (selected == null)
            return AppResult<AgentRunnerCapabilities>.Fail("incompatible_protocol",
                $"Runner {runnerId} does not advertise a compatible protocol.");
        var stored = registrations.GetStored(runnerId);
        if (!stored.Success)
            return AppResult<AgentRunnerCapabilities>.Fail(stored.ErrorCode!, stored.Message!);
        if (stored.Payload!.ProtocolVersion != selected.Value)
        {
            var saved = registrations.Save(stored.Payload with { ProtocolVersion = selected.Value }, true);
            if (!saved.Success)
                return AppResult<AgentRunnerCapabilities>.Fail(saved.ErrorCode!, saved.Message!);
        }
        return parsed;
    }

    public async Task<AppResult<AgentRunRemoteStart>> Start(string runnerId, AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = AgentRunContractValidator.ValidateRequest(request);
        if (!validation.Success)
            return AppResult<AgentRunRemoteStart>.Fail(validation.ErrorCode!, validation.Message!);
        var raw = await SendRegistered(runnerId, HttpMethod.Post, "/v1/runs", Serialize(request), cancellationToken);
        if (!raw.Success) return AppResult<AgentRunRemoteStart>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode is not (HttpStatusCode.Accepted or HttpStatusCode.OK))
            return Failure<AgentRunRemoteStart>(raw.Payload);
        var parsed = Deserialize<StartResponse>(raw.Payload.Body);
        if (!parsed.Success) return AppResult<AgentRunRemoteStart>.Fail(parsed.ErrorCode!, parsed.Message!);
        var disposition = parsed.Payload!.Disposition switch
        {
            "new" when raw.Payload.StatusCode == HttpStatusCode.Accepted => AgentRunRemoteStartDisposition.New,
            "existing" when raw.Payload.StatusCode == HttpStatusCode.OK => AgentRunRemoteStartDisposition.Existing,
            _ => (AgentRunRemoteStartDisposition?)null,
        };
        return disposition.HasValue && ValidateRun(parsed.Payload.Run, runnerId)
            ? AppResult<AgentRunRemoteStart>.Ok(new AgentRunRemoteStart(disposition.Value, parsed.Payload.Run))
            : AppResult<AgentRunRemoteStart>.Fail("invalid_runner_response", "The runner returned an invalid start response.");
    }

    public async Task<AppResult<AgentRunnerRun>> Inspect(string runnerId, string runId,
        CancellationToken cancellationToken = default)
    {
        var raw = await SendRegistered(runnerId, HttpMethod.Get, $"/v1/runs/{Escape(runId)}", [], cancellationToken);
        if (!raw.Success) return AppResult<AgentRunnerRun>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.OK) return Failure<AgentRunnerRun>(raw.Payload);
        var parsed = Deserialize<RunResponse>(raw.Payload.Body);
        return parsed.Success && ValidateRun(parsed.Payload!.Run, runnerId) && parsed.Payload.Run.RunId == runId
            ? AppResult<AgentRunnerRun>.Ok(parsed.Payload.Run)
            : AppResult<AgentRunnerRun>.Fail("invalid_runner_response", "The runner returned an invalid run.");
    }

    public async Task<AppResult<AgentRunnerRunPage>> ActiveRuns(string runnerId, int limit = 100,
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
            return AppResult<AgentRunnerRunPage>.Fail("invalid_page_limit", "Page limit must be between 1 and 500.");
        var path = $"/v1/runs?scope=active&limit={limit}" +
                   (cursor == null ? string.Empty : $"&cursor={Escape(cursor)}");
        var raw = await SendRegistered(runnerId, HttpMethod.Get, path, [], cancellationToken);
        if (!raw.Success) return AppResult<AgentRunnerRunPage>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.OK) return Failure<AgentRunnerRunPage>(raw.Payload);
        var parsed = Deserialize<AgentRunnerRunPage>(raw.Payload.Body);
        return parsed.Success && parsed.Payload!.Runs.All(ValidateRunSummary)
            ? parsed
            : AppResult<AgentRunnerRunPage>.Fail("invalid_runner_response", "The runner returned an invalid run page.");
    }

    public async Task<AppResult<AgentRunEventPage>> Events(string runnerId, string runId,
        long afterSequence = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 500)
            return AppResult<AgentRunEventPage>.Fail("invalid_event_sequence", "Event replay options are invalid.");
        var path = $"/v1/runs/{Escape(runId)}/events?afterSequence={afterSequence}&limit={limit}";
        var raw = await SendRegistered(runnerId, HttpMethod.Get, path, [], cancellationToken);
        if (!raw.Success) return AppResult<AgentRunEventPage>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.OK) return Failure<AgentRunEventPage>(raw.Payload);
        var parsed = Deserialize<AgentRunEventPage>(raw.Payload.Body);
        if (!parsed.Success) return parsed;
        var page = parsed.Payload!;
        var expected = afterSequence;
        foreach (var runEvent in page.Events)
        {
            if (runEvent.RunId != runId || !AgentRunContractValidator.ValidateEvent(runEvent).Success ||
                !AgentRunReplay.ValidateNextSequence(expected, runEvent).Success)
                return AppResult<AgentRunEventPage>.Fail("invalid_event_sequence",
                    "The runner returned an invalid event sequence.");
            expected = runEvent.Sequence;
        }
        return page.NextAfterSequence == expected && (!page.HasMore || page.Events.Count > 0)
            ? parsed
            : AppResult<AgentRunEventPage>.Fail("invalid_event_sequence",
                "The runner returned an invalid event cursor.");
    }

    public async Task<AppResult<IAgentRunnerEventStream>> OpenEventStream(string runnerId, string runId,
        long afterSequence = 0, CancellationToken cancellationToken = default)
    {
        if (afterSequence < 0)
            return AppResult<IAgentRunnerEventStream>.Fail("invalid_event_sequence", "Event sequence cannot be negative.");
        var stored = registrations.GetStored(runnerId);
        if (!stored.Success)
            return AppResult<IAgentRunnerEventStream>.Fail(stored.ErrorCode!, stored.Message!);
        var path = $"/v1/runs/{Escape(runId)}/events/stream?afterSequence={afterSequence}";
        var pinned = CreateClient(stored.Payload!.TlsFingerprint, Timeout.InfiniteTimeSpan);
        try
        {
            var uri = new Uri(new Uri(stored.Payload.Endpoint), path);
            using var request = SignedRequest(stored.Payload, HttpMethod.Get, uri, []);
            var response = await pinned.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var bytes = await ReadBounded(response.Content, cancellationToken);
                var failure = Failure<IAgentRunnerEventStream>(new RawResponse(response.StatusCode,
                    response.Headers, bytes));
                response.Dispose();
                pinned.Client.Dispose();
                return failure;
            }
            if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
            {
                response.Dispose();
                pinned.Client.Dispose();
                return AppResult<IAgentRunnerEventStream>.Fail("invalid_runner_response",
                    "The runner returned an invalid event stream content type.");
            }
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return AppResult<IAgentRunnerEventStream>.Ok(
                new AgentRunnerEventStream(pinned.Client, response, stream, runId, afterSequence));
        }
        catch (Exception exception) when (IsTransportException(exception))
        {
            pinned.Client.Dispose();
            return AppResult<IAgentRunnerEventStream>.Fail(
                pinned.CertificateRejected ? "runner_tls_mismatch" : "runner_unavailable",
                pinned.CertificateRejected
                    ? "The runner TLS certificate did not match its pinned identity."
                    : "The runner could not be reached.");
        }
    }

    public async Task<AppResult<AgentRunCancellation>> Cancel(string runnerId, string runId,
        CancellationToken cancellationToken = default)
    {
        var raw = await SendRegistered(runnerId, HttpMethod.Post,
            $"/v1/runs/{Escape(runId)}/cancel", [], cancellationToken);
        if (!raw.Success) return AppResult<AgentRunCancellation>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted))
            return Failure<AgentRunCancellation>(raw.Payload);
        var parsed = Deserialize<AgentRunCancellation>(raw.Payload.Body);
        return parsed.Success && ValidateRun(parsed.Payload!.Run, runnerId)
            ? parsed
            : AppResult<AgentRunCancellation>.Fail("invalid_runner_response",
                "The runner returned an invalid cancellation response.");
    }

    public async Task<AppResult<IReadOnlyList<AgentRunArtifact>>> Artifacts(string runnerId,
        string runId, CancellationToken cancellationToken = default)
    {
        var raw = await SendRegistered(runnerId, HttpMethod.Get,
            $"/v1/runs/{Escape(runId)}/artifacts", [], cancellationToken);
        if (!raw.Success) return AppResult<IReadOnlyList<AgentRunArtifact>>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.OK)
            return Failure<IReadOnlyList<AgentRunArtifact>>(raw.Payload);
        var parsed = Deserialize<ArtifactListResponse>(raw.Payload.Body);
        return parsed.Success && parsed.Payload!.Artifacts.All(item => AgentRunContractValidator.ValidateArtifact(item).Success)
            ? AppResult<IReadOnlyList<AgentRunArtifact>>.Ok(parsed.Payload.Artifacts)
            : AppResult<IReadOnlyList<AgentRunArtifact>>.Fail("invalid_runner_response",
                "The runner returned invalid artifact metadata.");
    }

    public async Task<AppResult<AgentRunArtifact>> Artifact(string runnerId, string runId,
        string artifactId, CancellationToken cancellationToken = default)
    {
        var raw = await SendRegistered(runnerId, HttpMethod.Get,
            $"/v1/runs/{Escape(runId)}/artifacts/{Escape(artifactId)}", [], cancellationToken);
        if (!raw.Success) return AppResult<AgentRunArtifact>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.OK) return Failure<AgentRunArtifact>(raw.Payload);
        var parsed = Deserialize<ArtifactResponse>(raw.Payload.Body);
        return parsed.Success && AgentRunContractValidator.ValidateArtifact(parsed.Payload!.Artifact).Success
            ? AppResult<AgentRunArtifact>.Ok(parsed.Payload.Artifact)
            : AppResult<AgentRunArtifact>.Fail("invalid_runner_response",
                "The runner returned invalid artifact metadata.");
    }

    public async Task<AppResult<IAgentRunArtifactContent>> ArtifactContent(string runnerId, string runId,
        string artifactId, CancellationToken cancellationToken = default)
    {
        var metadata = await Artifact(runnerId, runId, artifactId, cancellationToken);
        if (!metadata.Success)
            return AppResult<IAgentRunArtifactContent>.Fail(metadata.ErrorCode!, metadata.Message!);
        const long maximumArtifactBytes = 64L * 1024 * 1024;
        if (metadata.Payload!.ByteLength > maximumArtifactBytes)
            return AppResult<IAgentRunArtifactContent>.Fail("artifact_too_large",
                "The artifact exceeds the transfer limit.");

        var stored = registrations.GetStored(runnerId);
        if (!stored.Success)
            return AppResult<IAgentRunArtifactContent>.Fail(stored.ErrorCode!, stored.Message!);
        var path = $"/v1/runs/{Escape(runId)}/artifacts/{Escape(artifactId)}/content";
        var pinned = CreateClient(stored.Payload!.TlsFingerprint, Timeout.InfiniteTimeSpan);
        try
        {
            var uri = new Uri(new Uri(stored.Payload.Endpoint), path);
            using var request = SignedRequest(stored.Payload, HttpMethod.Get, uri, []);
            var response = await pinned.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var bytes = await ReadBounded(response.Content, cancellationToken);
                var failure = Failure<IAgentRunArtifactContent>(new RawResponse(response.StatusCode,
                    response.Headers, bytes));
                response.Dispose();
                pinned.Client.Dispose();
                return failure;
            }

            var artifact = metadata.Payload;
            var validHeaders = response.Content.Headers.ContentLength == artifact.ByteLength &&
                               response.Content.Headers.ContentType?.MediaType == artifact.MediaType &&
                               Header(response.Headers, "PM-Artifact-Id") == artifact.ArtifactId &&
                               Header(response.Headers, "PM-Artifact-SHA256") == artifact.Sha256 &&
                               response.Headers.ETag?.Tag == $"\"sha256:{artifact.Sha256}\"";
            if (!validHeaders)
            {
                response.Dispose();
                pinned.Client.Dispose();
                return AppResult<IAgentRunArtifactContent>.Fail("invalid_runner_response",
                    "The runner returned invalid artifact content metadata.");
            }
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return AppResult<IAgentRunArtifactContent>.Ok(
                new AgentRunArtifactContent(artifact, stream, response, pinned.Client));
        }
        catch (Exception exception) when (IsTransportException(exception))
        {
            pinned.Client.Dispose();
            return AppResult<IAgentRunArtifactContent>.Fail(
                pinned.CertificateRejected ? "runner_tls_mismatch" : "runner_unavailable",
                pinned.CertificateRejected
                    ? "The runner TLS certificate did not match its pinned identity."
                    : "The runner could not be reached.");
        }
    }

    public async Task<AppResult<AgentRunnerRegistration>> Rotate(string runnerId,
        CancellationToken cancellationToken = default)
    {
        var stored = registrations.GetStored(runnerId);
        if (!stored.Success)
            return AppResult<AgentRunnerRegistration>.Fail(stored.ErrorCode!, stored.Message!);
        var current = stored.Payload!;
        var successor = AgentRunnerCredential.Generate(current.Credential.DisplayName);
        var nonce = $"nonce_{AgentRunnerEncoding.Base64Url(RandomNumberGenerator.GetBytes(18))}";
        var proof = AgentRunnerRequestSigning.SignRotationProof(successor, runnerId,
            current.Credential.ClientId, nonce);
        var body = Serialize(new RotationBody(successor.ClientId, successor.DisplayName,
            successor.PublicKey, proof));
        var raw = await Send(new Uri(current.Endpoint), current.TlsFingerprint, HttpMethod.Post,
            "/v1/client/rotate", body, current, cancellationToken, nonce);
        if (!raw.Success) return AppResult<AgentRunnerRegistration>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.OK)
            return Failure<AgentRunnerRegistration>(raw.Payload);
        var parsed = Deserialize<RotationResponse>(raw.Payload.Body);
        if (!parsed.Success || parsed.Payload!.ClientId != successor.ClientId ||
            parsed.Payload.Fingerprint != successor.Fingerprint)
            return AppResult<AgentRunnerRegistration>.Fail("invalid_runner_response",
                "The runner returned an invalid credential rotation response.");
        var replaced = current with
        {
            Credential = successor,
            ClientFingerprint = successor.Fingerprint,
        };
        var saved = registrations.Save(replaced, true);
        return saved.Success
            ? registrations.Get(runnerId)
            : AppResult<AgentRunnerRegistration>.Fail(saved.ErrorCode!, saved.Message!);
    }

    public async Task<AppResult> Revoke(string runnerId, CancellationToken cancellationToken = default)
    {
        var raw = await SendRegistered(runnerId, HttpMethod.Delete, "/v1/client", [], cancellationToken);
        if (!raw.Success) return AppResult.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.NoContent)
        {
            var failure = Failure<object>(raw.Payload);
            return AppResult.Fail(failure.ErrorCode!, failure.Message!);
        }
        return registrations.Remove(runnerId);
    }

    private async Task<AppResult<RawResponse>> SendRegistered(string runnerId, HttpMethod method,
        string path, byte[] body, CancellationToken cancellationToken)
    {
        var stored = registrations.GetStored(runnerId);
        return stored.Success
            ? await Send(new Uri(stored.Payload!.Endpoint), stored.Payload.TlsFingerprint, method,
                path, body, stored.Payload, cancellationToken)
            : AppResult<RawResponse>.Fail(stored.ErrorCode!, stored.Message!);
    }

    private static async Task<AppResult<RawResponse>> Send(Uri endpoint, string fingerprint,
        HttpMethod method, string path, byte[] body, StoredAgentRunnerRegistration? registration,
        CancellationToken cancellationToken, string? nonce = null)
    {
        var pinned = CreateClient(fingerprint, TimeSpan.FromSeconds(30));
        try
        {
            var uri = new Uri(endpoint, path);
            using var request = registration == null
                ? PlainRequest(method, uri, body)
                : SignedRequest(registration, method, uri, body, nonce);
            using var response = await pinned.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var responseBody = await ReadBounded(response.Content, cancellationToken);
            return AppResult<RawResponse>.Ok(new RawResponse(response.StatusCode, response.Headers, responseBody));
        }
        catch (Exception exception) when (IsTransportException(exception))
        {
            return AppResult<RawResponse>.Fail(
                pinned.CertificateRejected ? "runner_tls_mismatch" : "runner_unavailable",
                pinned.CertificateRejected
                    ? "The runner TLS certificate did not match its pinned identity."
                    : "The runner could not be reached.");
        }
        finally
        {
            pinned.Client.Dispose();
        }
    }

    private static AppResult<T> ParseRegistered<T>(AppResult<RawResponse> raw,
        string runnerId, Func<T, bool> validate)
    {
        if (!raw.Success) return AppResult<T>.Fail(raw.ErrorCode!, raw.Message!);
        if (raw.Payload!.StatusCode != HttpStatusCode.OK) return Failure<T>(raw.Payload);
        var parsed = Deserialize<T>(raw.Payload.Body);
        return parsed.Success && validate(parsed.Payload!)
            ? parsed
            : AppResult<T>.Fail("invalid_runner_response", $"Runner {runnerId} returned an invalid response.");
    }

    private static HttpRequestMessage PlainRequest(HttpMethod method, Uri uri, byte[] body)
    {
        var request = new HttpRequestMessage(method, uri);
        AddBody(request, body);
        return request;
    }

    private static HttpRequestMessage SignedRequest(StoredAgentRunnerRegistration registration,
        HttpMethod method, Uri uri, byte[] body, string? nonce = null)
    {
        var signed = AgentRunnerRequestSigning.Sign(registration.Credential, method, uri.PathAndQuery,
            body, registration.ProtocolVersion, nonce: nonce);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("PM-Runner-Client-Id", signed.ClientId);
        request.Headers.Add("PM-Runner-Timestamp", signed.Timestamp);
        request.Headers.Add("PM-Runner-Nonce", signed.Nonce);
        request.Headers.Add("PM-Runner-Signature", signed.Signature);
        request.Headers.Add("PM-Runner-Protocol-Version", signed.ProtocolVersion);
        AddBody(request, body);
        return request;
    }

    private static void AddBody(HttpRequestMessage request, byte[] body)
    {
        if (body.Length == 0) return;
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };
    }

    private static PinnedClient CreateClient(string expectedFingerprint, TimeSpan timeout)
    {
        var state = new CertificatePinState(expectedFingerprint);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = state.Validate,
        };
        return new PinnedClient(new HttpClient(handler, true) { Timeout = timeout }, state);
    }

    private static async Task<byte[]> ReadBounded(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw new HttpRequestException("Runner response exceeded the client size limit.");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return memory.ToArray();
            if (memory.Length + read > MaximumResponseBytes)
                throw new HttpRequestException("Runner response exceeded the client size limit.");
            memory.Write(buffer, 0, read);
        }
    }

    private static AppResult<T> Deserialize<T>(byte[] body)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return value == null
                ? AppResult<T>.Fail("invalid_runner_response", "The runner returned an empty response.")
                : AppResult<T>.Ok(value);
        }
        catch (JsonException)
        {
            return AppResult<T>.Fail("invalid_runner_response", "The runner returned invalid JSON.");
        }
    }

    private static AppResult<T> Failure<T>(RawResponse response)
    {
        var error = Deserialize<ErrorResponse>(response.Body);
        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized when response.ServerTime != null => "runner_clock_skew",
            HttpStatusCode.Unauthorized => "runner_unauthorized",
            HttpStatusCode.UpgradeRequired => "incompatible_protocol",
            _ => error.Success ? error.Payload!.ErrorCode : "runner_error",
        };
        var message = code == "runner_clock_skew"
            ? $"The runner rejected the request because the clocks differ. Runner Unix time: {response.ServerTime}."
            : error.Success ? error.Payload!.Message : "The runner request failed.";
        return AppResult<T>.Fail(code, message);
    }

    private static bool ValidateRun(AgentRunnerRun run, string runnerId) =>
        run.RunId == run.Specification.RunId && run.Specification.Runtime.RunnerId == runnerId &&
        run.LastEventSequence >= 0 && Enum.IsDefined(run.State) &&
        AgentRunContractValidator.ValidateRequest(new AgentRunRequest(run.SpecificationHash, run.Specification)).Success;

    private static bool ValidateRunSummary(AgentRunnerRunSummary run) =>
        !string.IsNullOrWhiteSpace(run.RunId) && !string.IsNullOrWhiteSpace(run.TaskId) &&
        !string.IsNullOrWhiteSpace(run.TaskTitle) && run.LastEventSequence >= 0 && Enum.IsDefined(run.State);

    private static bool IsFingerprint(string value) =>
        value.Length == 71 && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string? Header(System.Net.Http.Headers.HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.SingleOrDefault() : null;
    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    private static bool IsTransportException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or AuthenticationException or IOException;

    private sealed class CertificatePinState(string expectedFingerprint)
    {
        public bool Rejected { get; private set; }

        public bool Validate(HttpRequestMessage _, X509Certificate2? certificate, X509Chain? __,
            SslPolicyErrors errors)
        {
            try
            {
                if (certificate == null || (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0 ||
                    DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() ||
                    DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
                    return Reject();
                var actual = $"sha256:{AgentRunnerRequestSigning.Sha256Hex(certificate.RawData)}";
                var expected = System.Text.Encoding.ASCII.GetBytes(expectedFingerprint);
                var received = System.Text.Encoding.ASCII.GetBytes(actual);
                return expected.Length == received.Length && CryptographicOperations.FixedTimeEquals(expected, received)
                    ? true
                    : Reject();
            }
            catch (CryptographicException)
            {
                return Reject();
            }
        }

        private bool Reject()
        {
            Rejected = true;
            return false;
        }
    }

    private sealed record PinnedClient(HttpClient Client, CertificatePinState State)
    {
        public bool CertificateRejected => State.Rejected;
    }

    private sealed record RawResponse(HttpStatusCode StatusCode,
        System.Net.Http.Headers.HttpResponseHeaders Headers, byte[] Body)
    {
        public string? ServerTime => Headers.TryGetValues("PM-Runner-Server-Time", out var values)
            ? values.SingleOrDefault()
            : null;
    }

    private sealed record ErrorResponse(string ErrorCode, string Message);
    private sealed record PairingBody(string Code, IReadOnlyList<string> ProtocolVersions, PairingClient Client);
    private sealed record PairingClient(string ClientId, string DisplayName, string PublicKey);
    private sealed record PairingResponse(string RunnerId, AgentRunProtocolVersion ProtocolVersion,
        string TlsFingerprint, PairingResponseClient Client, AgentRunnerCapabilities Capabilities);
    private sealed record PairingResponseClient(string ClientId, string DisplayName, string Fingerprint);
    private sealed record StartResponse(string Disposition, AgentRunnerRun Run);
    private sealed record RunResponse(AgentRunnerRun Run);
    private sealed record ArtifactListResponse(IReadOnlyList<AgentRunArtifact> Artifacts);
    private sealed record ArtifactResponse(AgentRunArtifact Artifact);
    private sealed record RotationBody(string ClientId, string DisplayName, string PublicKey,
        string NewKeySignature);
    private sealed record RotationResponse(string ClientId, string DisplayName, string Fingerprint,
        DateTimeOffset RotatedAt);

    private sealed class AgentRunArtifactContent(
        AgentRunArtifact artifact,
        Stream content,
        HttpResponseMessage response,
        HttpClient client) : IAgentRunArtifactContent
    {
        public AgentRunArtifact Artifact { get; } = artifact;
        public Stream Content { get; } = content;

        public async ValueTask DisposeAsync()
        {
            await Content.DisposeAsync();
            response.Dispose();
            client.Dispose();
        }
    }
}
