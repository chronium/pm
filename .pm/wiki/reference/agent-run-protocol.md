---
title: Agent Run Protocol
createdAt: 2026-07-27T09:18:00.7240920Z
modifiedAt: 2026-07-27T10:46:42.2502110Z
---

Agent run protocol 1.0 defines the durable contract between PM's control plane and a runner. The contract is transport-neutral and is exposed by the TypeScript agent host over authenticated HTTPS with durable event replay.

The normative interoperability artifacts are the .NET contracts and validators in `PM.AgentRuns` together with the checked-in fixtures under `contracts/agent-runs/v1/`. This page explains their intended behavior.

## Immutable run request

A start request contains a canonical run specification and its lowercase SHA-256 hash. The specification freezes:

- run, project, and task identity
- the task revision visible when the run was requested
- repository remote and committed base SHA
- agent provider, model, effort, and prompt profile
- runner and runtime profile identity
- image, resource limits, network policy, validation steps, and patch output policy

Provider, model, effort, runtime-profile, and network-profile values are opaque advertised identifiers. PM selects an installed profile; it does not author container policy. The runner must compare the submitted profile ID, revision, and snapshot with its installed profile and reject a mismatch.

The specification hash excludes the hash property itself. Runtime profile revisions similarly exclude their own revision property. Canonical serialization fixes property order, timestamps, integer values, and ordered arrays so .NET and TypeScript implementations produce identical hashes.

A repeated start using the same run ID and specification hash is idempotent and represents the existing execution. Reusing a run ID with a different hash is a conflict.

## Lifecycle

The normal lifecycle is:

```text
requested
→ accepted
→ queued
→ preparing_workspace
→ starting_runtime
→ starting_agent
→ running
→ validating
→ collecting_artifacts
→ completed
```

`requested` belongs to the PM control plane. Once the runner durably accepts a request, its journal is authoritative for execution state. Any accepted nonterminal state may end as `failed` or `cancelled`; terminal states never transition again.

A cancellation request is an event, not an immediate state transition. The run becomes `cancelled` only after the active execution has stopped and the runner records the terminal event.

Runner reachability is separate from run progress. Losing the connection, closing PM, or restarting the Mac does not fail or cancel an accepted run.

## Durable events and replay

Every durable event has a protocol version, run ID, positive per-run sequence number, UTC timestamp, opaque event type, optional lifecycle state, summary, and extensible JSON data.

Sequences are contiguous and monotonically increasing. The runner journals an event before publishing it. A replay request with `afterSequence=N` returns events whose sequence is greater than `N`. Clients deduplicate by run ID and sequence. Transport heartbeats are not durable events and do not consume sequence numbers.

Protocol 1.0 fixes the core envelope and state-transition payload. Later runtime and Codex integration tasks may add event types and payload fields without turning Codex's own protocol into PM's runner protocol.

The HTTPS runner exposes paged history and replayable server-sent events using the same `afterSequence` cursor. The cursor is part of the signed path and query. Durable events are committed before live subscribers are notified; heartbeats and the terminal `stream-end` signal are transport-only and do not consume sequence numbers. Reconnecting clients deduplicate by run ID and sequence.

Protocol 1.0 event types use the `run.*`, `runner.*`, `runtime.*`, `agent.*`, `command.*`, `mcp.*`, `validation.*`, and `artifact.*` namespaces. Summaries and payloads are stripped of unsafe terminal controls, common credentials are redacted, and depth and payload size are bounded before persistence.

Run start is idempotent by run ID and canonical specification hash. Queued cancellation settles immediately; active cancellation is journaled first and settles after execution stops. Artifact routes expose validated metadata in this slice, with byte transfer deferred.

## Authority boundaries

- The runner journal is authoritative for execution state, events, and artifacts after acceptance.
- PM remains authoritative for project tasks, statuses, and completion.
- An agent may append implementation notes and report that it believes work is complete.
- An agent run cannot move its task to done, alter project configuration, or grant itself broader MCP capabilities.
- PM may show task-revision drift when the current task no longer matches the immutable submitted revision.

## V1 threat model

V1 is intended for a registered repository controlled by the operator. Repository contents and `.pm/` project data are public artifacts, but runner credentials, pairing credentials, Codex credentials, and private PM identity keys remain outside repositories.

Runs require a committed base SHA. The runner prepares an isolated credential-free checkout and retains host Git credentials outside the worker container. The worker must not receive the host home directory, unrelated checkouts, other run workspaces, runner configuration, Git push credentials, or the Docker socket.

The container is the unattended-agent security boundary. Runtime profiles are administrator-defined and impose explicit image, CPU, memory, process, disk, timeout, writable-volume, and network policies. V1 returns a bounded patch and artifacts; it does not create branches, push, merge, or accept dirty-worktree snapshots.

Model credentials are sensitive even inside a container. Authentication mode and credential exposure must be explicit, and stored events must be redacted before persistence. Artifacts and logs may still contain repository content and are not automatically committed or published.

## Disconnects and retention

The runner survives control-plane disconnects and service clients reconnect using the last durable sequence they observed. Accepted jobs remain inspectable after runner restart through the durable runner journal.

Retention is runner-owned policy. Artifact metadata crosses the protocol without exposing runner-local filesystem paths. Cleanup must not change the terminal result recorded in the journal.