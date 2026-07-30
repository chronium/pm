---
id: DISCOVERY-0008
title: Explore local Codex project chat and thread history
track: DISCOVERY
createdAt: 2026-07-30T11:07:57.4090310Z
modifiedAt: 2026-07-30T11:07:57.4090310Z
---

## Idea

Explore adding a Project Chat destination below run history in the future app-level navigation rail. It would host local Codex conversations grounded in the active PM project and capable of proposing or performing PM changes such as creating tasks, refining plans, organizing milestones, and updating wiki content.

Thread history should reflect the local Codex CLI history available for this workspace. It is intentionally local to the machine and user: it does not need cross-device synchronization and must not be conflated with remote runner threads or remote task execution.

## Questions to answer

- Which supported Codex integration surface exposes local workspace threads and continuation safely?
- How should PM identify which local Codex threads belong to the current workspace?
- Can existing CLI history be listed and resumed without PM owning or copying Codex's conversation store?
- How should tool permissions, confirmations, and PM mutation authority work inside chat?
- How should proposed project changes be previewed and reviewed before application?
- What happens when local Codex history is unavailable, moved, pruned, or created outside PM?
- How should project chat coexist with remote runs while keeping their histories and trust boundaries distinct?
- What minimal local persistence, if any, should PM own for presentation preferences or thread labels?

## Boundaries

This is a discovery task only. Do not introduce synchronized chat storage, remote-agent chat history, or a new PM-owned conversation database without a later design decision.