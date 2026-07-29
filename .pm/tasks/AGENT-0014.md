---
id: AGENT-0014
title: Tighten protocol 1.0 interoperability semantics
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0004
- AGENT-0005
createdAt: 2026-07-28T18:44:20.1114510Z
modifiedAt: 2026-07-29T07:19:37.9285340Z
---

## Goal

Remove the remaining protocol 1.0 ambiguities before the PM control-plane client is implemented so independent clients and runners produce identical authentication, compatibility, and lifecycle behavior.

## Implementation

- Define the signed body component as the lowercase hexadecimal SHA-256 of the exact request body bytes, without parsing, normalization, reserialization, or newline conversion.
- Define durable nonce uniqueness and replay rejection over the `(clientId, nonce)` tuple, including retention and expiry behavior.
- Add a protocol-prefixed server-time response header for requests rejected because their timestamp is outside the accepted skew window while retaining the generic authenticated-error body.
- Make the pairing presentation requirements normative: show the stable runner ID beside the TLS fingerprint and one-use code.
- State explicitly that, after durable acceptance, the runner owns the run lifecycle; client, SSE, and PM process disconnects do not cancel or invalidate it.
- Define forward-compatible parsing:
  - unknown additive object fields and event-data members are ignored;
  - unknown valid namespaced event types remain replayable and displayable generically;
  - unknown values that select authentication, security, runtime, or lifecycle semantics are rejected.
- Keep event sequence numbers per-run and preserve persistence-before-notification and reconnect semantics.
- Update the TypeScript host, contract documentation, and tests. Update shared .NET protocol behavior where applicable.

## Acceptance criteria

- Authentication tests prove body hashes change for byte-level whitespace, line-ending, and encoding differences.
- Reusing one nonce for the same client is rejected durably, while the documented scope is unambiguous.
- A skew rejection includes the documented server time without weakening generic authentication failures.
- Pairing output includes runner ID and certificate fingerprint.
- The RFC explicitly assigns accepted-run lifecycle ownership to the runner.
- Compatibility tests cover ignored additive fields, accepted future event types, and rejected unknown security-critical discriminator values.
- AGENT-0009 can implement the PM client without inferring undocumented behavior.

## Notes

- 2026-07-29 07:19 UTC - Implemented protocol 1.0 interoperability tightening across the TypeScript runner, shared .NET contracts, and RFC. Exact request bytes now define signed body hashes; durable nonce expiry covers the final valid skew second; skew rejections expose PM-Runner-Server-Time with a generic body; pairing presentation is tested; valid future event namespaces are replayable while unknown semantic discriminators remain rejected; and accepted-run ownership is normative. Validation: agent-host npm run validate passed (51 passed, 1 expected Linux Podman skip); dotnet build PM.slnx -m:1 --no-restore passed with two existing NU1510 warnings; dotnet test PM.slnx -m:1 --no-restore --no-build passed (312 tests).