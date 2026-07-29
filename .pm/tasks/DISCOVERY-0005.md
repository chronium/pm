---
id: DISCOVERY-0005
title: Explore task archival and soft deletion
track: DISCOVERY
createdAt: 2026-07-28T12:43:00.9144860Z
modifiedAt: 2026-07-28T12:43:24.1395010Z
---

## Goal

Define first-class task archival so removed ideas remain inspectable without appearing in active project workflows.

## Decisions

- Normal remove actions must never delete task history from the project filesystem.
- Removing a task archives it and preserves its document plus enough metadata to restore it.
- Permanent filesystem deletion is a separate, explicitly named `prune` operation.
- Pruning is irreversible from PM's perspective, should require stronger confirmation in interactive interfaces, and must be advertised as destructive through MCP.

## Questions to resolve

- Choose the on-disk archive structure and metadata needed to preserve the task, its previous state, and when it was archived.
- Define how archived tasks interact with dependencies from active tasks and project validation.
- Define default visibility in task lists, search, recommendations, static exports, and direct task links.
- Define restoration behavior, including state, milestone, track, task order, and conflicts with reused IDs.
- Decide whether compatibility requires temporarily retaining `remove_task` as an alias for archive while adding explicit `archive_task`, `restore_task`, and `prune_task` operations.
- Keep CLI, MCP, JSON API, and web behavior consistent.

## Expected outcome

Document the remaining lifecycle and storage contract, then split implementation into focused core and adapter/UI tasks with acceptance criteria.