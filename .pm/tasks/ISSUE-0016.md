---
id: ISSUE-0016
title: Keep MCP mutation dependency receipts authoritative
track: ISSUE
createdAt: 2026-08-02T14:42:50.6109400Z
modifiedAt: 2026-08-02T14:42:50.6109400Z
---

## Goal

Ensure MCP task mutation responses report the same canonical linked-project dependency status as an immediate authoritative task read.

## Proposed implementation

- Reread task details through the linked-project read service after note and metadata mutations.
- Preserve mutation receipts, linked-project targeting, write-trust enforcement, and run-worker restrictions.
- Cover current-project and linked-project mutations without broadening family access.
- Add regression coverage for completed canonical dependencies whose owner is readable but not write-trusted.

## Acceptance criteria

- `append_task_note` returns resolved completed canonical dependencies instead of briefly marking them unavailable.
- `update_task_metadata` uses the same authoritative dependency enrichment.
- The returned dependency fields match an immediate `get_task` result.
- Existing mutation receipts and access controls remain unchanged.
- .NET build and tests pass.