# Agent runner HTTPS transport 1.0

The runner exposes HTTPS only on an explicitly configured non-wildcard interface. Pairing is the only route that does not require a signed PM identity request, and it requires a short-lived one-use code displayed locally beside the runner certificate fingerprint.

## Pairing

`POST /v1/pairing/complete` accepts a pairing code, the client's supported protocol versions, and the existing PM P-256 identity. The operator must verify the displayed `sha256:<hex>` TLS certificate fingerprint before submitting the code. A successful response selects protocol `1.0`, consumes the code, registers the single client, and returns capabilities. Codes expire after ten minutes and lock after five invalid attempts.

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
<LOWERCASE SHA-256 BODY HASH>
```

The runner accepts five minutes of clock skew and durably rejects a reused nonce. It verifies the signature before reporting an incompatible authenticated protocol.

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

The host implemented by AGENT-0005 intentionally uses a queue-only execution controller. It durably accepts commands, but production runs remain queued until a configured runtime and agent driver are introduced by later slices.

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

Every durable event carries `protocolVersion`, `runId`, a positive per-run `sequence`, UTC `timestamp`, namespaced `type`, optional lifecycle `state`, human summary, and extensible JSON `data`. Protocol 1.0 reserves these type families:

- `run.*` for acceptance, lifecycle, and cancellation.
- `runner.*` for execution-host messages.
- `runtime.*` for container or process runtime activity.
- `agent.*` for provider output and agent lifecycle.
- `command.*` for toolchain command output and results.
- `mcp.*` for repository-local PM MCP activity.
- `validation.*` for validation steps and outcomes.
- `artifact.*` for artifact collection and metadata.

Before persistence, summaries and data have unsafe terminal controls removed, common credential forms and sensitive fields redacted, collection depth and size bounded, and oversized payloads replaced by a redaction marker. Raw output is never journaled first and sanitized later.

The Codex adapter runs as a one-shot worker inside the selected runtime and translates SDK activity into these provider-neutral envelopes. Thread IDs are stored only for diagnostics and future continuation; Codex's local transport is not exposed as the PM runner protocol. Command output is emitted incrementally, file paths are workspace-relative, and MCP events omit raw tool arguments and results.
