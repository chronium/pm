---
id: AGENT-0013
title: Plan v2 interactive Codex collaboration and Git delivery
track: AGENT
milestone: agent-runs-v2
dependsOn:
- AGENT-0012
createdAt: 2026-07-27T06:57:02.6697610Z
modifiedAt: 2026-07-27T06:57:21.3940180Z
---

## Goal

Design the second agent-execution release after v1 is stable, covering conversational project planning and runner-owned branch delivery.

## Planning scope

- Design a direct Codex chat workspace inside PM for discussing features, milestones, tasks, dependencies, wiki changes, and implementation approaches.
- Define structured proposals that can create or update tasks and milestones only after explicit user review and application by PM.
- Evaluate persistent/resumable threads, conversation history, steering, attachments, context selection, and whether the Codex SDK remains sufficient or an app-server adapter is justified.
- Design runner-owned Git branch creation and push while keeping GitHub/SSH credentials outside the agent container.
- Define branch naming, base drift, force-push prohibition, conflicts, retries, repository permissions, audit events, and optional pull-request handoff.
- Revisit deferred v1 capabilities: live steering, global run history, notifications, dirty-worktree snapshots, multiple clients/repositories, additional auth modes, richer artifacts, and milestone scheduling.
- Preserve the rule that an agent may propose completion but PM or the user performs the authoritative state transition.

## Deliverables

- A reviewed v2 architecture and threat-model update.
- User flows and wireframes for chat, proposal review, branch results, and conflict handling.
- Protocol and persistence changes with backward-compatibility analysis.
- A sequenced implementation backlog with explicit non-goals.

## Acceptance criteria

- Planning starts from measured v1 operational experience rather than assumptions.
- Chat cannot silently mutate the public board.
- Branch credentials remain unavailable to Codex and worker containers.
- Existing patch-only runners remain protocol compatible.