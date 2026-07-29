---
id: AGENT-0016
title: Design runner-global operational reporting
track: AGENT
milestone: agent-runner-evolution
dependsOn:
- AGENT-0012
createdAt: 2026-07-28T18:44:20.6294680Z
modifiedAt: 2026-07-29T12:08:46.9668990Z
---

## Goal

Design runner-global operational reporting without putting host-wide events into arbitrary per-run journals or replacing their per-run sequence numbers.

## Investigation

- Identify operational signals that are not run events, including storage pressure, runner shutdown, configuration changes, degraded runtime readiness, protocol upgrades, and capacity changes.
- Decide which signals belong in authenticated health and capability snapshots and which require a durable runner-global event journal.
- If a global journal is justified, define independent sequencing, replay, retention, SSE backpressure, authentication, and reconnect behavior.
- Keep `run.*` and the existing per-run event streams unchanged.
- Decide whether a future `system.*` namespace is appropriate only after its transport and ownership model are clear.
- Define sanitization and visibility rules so global events cannot leak host paths, usernames, credentials, or unrelated machine state.
- Consider how PM distinguishes transient runner unavailability from a durable shutdown or degraded-readiness event.

## Acceptance criteria

- The design does not overload a run ID with unrelated host events.
- Existing per-run sequences remain independent and unchanged.
- PM can present actionable runner health without treating connectivity loss as run failure.
- Any proposed event stream has explicit persistence, replay, backpressure, retention, and compatibility semantics.
- The result either proposes a bounded implementation task or records why snapshot-based health is sufficient for the current runner.