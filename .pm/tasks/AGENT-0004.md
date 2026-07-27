---
id: AGENT-0004
title: Implement runner pairing, HTTPS, and capability discovery
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0003
createdAt: 2026-07-27T06:57:00.6516300Z
modifiedAt: 2026-07-27T06:57:21.3302920Z
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