---
id: ISSUE-0026
title: Match deliverable rows to the production board surface
track: ISSUE
milestone: angular-web
createdAt: 2026-08-08T20:34:07.7697220Z
modifiedAt: 2026-08-08T20:34:07.7697220Z
---

## Goal

Make milestone Deliverable rows blend into the same resting production board surface as task-state headers.

## Problem

The production `.pm-board-surface` cascade forces task-state summaries and task lists transparent but does not include `.deliverable-description`. Deliverable rows therefore retain their standalone 34% canvas tint and appear as a different horizontal strip. Existing Storybook coverage misses the mismatch because the milestone stories are not rendered inside the production board-surface context.

## Proposed implementation

- Include the Deliverable container in the production board-surface transparency override.
- Preserve the existing Deliverable hover and focus surface, disclosure behavior, separator, expanded Markdown body, and standalone component styling.
- Render milestone Storybook coverage inside `.pm-board-surface` so production cascade differences are observable.
- Assert matching transparent resting backgrounds in both light and dark themes.

## Acceptance criteria

- On the production task board, a resting Deliverable row has the same computed background as the adjacent task-state summary.
- The resting Deliverable and task-state surfaces are transparent to the shared board surface in light and dark themes.
- Hover and focus still provide visible interaction feedback.
- Expanding and collapsing the Deliverable does not change its separator, caret alignment, or neighboring task-state placement.
- Storybook browser coverage exercises the production board-surface cascade rather than only the standalone component.