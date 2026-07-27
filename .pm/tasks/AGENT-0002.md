---
id: AGENT-0002
title: Add a restricted PM MCP profile for agent runs
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
createdAt: 2026-07-27T06:57:00.1964900Z
modifiedAt: 2026-07-27T09:32:07.3753910Z
---

## Goal

Let Codex inspect PM context and append implementation notes inside an isolated checkout without granting authoritative project-management control.

## Implementation

- Add an explicit `run-worker` capability profile to `pm mcp`.
- Allow project/task/wiki retrieval, search, outlines, next-task context, and project validation.
- Allow `append_task_note` for durable implementation observations.
- Deny task movement, removal, status/track/milestone configuration, project creation, task creation, full task/wiki replacement, wiki rename/delete, and bulk mutations.
- Make denied tools absent from the advertised schema where practical; otherwise fail closed with a stable authorization error.
- Let runner startup require this profile and fail if the restricted MCP server cannot initialize.
- Document that authoritative completion remains a PM control-plane action after validation or review.

## Acceptance criteria

- A run worker can read its task, dependencies, and wiki context and append a note.
- It cannot mark a task done or mutate project configuration.
- Normal MCP mode remains backward compatible.
- Capability selection cannot be overridden by repository-controlled configuration.

## Validation

- Add MCP schema and authorization tests for allowed and denied tools.
- Run the .NET build and tests.

## Notes

- 2026-07-27 09:32 UTC - Implemented the `run-worker` MCP profile with trusted `--profile run-worker --task-id <TASK-ID>` startup options, an explicit 14-tool read-oriented allowlist, assigned-task-only `append_task_note` authorization, fail-closed project/task startup validation, normal-profile schema compatibility, and README guidance on the control-plane authority boundary. Validation: `dotnet build PM.slnx -m:1 --no-restore` succeeded; `dotnet test PM.slnx -m:1 --no-restore` passed 337 tests; a live stdio handshake advertised only the restricted allowlist; missing-task startup exited with code 2; `pm doctor` passed.