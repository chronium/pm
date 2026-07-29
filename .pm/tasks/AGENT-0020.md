---
id: AGENT-0020
title: Add run artifact downloads to the Angular workspace
track: AGENT
milestone: agent-runner-evolution
priority: urgent
dependsOn:
- AGENT-0019
createdAt: 2026-07-29T12:09:33.6183920Z
modifiedAt: 2026-07-29T12:09:47.1869580Z
---

## Goal

Make completed run evidence directly downloadable from the Angular supervision workspace without SSH or knowledge of runner storage.

## Proposed implementation

- Add typed Angular API support for artifact-content responses.
- Add accessible download actions beside retained artifacts, with clear loading, unavailable, and integrity-failure states.
- Use the safe filename and media type supplied by PM.
- Verify the received byte length and SHA-256 digest before presenting a successful download.
- Keep event-journal download behavior consistent with individual artifact downloads.
- Preserve the stable split-screen layout as artifacts arrive.

## Acceptance criteria

- `changes.patch`, validation, reports, manifests, responses, and event logs can be downloaded from the run page.
- Corrupt or incomplete downloads are rejected and explained without saving a misleading file.
- Keyboard and touch users can reach every download action.
- Unit, Storybook, and browser tests cover success, loading, missing, pruned, and integrity-failure states.