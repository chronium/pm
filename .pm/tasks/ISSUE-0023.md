---
id: ISSUE-0023
title: Refine deliverable row disclosure styling
track: ISSUE
milestone: angular-web
createdAt: 2026-08-08T15:53:09.9308440Z
modifiedAt: 2026-08-08T15:53:09.9308440Z
---

## Goal

Make the milestone deliverable row communicate its open state correctly and reduce the visual weight of its bottom divider.

## Problem

The deliverable disclosure caret does not rotate when the row opens or closes, so the control does not visually reflect its current state. The row also uses a bottom border that is heavier than the surrounding milestone and task separators, making the deliverable area compete with primary content.

## Proposed implementation

- Rotate the existing deliverable disclosure caret when its native details element is open and restore it when closed.
- Reuse the established disclosure motion and reduced-motion behavior used by other task-board details controls.
- Replace the heavy bottom divider with the existing subtle border token and an opacity/weight consistent with nearby milestone and status separators.
- Preserve the native details/summary interaction, keyboard behavior, focus treatment, deliverable content, and collapsed-row density.
- Add Storybook examples for collapsed and expanded deliverables in light and dark themes.
- Present the result for visual review before running the full release or marking the task done.

## Acceptance criteria

- The caret points consistently with the collapsed state and rotates to communicate the expanded state.
- Caret state remains correct after repeated mouse and keyboard toggles.
- Reduced-motion preferences do not introduce unnecessary animation.
- The deliverable row divider is visibly lighter and no thicker than surrounding task-board separators.
- Collapsed and expanded rows remain readable without layout shift or horizontal overflow.
- Storybook and component coverage exercise open, closed, focus-visible, light-theme, and dark-theme states.
- No full release or done-state transition occurs until the styling has been visually reviewed and approved.