# Agent runner HTTPS transport 1.0

The runner exposes HTTPS only on an explicitly configured non-wildcard interface. Pairing is the only route that does not require a signed PM identity request, and it requires a short-lived one-use code displayed locally beside the stable runner ID and runner certificate fingerprint.

## Pairing

`POST /v1/pairing/complete` accepts a pairing code, the client's supported protocol versions, and the existing PM P-256 identity. Before submitting the code, the local pairing command must display the stable runner ID, one-use code, expiry, and `sha256:<hex>` TLS certificate fingerprint together. The operator must verify that the runner ID and fingerprint match the PM pairing presentation. A successful response selects protocol `1.0`, consumes the code, registers the single client, and returns capabilities. Codes expire after ten minutes and lock after five invalid attempts.

## Authenticated requests

Authenticated requests use these headers:

- `PM-Runner-Client-Id`
- `PM-Runner-Timestamp`, as Unix seconds
- `PM-Runner-Nonce`
- `PM-Runner-Signature`, as base64url P-256 IEEE-P1363 bytes
- `PM-Runner-Protocol-Version`

The signed UTF-8 value is:

```text
pm-runner-auth-v1
<UPPERCASE METHOD>
<RAW PATH AND QUERY>
<PROTOCOL VERSION>
<TIMESTAMP>
<NONCE>
<CLIENT ID>
<LOWERCASE HEX SHA-256 OF EXACT REQUEST BODY BYTES>
```

The body hash covers the exact bytes received after the HTTP message framing is removed. Implementations must not decode text, parse or reserialize JSON, normalize Unicode, convert line endings, trim whitespace, or otherwise transform the body before hashing it. An empty request body hashes the zero-length byte sequence.

The runner accepts five minutes of clock skew and durably rejects a reused `(clientId, nonce)` tuple. A nonce record is persisted before the authenticated operation proceeds and survives runner restarts. It remains retained through the final server second in which the signed timestamp could pass the skew check; an implementation may remove it only after that signature is necessarily outside the accepted window. Expired records may be pruned lazily. Reusing the same nonce under another client ID is a distinct tuple, although protocol 1.0 registers only one client at a time.

If a syntactically valid integer timestamp is outside the accepted skew window, the runner returns the same generic `401 unauthorized` body as other authentication failures and adds `PM-Runner-Server-Time` containing the runner's current Unix time in seconds. Malformed timestamps and other authentication failures do not include this header. The runner verifies the signature before reporting an incompatible authenticated protocol.

## Discovery and credential lifecycle

- `GET /v1/health` distinguishes authenticated runner reachability from run state.
- `GET /v1/capabilities` returns `AgentRunnerCapabilities`. OCI discovery reports the installed
  engine and its rootless, cgroup, seccomp, and LSM state; protocol 1.0 execution requires rootless
  Podman with cgroup v2/systemd and seccomp.
- `POST /v1/client/rotate` requires the old request signature and a new-key proof over `pm-runner-rotation-v1`, runner ID, old client ID, new client ID, new public key, and request nonce.
- `DELETE /v1/client` revokes the current client.

Replacing the TLS certificate requires explicit re-pairing in protocol 1.0.

## Run commands

All run routes require the authenticated request headers above. A submitted body is the protocol 1.0 `RunRequest`, including its canonical specification hash.

- `POST /v1/runs` accepts a run. A new run returns `202`; an identical retry returns the existing run with `200`. Reusing a run ID with another specification hash returns `409 run_id_conflict`. Unsupported runner, provider, model, effort, or runtime-profile selections return a stable capability error without creating a run.
- `GET /v1/runs/{runId}` returns the immutable specification, current durable state, and nullable provider thread ID recorded after agent startup.
- `GET /v1/runs?scope=active&limit=100&cursor=...` lists non-terminal runs in acceptance order. The cursor is opaque to clients. The default limit is 100 and the maximum is 500.
- `POST /v1/runs/{runId}/cancel` journals the request. A queued run becomes cancelled immediately. An active run returns `202` and becomes terminal only after its processor stops. If completion wins the race, the completed state remains authoritative.
- `GET /v1/runs/{runId}/artifacts` and `GET /v1/runs/{runId}/artifacts/{artifactId}` return validated artifact metadata. Protocol 1.0 does not transfer artifact bytes.

Once a run is durably accepted, the runner owns its execution lifecycle. Client disconnects, PM process shutdown, SSE disconnection, and later connectivity loss do not cancel, invalidate, or return ownership of the run. Cancellation requires the authenticated cancellation command. The runner recovers accepted work from its durable state after restart.

## Event history

`GET /v1/runs/{runId}/events?afterSequence=N&limit=100` returns durable events strictly after `N`, a `nextAfterSequence` cursor, `hasMore`, and the run's terminal flag. Limits use the same 100 default and 500 maximum as active-run pages.

`GET /v1/runs/{runId}/events/stream?afterSequence=N` opens a server-sent event stream. The raw path and query, including `afterSequence`, are covered by the request signature; the runner does not trust `Last-Event-ID` as an unsigned replacement.

The stream sends:

```text
retry: 2000

id: 42
event: run-event
data: { durable RunEvent JSON }

: heartbeat

event: stream-end
data: { terminal state and last sequence }
```

Events are committed to SQLite before subscribers are notified. Reconnecting with the last durably processed sequence replays every later event. Clients must deduplicate by `(runId, sequence)`. Heartbeats are comments and `stream-end` is a non-durable transport signal; neither consumes an event sequence. A terminal stream closes after replay and `stream-end`.

The host bounds concurrent streams, writes one event at a time, waits for socket drain, and disconnects a client that remains backpressured. Disconnecting a stream never cancels or otherwise changes its run.

## Event envelopes

Every durable event carries `protocolVersion`, `runId`, a positive per-run `sequence`, UTC `timestamp`, namespaced `type`, optional lifecycle `state`, human summary, and extensible JSON `data`. Event types use the lowercase grammar `^[a-z][a-z0-9_-]*\.[a-z0-9][a-z0-9._-]*$`. Protocol 1.0 reserves these standard type families:

- `run.*` for acceptance, lifecycle, and cancellation.
- `runner.*` for execution-host messages.
- `runtime.*` for container or process runtime activity.
- `agent.*` for provider output and agent lifecycle.
- `command.*` for toolchain command output and results.
- `mcp.*` for repository-local PM MCP activity.
- `validation.*` for validation steps and outcomes.
- `artifact.*` for artifact collection and metadata.

The standard families are not an exhaustive allowlist. Clients must retain, replay, and display an unknown event type generically when it follows the event-type grammar and its envelope is otherwise valid.

## Forward compatibility

Readers must ignore unknown additive object fields and unknown members inside extensible event `data`. They must preserve event `data` as an opaque JSON value when they journal or relay it. Unknown valid namespaced event types use the generic event behavior above.

Readers must reject unknown values that select authentication, protocol, lifecycle, runtime, security, network, or output semantics. In particular, an unknown protocol version, lifecycle state, network mode, output mode, container security value, or authentication scheme must never silently fall back to a known behavior. Additive compatibility does not permit weakening validation of required fields, canonical hashes, or security policy.

Before persistence, summaries and data have unsafe terminal controls removed, common credential forms and sensitive fields redacted, collection depth and size bounded, and oversized payloads replaced by a redaction marker. Raw output is never journaled first and sanitized later.

The Codex adapter runs as a one-shot worker inside the selected runtime and translates SDK activity into these provider-neutral envelopes. Thread IDs are stored only for diagnostics and future continuation; Codex's local transport is not exposed as the PM runner protocol. Command output is emitted incrementally, file paths are workspace-relative, and MCP events omit raw tool arguments and results.
