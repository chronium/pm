---
id: AGENT-0009
title: Implement runner registrations and signed transport client
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
- AGENT-0004
- AGENT-0005
- AGENT-0014
createdAt: 2026-07-27T06:57:01.7833670Z
modifiedAt: 2026-07-29T08:04:12.4782660Z
---

## Goal

Give PM a secure, provider-neutral transport adapter for registering, pairing with, and communicating with agent runners. Keep project-aware run orchestration and Angular-facing APIs out of this task.

## Implementation

- Store runner registrations, TLS pins, client identifiers, and signing credentials in OS user configuration outside `.pm/`.
- Implement explicit pairing with runner-ID and TLS-fingerprint verification. Never automatically trust certificate replacement.
- Add a runner client abstraction implementing:
  - capability and health discovery;
  - idempotent run submission;
  - run inspection and active-run paging;
  - event replay and authenticated SSE consumption;
  - cancellation;
  - artifact metadata retrieval;
  - credential rotation and revocation where required by protocol 1.0.
- Implement protocol 1.0 request signing over exact request bytes, raw path and query, timestamp, scoped nonce, client ID, and protocol version.
- Use the server-time response defined by AGENT-0014 to surface actionable clock-skew failures without weakening TLS or signature verification.
- Preserve runner authority after durable acceptance and expose reconnect primitives using per-run event sequences.
- Keep the client independent from Angular, PM task/milestone/wiki concepts, and any specific agent provider.
- Do not persist pairing secrets, signatures, private keys, or raw authenticated payloads in logs or `.pm/`.

## Acceptance criteria

- A PM installation can pair with one runner and reconnect after process restart without exposing private credentials.
- TLS pinning rejects an unexpected certificate and requires explicit re-pairing.
- Identical run submissions are idempotent and conflicting run IDs remain conflicts.
- The client can reconnect from a durable event sequence without losing or duplicating semantic events.
- Runner unavailability is reported without rewriting remote run state.
- Unknown additive response fields remain compatible while unsupported security-critical values fail closed.
- A transport-level integration harness can communicate with the real Linux runner independently of Angular and project orchestration.

## Validation

- Add client tests against a fake authenticated HTTPS runner for pairing, signing, exact body bytes, nonce replay, clock skew, TLS replacement, capabilities, duplicate starts, replay, SSE reconnect, backpressure disconnect, cancellation, artifacts, rotation, revocation, and secret omission.
- Add an opt-in Tailscale/Linux transport smoke using `codex@agent-box`.
- Run the .NET build and test suite.

## Notes

- 2026-07-29 08:04 UTC - Implemented the provider-neutral runner registry and signed HTTPS client, including explicit TLS pinning, exact-byte request signatures, health/capability discovery, idempotent starts, inspection, paging, replayable SSE, cancellation, artifacts, credential rotation, and revocation. Added the `pm runner` management CLI, private OS-user persistence, fake HTTPS integration coverage, and Linux operator documentation. The real Mac-to-Arch smoke paired over Tailscale, persisted and queried the runner, rotated credentials, authenticated with the successor, and revoked cleanly. That smoke exposed a Node 26 TLS 1.3-only interoperability failure in .NET 10 AppleCrypto; the runner now permits TLS 1.2 and 1.3. Validation passed with 318 .NET tests and 51 agent-host tests; the optional Podman image integration remained skipped because its environment variable was not set.