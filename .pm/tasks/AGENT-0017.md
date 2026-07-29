---
id: AGENT-0017
title: Add PM run orchestration and control-plane JSON APIs
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0008
- AGENT-0009
createdAt: 2026-07-28T21:13:43.2481800Z
modifiedAt: 2026-07-28T21:13:50.0835610Z
---

## Goal

Build the project-aware PM application service and versioned JSON API that turn the provider-neutral runner client into a complete control-plane workflow for Angular.

## Implementation

- Add an application-level run service that builds immutable run specifications from the current project, selected task, committed Git HEAD, selected runner/profile, and task revision.
- Preflight repository state, remote reachability, base commit availability, task revision, runner health, protocol compatibility, installed capabilities, selected profile, and current capacity.
- Reject dirty or unpushed/unreachable bases according to the v1 committed-base policy.
- Persist a local non-authoritative run cache outside public PM project artifacts so PM can restart and reconnect from the last processed event sequence while treating runner state as authoritative.
- Reconcile remote run state and task-revision drift without mutating authoritative PM task status.
- Expose versioned JSON endpoints for:
  - runner registrations, pairing state, reachability, and safe capabilities;
  - run preflight and immutable specification preview;
  - start, inspect, active runs, event replay/streaming, cancellation, and artifacts.
- Proxy runner communication through PM; Angular must never receive runner signing credentials or communicate with the Linux runner directly.
- Use existing API revision, error, OpenAPI, and generated Angular type conventions.
- Keep artifacts metadata-only until a later protocol explicitly defines byte retrieval.

## Acceptance criteria

- PM can submit a valid task run, restart, reconnect from its last event sequence, and present authoritative remote state.
- Editing the task after submission produces visible task-revision drift without changing the immutable run.
- Dirty, unreachable, incompatible, offline, or capacity-constrained preflight failures are actionable and create no run.
- Starting the same immutable request twice cannot create duplicate execution.
- Runner unavailability does not rewrite a running job as failed.
- No run action marks a PM task complete automatically.
- API responses expose no pairing codes, signatures, private keys, raw credential values, or runner-internal host paths.
- A Mac-to-Linux API integration test can submit a run and observe replayable lifecycle and artifact metadata without Angular.

## Validation

- Add application-service and endpoint tests using a fake runner client.
- Cover specification construction, Git preflight, duplicate starts, restart/reconnect, task drift, runner offline, cancellation, event replay, artifacts, API revisions, and secret omission.
- Add an opt-in integration smoke against the runner completed by AGENT-0008 and AGENT-0009.
- Run the .NET build/tests and regenerate checked OpenAPI and Angular API types.