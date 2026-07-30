---
id: DISCOVERY-0007
title: Explore app-level navigation and remote run history
track: DISCOVERY
createdAt: 2026-07-30T11:07:57.1420500Z
modifiedAt: 2026-07-30T11:07:57.1420500Z
---

## Idea

Explore introducing a narrow app-level vertical navigation rail outside the existing project workspace shell.

The default/top destination would retain the current PM workspace, including Tasks and Wiki. A second destination would provide a dedicated run workspace covering active remote runs, recent and historical runs, outcomes, runner context, and links into existing supervision views.

## Questions to answer

- Which navigation belongs at the app level versus inside the Tasks and Wiki project shells?
- How should active runs be surfaced without making run history dominate normal project work?
- What run index and filtering API is needed, and which data is runner-authoritative versus locally retained by PM?
- How should history behave when a runner is offline, removed, or no longer retains a run?
- Which compact states, counts, and alerts belong in the app rail?
- How should this work on mobile, where the vertical rail likely becomes another navigation pattern?
- Can the current task-run supervision workspace be reused without duplicating its event and artifact models?

## Boundaries

This is a discovery task, not an instruction to implement the rail or a new run persistence model. Preserve the current Tasks/Wiki top bar relationship within the PM workspace.