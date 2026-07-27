---
id: AGENT-0001
title: Define the agent run domain and runner protocol
track: AGENT
milestone: agent-runs
dependsOn:
- PM-0067
createdAt: 2026-07-27T06:56:59.9661710Z
modifiedAt: 2026-07-27T09:19:58.3950960Z
---

## Goal

Establish the durable contracts for supervised Codex runs before implementing either side of the connection.

## Implementation

- Define immutable run specifications covering run ID, project/task identity, task revision, base commit, specification hash, agent settings, runtime profile, limits, network profile, validation, and output policy.
- Define the lifecycle state machine: requested, accepted, queued, preparing workspace, starting runtime, starting agent, running, validating, collecting artifacts, completed, failed, and cancelled.
- Define monotonically sequenced runner events, replay semantics, idempotent start behavior, cancellation, artifact metadata, capability advertisement, and protocol versioning.
- Specify authority boundaries: the runner journal is authoritative for execution; PM is authoritative for project task state; agents cannot mark tasks complete.
- Document the v1 threat model, trusted-repository assumption, committed-base requirement, credential boundaries, retention, and reconnect behavior.
- Add typed .NET protocol/domain models and focused serialization/state-transition tests without implementing transport.

## Acceptance criteria

- Duplicate start requests for one run ID cannot represent two executions.
- Invalid lifecycle transitions and mismatched specification hashes are rejected.
- Events can be resumed after an explicit sequence number.
- Model IDs and runtime profiles remain opaque advertised values rather than hard-coded enums.
- The specification clearly separates runner connectivity from run progress.

## Validation

- Run the .NET build and tests.
- Review the protocol against restart, disconnect, stale task, cancellation, and duplicate-request scenarios.

## Notes

- 2026-07-27 09:19 UTC - Implemented protocol 1.0 as a transport-neutral `PM.AgentRuns` domain. Added immutable specification and validated runtime-profile snapshots, canonical SHA-256 hashing, stable validation failures, idempotent start evaluation, lifecycle enforcement, durable event replay semantics, runner capability and artifact contracts, and language-neutral fixtures under `contracts/agent-runs/v1/`. Added the Agent Run Protocol wiki reference and linked it from Architecture. Validation: `dotnet build PM.slnx -m:1 --no-restore` succeeded; `dotnet test PM.slnx -m:1 --no-restore --no-build` passed 332 tests; PM project validation passed.