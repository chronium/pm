---
id: AGENT-0005
title: Implement idempotent run commands and replayable event streaming
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
- AGENT-0003
- AGENT-0004
createdAt: 2026-07-27T06:57:00.8799510Z
modifiedAt: 2026-07-27T10:46:18.6627070Z
---

## Goal

Expose the stable runner protocol through HTTPS commands, journal-backed reads, and reconnectable server-sent events.

## Implementation

- Implement start, inspect, list-active, cancel, artifact metadata, and event-history endpoints.
- Make start idempotent by run ID and specification hash; reject conflicting reuse of an existing ID.
- Provide paged event reads using `afterSequence` and an SSE stream using the same replay cursor.
- Journal events before publishing them to connected clients.
- Apply bounded per-client buffering, heartbeat events, backpressure handling, reconnect guidance, and deterministic terminal-state closure.
- Normalize runner, runtime, agent, command, MCP, validation, and artifact events into versioned envelopes.
- Redact secrets and unsafe control sequences before persistence, not only before display.

## Acceptance criteria

- Disconnecting PM never interrupts an accepted run.
- Reconnecting after sequence N returns every later durable event exactly once after client deduplication.
- Sending the same start command twice creates one run.
- Slow clients cannot exhaust runner memory.

## Validation

- Test disconnect/reconnect, replay boundaries, duplicate starts, conflicting hashes, cancellation races, backpressure, and restart recovery.
- Run runner formatting, strict checks, and tests.

## Notes

- 2026-07-27 10:46 UTC - Implemented the authenticated protocol 1.0 run command surface with idempotent submission, capability checks, active-run paging, inspection, cancellation, artifact metadata, paged history, and replayable SSE. Durable events are sanitized before persistence and published only after commit; streams include heartbeats, terminal closure, capacity limits, and backpressure disconnects. Added cancellation-race, restart/idempotency, reconnect, replay-boundary, capacity, backpressure, sanitization, and HTTPS integration coverage. Validation: `socket npm ci` reported no new risks; `npm run validate` passed 25 tests; `dotnet build PM.slnx -m:1 --no-restore` succeeded; `dotnet test PM.slnx -m:1 --no-restore` passed 337 tests; PM project validation passed.