---
id: ISSUE-0017
title: Prevent linked MCP task moves from orphaning state refs
track: ISSUE
priority: urgent
createdAt: 2026-08-03T05:19:28.4345240Z
modifiedAt: 2026-08-03T05:19:36.7109550Z
---

## Problem

A linked-project `move_task` call can fail with an unstructured MCP error after removing the task's existing state reference. The task file remains, but an immediate `get_task` reports `missing_current_state`, and neither the source nor destination state contains the task.

Observed while moving Royale task `PHYS-012` from `todo` to configured state `doing` through a linked-project MCP selector. The state key was valid, so this is distinct from simple invalid-state validation. Manual recovery required recreating a generated state ref.

This violates the state-move atomicity required by ISSUE-0010.

## Investigation and implementation

- Reproduce the linked MCP move using an isolated parent/child project family.
- Test the current source build and the currently published/released PM binary to distinguish stale-runtime behavior from a current regression.
- Trace failures after destination ref creation, task-order movement, source ref deletion, mutation tracking, receipt construction, and MCP response serialization.
- Ensure any exception after source deletion restores the original source ref as well as the destination ref and task-order file.
- Keep state movement atomic across local CLI, local MCP, linked MCP, and web API adapters.
- Return a structured bounded application error instead of an unstructured MCP invocation failure.
- Preserve project-target ownership and mutation receipt paths on successful linked moves.
- Add regression coverage for valid linked moves, invalid target states, destination write failures, and failures occurring after source-ref deletion.

## Acceptance criteria

- A failed linked-project task move never leaves the task without a state reference.
- A valid linked-project move from `todo` to `doing` succeeds and reports the target project's mutation receipt.
- Invalid target states return `invalid_state` without changing either state refs or task order.
- Failures after destination creation or source deletion restore the exact original state and task-order contents.
- MCP failures remain structured and actionable.
- Tests cover current-project and trusted linked-project mutation paths.
- The reproduction is checked against the release artifact or installed runtime used by external Codex sessions.
- `pm doctor`, the .NET build, and the full .NET test suite pass.