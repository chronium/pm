---
id: AGENT-0024
title: Keep run supervision panes full height
track: AGENT
milestone: agent-runner-evolution
priority: low
dependsOn:
- AGENT-0011
createdAt: 2026-07-29T12:09:34.6267440Z
modifiedAt: 2026-07-29T12:09:47.2239290Z
---

## Goal

Keep the split-screen run workspace visually stable before and after artifacts arrive.

## Proposed implementation

- Stretch both progress and output panes to the available desktop viewport height beneath the shared app bar.
- Keep each pane's internal scrolling independent and prevent artifact arrival from changing the overall split height.
- Preserve the existing mobile single-pane behavior and safe-area handling.
- Avoid fixed pixel heights that break zoom, compact windows, or browser UI changes.

## Acceptance criteria

- Both desktop panes fill the available height for new, active, failed, and completed runs.
- Empty or delayed artifact sections do not collapse either side.
- No page-level overflow or hidden controls are introduced.
- Storybook and browser checks cover empty, active, artifact-rich, short, and narrow layouts.