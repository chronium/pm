---
id: AGENT-0004
title: Implement runner pairing, HTTPS, and capability discovery
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0003
createdAt: 2026-07-27T06:57:00.6516300Z
modifiedAt: 2026-07-27T10:23:48.9150980Z
---

## Goal

Secure direct PM-to-runner communication over a private Tailscale or trusted-network route.

## Implementation

- Bind HTTPS only to explicitly configured interfaces and never expose an unauthenticated listener.
- Implement one-time pairing for one PM client using a displayed short-lived code, pinned runner identity, and a client credential stored outside `.pm/`.
- Authenticate every command and event request; include replay protection and explicit protocol-version negotiation.
- Advertise runner identity, OS/architecture, Docker availability, supported runtime profiles, model/default capabilities, capacity, active slots, and health.
- Add credential rotation and revocation primitives sufficient for reinstalling or re-pairing one client.
- Keep TLS and pairing credentials out of repositories, event payloads, and logs.

## Acceptance criteria

- An unpaired client cannot inspect capabilities or runs.
- Replayed or expired authenticated requests fail closed.
- PM can detect incompatible protocol versions before submitting work.
- Runner reachability is distinguishable from an individual run state.

## Validation

- Test successful pairing, invalid codes, replay, expiry, revocation, TLS identity mismatch, and version mismatch.
- Run runner formatting, strict checks, and tests.

## Notes

- 2026-07-27 10:23 UTC - Implemented the TypeScript 7 runner transport boundary. Added explicit fail-closed `serve`, local `pair`, and local `revoke-client` commands; operator-provided TLS with fingerprint pinning; a validated external capability manifest and dynamic host capacity; one-client P-256 pairing using the existing PM identity; durable pairing challenges, replay nonces, rotation, and revocation in an owner-only credential SQLite database; authenticated health/capability/credential endpoints; and the `pm-runner-auth-v1` transport contract. Tests use real temporary OpenSSL certificates and cover pairing attempts/expiry, persistence, manifest validation, certificate mismatch, unauthenticated access, path/query binding, stale requests, replay, protocol mismatch, dual-proof rotation, revocation, and redacted logs. Validation: `socket npm ci` reported no new risks; elevated `npm run validate` passed formatting, TypeScript checks, build, and 18 tests; `dotnet build PM.slnx -m:1 --no-restore` succeeded; `dotnet test PM.slnx -m:1 --no-restore` passed 337 tests; direct pairing CLI smoke and `pm doctor` passed.