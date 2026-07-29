using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.AgentRuns;

namespace PM.Api;

public sealed record PairAgentRunnerRequest(
    string Endpoint,
    string RunnerId,
    string TlsFingerprint,
    string PairingCode,
    bool ReplaceExisting = false);

public sealed record AgentRunnerStatusResponse(
    AgentRunnerRegistration Registration,
    AgentRunnerHealth Health,
    AgentRunnerCapabilities Capabilities,
    string Revision);

public sealed record AgentRunPreflightRequest(
    string TaskId,
    string RunnerId,
    string ProfileId,
    string ProviderId,
    string ModelId,
    string EffortId);

public sealed record AgentRunActionRequest;

public static class AgentRunApiEndpoints
{
    private static readonly JsonSerializerOptions StreamJson = new(AgentRunJson.Options)
    {
        WriteIndented = false,
    };

    public static void MapAgentRunApi(
        this RouteGroupBuilder api,
        IAgentRunService runs,
        IAgentRunnerClient runners)
    {
        api.MapGet("/runners", (HttpRequest request) =>
            {
                var result = runners.Registrations();
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var revision = Hash(result.Payload!);
                var notModified = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (notModified != null) return notModified;
                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(result.Payload);
            })
            .WithName("ListAgentRunners")
            .WithSummary("List paired agent runners")
            .Produces<IReadOnlyList<AgentRunnerRegistration>>()
            .WithRevisionedReadMetadata();

        api.MapPost("/runners/pair", async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<PairAgentRunnerRequest>(request, cancellationToken);
                if (error != null) return error;
                if (!Uri.TryCreate(input!.Endpoint, UriKind.Absolute, out var endpoint))
                    return ApiResults.Failure("invalid_runner_pairing", "Runner endpoint is invalid.", request.Path);
                var result = await runners.Pair(new AgentRunnerPairingRequest(
                    endpoint, input.RunnerId, input.TlsFingerprint, input.PairingCode, input.ReplaceExisting),
                    cancellationToken);
                return result.Success
                    ? Results.Created($"/api/v1/runners/{Uri.EscapeDataString(result.Payload!.RunnerId)}", result.Payload)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("PairAgentRunner")
            .WithSummary("Pair an agent runner")
            .WithClientHeaderMetadata()
            .Accepts<PairAgentRunnerRequest>("application/json")
            .Produces<AgentRunnerRegistration>(StatusCodes.Status201Created)
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        api.MapGet("/runners/{runnerId}", (HttpRequest request, string runnerId) =>
            {
                var result = runners.Registration(runnerId);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var revision = Hash(result.Payload!);
                var notModified = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (notModified != null) return notModified;
                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(result.Payload);
            })
            .WithName("GetAgentRunner")
            .WithSummary("Get a paired agent runner")
            .Produces<AgentRunnerRegistration>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/runners/{runnerId}/status", async (
                HttpRequest request, string runnerId, CancellationToken cancellationToken) =>
            {
                var registration = runners.Registration(runnerId);
                if (!registration.Success)
                    return ApiResults.Failure(registration.ErrorCode, registration.Message, request.Path);
                var health = await runners.Health(runnerId, cancellationToken);
                if (!health.Success) return ApiResults.Failure(health.ErrorCode, health.Message, request.Path);
                var capabilities = await runners.Capabilities(runnerId, cancellationToken);
                if (!capabilities.Success)
                    return ApiResults.Failure(capabilities.ErrorCode, capabilities.Message, request.Path);
                var revision = Hash(new
                {
                    Registration = registration.Payload,
                    Health = health.Payload,
                    Capabilities = capabilities.Payload,
                });
                var notModified = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (notModified != null) return notModified;
                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(new AgentRunnerStatusResponse(registration.Payload!, health.Payload!,
                    capabilities.Payload!, revision));
            })
            .WithName("GetAgentRunnerStatus")
            .WithSummary("Get agent runner health, capacity, and capabilities")
            .Produces<AgentRunnerStatusResponse>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        api.MapPost("/runners/{runnerId}/rotate", async (
                HttpRequest request, string runnerId, CancellationToken cancellationToken) =>
            {
                var (_, error) = await ApiJsonRequest.Read<AgentRunActionRequest>(request, cancellationToken);
                if (error != null) return error;
                var result = await runners.Rotate(runnerId, cancellationToken);
                return result.Success
                    ? Results.Ok(result.Payload)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("RotateAgentRunnerCredential")
            .WithSummary("Rotate the local credential for an agent runner")
            .WithClientHeaderMetadata()
            .Accepts<AgentRunActionRequest>("application/json")
            .Produces<AgentRunnerRegistration>()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        api.MapDelete("/runners/{runnerId}", async (
                HttpRequest request, string runnerId, CancellationToken cancellationToken) =>
            {
                var result = await runners.Revoke(runnerId, cancellationToken);
                return result.Success
                    ? Results.NoContent()
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("RevokeAgentRunner")
            .WithSummary("Revoke and remove an agent runner")
            .WithClientHeaderMetadata()
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        api.MapPost("/runs/preflight", async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<AgentRunPreflightRequest>(request, cancellationToken);
                if (error != null) return error;
                var result = await runs.Preflight(new AgentRunSelection(input!.TaskId, input.RunnerId,
                    input.ProfileId, input.ProviderId, input.ModelId, input.EffortId), cancellationToken);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                ApiPreconditions.SetETag(request.HttpContext.Response,
                    result.Payload!.Revision ?? Hash(result.Payload));
                return Results.Ok(result.Payload);
            })
            .WithName("PreflightAgentRun")
            .WithSummary("Validate and persist an immutable agent run draft")
            .WithClientHeaderMetadata()
            .Accepts<AgentRunPreflightRequest>("application/json")
            .Produces<AgentRunPreflightResult>()
            .WithResponseETagMetadata(StatusCodes.Status200OK)
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPost("/runs/{runId}/start", async (
                HttpRequest request, string runId, CancellationToken cancellationToken) =>
            {
                var (_, error) = await ApiJsonRequest.Read<AgentRunActionRequest>(request, cancellationToken);
                if (error != null) return error;
                var revision = ReadStrongIfMatch(request);
                if (!revision.Success) return ApiResults.Problem(revision.Status, revision.ErrorCode,
                    revision.Message, request.Path);
                var result = await runs.Start(runId, revision.Value!, cancellationToken);
                if (result.Success)
                    ApiPreconditions.SetETag(request.HttpContext.Response,
                        result.Payload!.Run.SpecificationHash);
                return result.Success
                    ? result.Payload!.Disposition == AgentRunRemoteStartDisposition.New
                        ? Results.Accepted($"/api/v1/runs/{Uri.EscapeDataString(runId)}", result.Payload)
                        : Results.Ok(result.Payload)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("StartAgentRun")
            .WithSummary("Start a persisted immutable agent run draft")
            .WithClientHeaderMetadata()
            .Accepts<AgentRunActionRequest>("application/json")
            .Produces<AgentRunRemoteStart>(StatusCodes.Status202Accepted)
            .Produces<AgentRunRemoteStart>()
            .WithRevisionedMutationMetadata(StatusCodes.Status202Accepted)
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        api.MapGet("/runs", async (HttpRequest request, string runnerId, string scope = "active",
                int limit = 100, string? cursor = null, CancellationToken cancellationToken = default) =>
            {
                if (scope != "active")
                    return ApiResults.Failure("invalid_run_scope", "Only active runs can be listed.", request.Path);
                var result = await runs.ActiveRuns(runnerId, limit, cursor, cancellationToken);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var revision = Hash(result.Payload!);
                var notModified = ApiPreconditions.EvaluateIfNoneMatch(request, revision);
                if (notModified != null) return notModified;
                ApiPreconditions.SetETag(request.HttpContext.Response, revision);
                return Results.Ok(result.Payload);
            })
            .WithName("ListActiveAgentRuns")
            .WithSummary("List active runs on an agent runner")
            .Produces<AgentRunnerRunPage>()
            .WithRevisionedReadMetadata();

        api.MapGet("/runs/{runId}", async (
                HttpRequest request, string runId, CancellationToken cancellationToken) =>
            {
                var result = await runs.Inspect(runId, cancellationToken);
                if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var notModified = ApiPreconditions.EvaluateIfNoneMatch(request, result.Payload!.Revision);
                if (notModified != null) return notModified;
                ApiPreconditions.SetETag(request.HttpContext.Response, result.Payload.Revision);
                return Results.Ok(result.Payload);
            })
            .WithName("GetAgentRun")
            .WithSummary("Inspect an agent run and local task drift")
            .Produces<AgentRunInspection>()
            .WithRevisionedReadMetadata()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/runs/{runId}/events", async (HttpRequest request, string runId,
                long afterSequence = 0, int limit = 100, CancellationToken cancellationToken = default) =>
            {
                var result = await runs.Events(runId, afterSequence, limit, cancellationToken);
                return result.Success
                    ? Results.Ok(result.Payload)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("ListAgentRunEvents")
            .WithSummary("Replay durable agent run events")
            .Produces<AgentRunEventPage>()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/runs/{runId}/events/stream", StreamEvents)
            .WithName("StreamAgentRunEvents")
            .WithSummary("Stream and resume durable agent run events")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapPost("/runs/{runId}/cancel", async (
                HttpRequest request, string runId, CancellationToken cancellationToken) =>
            {
                var (_, error) = await ApiJsonRequest.Read<AgentRunActionRequest>(request, cancellationToken);
                if (error != null) return error;
                var result = await runs.Cancel(runId, cancellationToken);
                return result.Success
                    ? Results.Ok(result.Payload)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("CancelAgentRun")
            .WithSummary("Request cancellation of an agent run")
            .WithClientHeaderMetadata()
            .Accepts<AgentRunActionRequest>("application/json")
            .Produces<AgentRunCancellation>()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/runs/{runId}/artifacts", async (
                HttpRequest request, string runId, CancellationToken cancellationToken) =>
            {
                var result = await runs.Artifacts(runId, cancellationToken);
                return result.Success
                    ? Results.Ok(result.Payload)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("ListAgentRunArtifacts")
            .WithSummary("List agent run artifact metadata")
            .Produces<IReadOnlyList<AgentRunArtifact>>()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        api.MapGet("/runs/{runId}/artifacts/{artifactId}", async (
                HttpRequest request, string runId, string artifactId, CancellationToken cancellationToken) =>
            {
                var result = await runs.Artifact(runId, artifactId, cancellationToken);
                return result.Success
                    ? Results.Ok(result.Payload)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName("GetAgentRunArtifact")
            .WithSummary("Get agent run artifact metadata")
            .Produces<AgentRunArtifact>()
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        return;

        async Task StreamEvents(HttpContext context, string runId, long afterSequence = 0)
        {
            var result = await runs.OpenEventStream(runId, afterSequence, context.RequestAborted);
            if (!result.Success)
            {
                await ApiResults.Failure(result.ErrorCode, result.Message, context.Request.Path).ExecuteAsync(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.WriteAsync("retry: 3000\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await using var stream = result.Payload!;
            try
            {
                await foreach (var message in stream.ReadAllAsync(context.RequestAborted))
                {
                    if (message.Event != null)
                    {
                        await WriteSse(context.Response, "run-event", message.Event.Sequence.ToString(),
                            message.Event, context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                        var advanced = await runs.AdvanceSequence(runId, message.Event.Sequence);
                        if (!advanced.Success) return;
                    }
                    else if (message.End != null)
                    {
                        await WriteSse(context.Response, "stream-end", null, message.End,
                            context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
            }
            catch (AgentRunnerStreamException)
            {
            }
        }
    }

    private static async Task WriteSse(
        HttpResponse response,
        string eventName,
        string? id,
        object payload,
        CancellationToken cancellationToken)
    {
        if (id != null) await response.WriteAsync($"id: {id}\n", cancellationToken);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, StreamJson)}\n\n", cancellationToken);
    }

    private static IfMatchValue ReadStrongIfMatch(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("If-Match", out var values) || string.IsNullOrWhiteSpace(values))
            return new IfMatchValue(false, StatusCodes.Status428PreconditionRequired, "precondition_required",
                "An If-Match header containing the preflight revision is required.", null);
        var value = values.ToString().Trim();
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) || value.Length < 2 ||
            value[0] != '"' || value[^1] != '"' || value[1..^1].Contains('"'))
            return new IfMatchValue(false, StatusCodes.Status412PreconditionFailed, "precondition_failed",
                "If-Match must contain the strong ETag returned by preflight.", null);
        return new IfMatchValue(true, StatusCodes.Status200OK, string.Empty, string.Empty, value[1..^1]);
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, StreamJson)))
            .ToLowerInvariant();

    private sealed record IfMatchValue(bool Success, int Status, string ErrorCode, string Message, string? Value);
}
