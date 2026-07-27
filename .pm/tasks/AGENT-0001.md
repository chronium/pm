---
id: AGENT-0001
title: Define the agent run domain and runner protocol
track: AGENT
milestone: agent-runs
dependsOn:
- PM-0067
createdAt: 2026-07-27T06:56:59.9661710Z
modifiedAt: 2026-07-27T07:02:56.1463600Z
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