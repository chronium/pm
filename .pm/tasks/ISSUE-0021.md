---
id: ISSUE-0021
title: Increase spacing between priority icon and task ID
track: ISSUE
milestone: angular-web
createdAt: 2026-08-08T09:22:28.5707780Z
modifiedAt: 2026-08-08T09:22:28.5707780Z
---

## Goal

Give the task-row priority indicator more breathing room before the task ID so the two elements remain visually distinct while still reading as one identity group.

## Proposed implementation

- Increase the horizontal gap between the priority indicator and task ID in the shared task-row identity layout.
- Preserve the existing priority icon size, task ID alignment, desktop column alignment, and compact row density.
- Apply the spacing consistently in desktop and narrow layouts.
- Keep the change scoped to the task row; do not alter priority semantics, colors, tooltips, or accessible names.

## Acceptance criteria

- The priority icon no longer feels crowded against the task ID.
- The icon and ID still read as a related identity group.
- Desktop task rows remain on one line at realistic widths.
- Long mobile task titles continue to wrap without horizontal overflow.
- Ready, blocked, selected, light-theme, and dark-theme Storybook states remain visually balanced.
- Focused component checks pass, and the result is presented for visual review before running the full release or marking the task done.