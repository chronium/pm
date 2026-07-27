---
id: AGENT-0009
title: Add PM runner registrations and control-plane run APIs
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
- AGENT-0004
- AGENT-0005
createdAt: 2026-07-27T06:57:01.7833670Z
modifiedAt: 2026-07-27T06:57:21.3656860Z
---

## Goal

Let the existing PM server register runners, submit immutable runs, mirror their state, and proxy events and artifacts to Angular.

## Implementation

- Store paired runner registrations and credentials in OS user configuration outside `.pm/`.
- Add a runner client abstraction implementing capability discovery, idempotent start, inspect, replay, SSE consumption, cancellation, and artifact retrieval.
- Add an application-level run service that builds specifications from the current project, task, committed HEAD, selected runner/profile, and task revision.
- Reject dirty or unpushed/unreachable bases according to the v1 committed-base policy.
- Persist a local non-authoritative run cache sufficient for PM restart and reconnect while treating runner state as authoritative.
- Expose versioned JSON endpoints for runner settings, preflight, start, inspect, events, cancel, and artifacts.
- Use existing API revision/error conventions and never expose pairing secrets to Angular.

## Acceptance criteria

- Angular never communicates with the Linux runner directly.
- PM can restart and reconnect to an accepted run from the last event sequence.
- Editing the task after submission produces a visible task-revision drift state.
- Runner unavailability does not rewrite a running job as failed.
- No run action mutates authoritative task status automatically.

## Validation

- Add application and API tests using a fake runner server.
- Cover pairing state, preflight failures, duplicate starts, reconnect, task drift, runner offline, cancellation, and secret omission.
- Run the .NET build and tests and regenerate checked API types.