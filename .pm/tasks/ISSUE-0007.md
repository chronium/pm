---
id: ISSUE-0007
title: Clean stale task order scopes when removing configuration
track: ISSUE
createdAt: 2026-07-29T12:10:27.2979920Z
modifiedAt: 2026-07-29T12:10:27.2979920Z
---

## Goal

Keep `.pm/task_order.yaml` valid when a milestone, track, or status is removed after its ordered scope becomes empty.

## Proposed implementation

- Review milestone, track, and status removal workflows in the application service layer.
- Remove task-order scopes that reference the deleted configuration key as part of the same successful mutation.
- Preserve scopes for remaining configuration and never remove ordering for in-use items.
- Ensure failed removals leave configuration and task order unchanged.
- Add regression coverage for empty stale scopes and project validation.

## Acceptance criteria

- Removing an unused milestone, track, or status cannot leave `stale_task_order_scope` issues.
- Configuration and task-order updates remain consistent across CLI, MCP, and web callers.
- Failed or rejected removals are atomic.
- Tests cover milestone, track, and status cleanup.