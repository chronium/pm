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
modifiedAt: 2026-07-27T06:57:21.3373270Z
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