---
id: ISSUE-0010
title: Make task state moves resilient to missing status directories
track: ISSUE
createdAt: 2026-08-01T04:54:21.7790200Z
modifiedAt: 2026-08-01T04:54:21.7790200Z
---

## Problem

Moving a task into a configured status fails when that status has no directory under `.pm/states/`. This commonly occurs after cloning because Git does not retain empty directories.

The current state move deletes the source ref before writing the destination ref. If the destination directory is absent, the write throws and leaves the task without any associated state.

## Proposed implementation

- Make task-state writes create the configured destination directory before writing the ref.
- Ensure a failed destination write cannot remove the existing state association.
- Apply the same invariant to every path that creates or changes task state.
- Keep empty status directories optional on disk so cloned projects remain valid without placeholder files.
- Return a bounded application error instead of allowing a filesystem exception to escape.

## Acceptance criteria

- Moving the first task into a configured status succeeds when its state directory does not exist.
- A failed move preserves the original state ref and task-order placement.
- New task creation and structured task updates handle missing configured state directories.
- Tests reproduce clone-like projects with absent empty state directories.
- `pm doctor` reports a valid project after the successful move.
