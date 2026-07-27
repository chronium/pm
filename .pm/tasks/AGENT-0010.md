---
id: AGENT-0010
title: Add Angular runner settings and task launch flow
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0009
createdAt: 2026-07-27T06:57:02.0007190Z
modifiedAt: 2026-07-27T06:57:21.3725760Z
---

## Goal

Make configured execution hosts understandable and let a user review an exact run specification before starting it.

## Implementation

- Add an Agent runners section to project settings for pairing, reachability, capabilities, installed profiles, capacity, and credential removal.
- Add a task-level `Run with Codex` action that opens a focused launch surface.
- Show runner, profile, model, effort, base SHA, task revision, limits, network profile, validation commands, and output policy.
- Run preflight before enabling Start and present actionable failures for dirty Git state, unreachable commit, offline runner, missing profile, no capacity, or protocol mismatch.
- Keep advanced runtime options fixed by the selected profile rather than editable as arbitrary values.
- Use existing Angular signals, resources, signal forms, API types, and UI primitives.

## Acceptance criteria

- A user can pair and inspect one Linux runner without seeing credentials.
- Start remains disabled until specification and preflight are valid.
- The launch flow makes network access and credential trust visible.
- Starting twice cannot create duplicate runs.
- Mobile preserves the primary Start/Cancel workflow.

## Validation

- Add focused unit and Storybook coverage for runner states and launch validation.
- Add desktop/mobile E2E using a fake runner.
- Run Angular formatting and the relevant frontend validation gates.