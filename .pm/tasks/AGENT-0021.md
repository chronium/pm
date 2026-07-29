---
id: AGENT-0021
title: Add guarded collection of agent patches into the local project
track: AGENT
milestone: agent-runner-evolution
priority: urgent
dependsOn:
- AGENT-0020
createdAt: 2026-07-29T12:09:33.8843180Z
modifiedAt: 2026-07-29T12:09:47.1973840Z
---

## Goal

Close the patch-only runner workflow by allowing PM to collect a completed run's verified patch into the local working tree while preserving explicit human review and PM authority.

## Proposed implementation

- Add an application service that retrieves `changes.patch`, verifies its persisted digest and run ownership, and binds it to the run's immutable base commit.
- Preflight repository identity, current HEAD, task revision, worktree state, patch size, changed paths, and `git apply --check` before mutation.
- Present the changed paths, statistics, base revision, validation result, and any drift before asking for confirmation.
- Apply the patch only after explicit confirmation; do not commit, push, mark the task done, or discard existing work.
- Fail clearly on conflicts, dirty overlapping paths, stale bases, missing artifacts, binary-policy violations, or integrity errors.
- Record a local audit result without writing secrets or absolute runner paths.

## Acceptance criteria

- A successful completed run can be collected from its Angular run page without SSH or `scp`.
- PM refuses unsafe, stale, corrupt, conflicting, or wrong-project patches before modifying files.
- The user can inspect the resulting normal Git diff and run local validation before completing the task.
- Collection never commits, pushes, changes task state, or grants the agent additional authority.
- Service, API, Angular, and end-to-end tests cover successful and rejected collection.