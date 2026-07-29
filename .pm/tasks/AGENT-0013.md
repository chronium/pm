---
id: AGENT-0013
title: Design interactive Codex collaboration and Git delivery
track: AGENT
milestone: agent-runner-evolution
dependsOn:
- AGENT-0012
createdAt: 2026-07-27T06:57:02.6697610Z
modifiedAt: 2026-07-29T12:08:46.9434050Z
---

## Goal

Design incremental collaboration and Git-delivery capabilities that can grow on top of the current runner without introducing an artificial version boundary.

## Planning scope

- Design a direct Codex chat workspace inside PM for discussing features, milestones, tasks, dependencies, wiki changes, and implementation approaches.
- Define structured proposals that create or update tasks and milestones only after explicit user review and application by PM.
- Evaluate persistent and resumable threads, conversation history, steering, attachments, context selection, and whether the Codex SDK remains sufficient or an app-server adapter is justified.
- Design runner-owned Git branch creation and push while keeping GitHub and SSH credentials outside the agent container.
- Define branch naming, base drift, force-push prohibition, conflicts, retries, repository permissions, audit events, and optional pull-request handoff.
- Revisit capabilities deferred from the initial runner: live steering, global run history, notifications, dirty-worktree snapshots, multiple clients and repositories, richer artifacts, and milestone scheduling.
- Preserve the rule that an agent may propose completion but PM or the user performs the authoritative state transition.

## Deliverables

- A reviewed incremental architecture and threat-model update.
- User flows and wireframes for chat, proposal review, branch results, and conflict handling.
- Protocol and persistence changes with backward-compatibility analysis.
- A sequenced implementation backlog with explicit non-goals.

## Acceptance criteria

- Planning builds on measured operational experience from the existing runner.
- Chat cannot silently mutate the public board.
- Branch credentials remain unavailable to Codex and worker containers.
- Existing patch-only runners remain protocol compatible.