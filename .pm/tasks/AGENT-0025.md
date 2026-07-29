---
id: AGENT-0025
title: Surface runner activity on task views
track: AGENT
milestone: agent-runner-evolution
priority: medium
dependsOn:
- AGENT-0022
createdAt: 2026-07-29T13:31:28.6978350Z
modifiedAt: 2026-07-29T13:31:31.5544390Z
---

## Goal

Make remote execution activity visible from the task board and task detail so operators can see whether a task has active, completed, or failed runs without navigating through settings or retaining a run URL.

## Proposed implementation

- Add compact, non-dominant run status indicators to task rows and task detail.
- Link the active or most recent run to the existing supervision workspace.
- Distinguish active, completed, and failed outcomes without implying that a completed run marks the PM task done.
- Keep run history retrieval bounded and avoid adding per-row network requests.
- Preserve static/read-only behavior by omitting unavailable runner activity.

## Acceptance criteria

- Tasks with active or recent runs expose the correct state and a link to supervision.
- Tasks without runs retain the existing layout.
- Runner activity is loaded efficiently for realistic boards.
- The UI remains accessible, responsive, and subordinate to task content.
- Unit, Storybook, and E2E coverage includes active, completed, failed, and no-run states.