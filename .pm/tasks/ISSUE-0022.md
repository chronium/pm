---
id: ISSUE-0022
title: Refine task ID width and row hierarchy
track: ISSUE
milestone: angular-web
createdAt: 2026-08-08T15:52:22.7792050Z
modifiedAt: 2026-08-08T15:52:22.7792050Z
---

## Goal

Keep long task IDs readable on one line and explore a calmer task-row hierarchy that gives titles more usable horizontal space.

## Problem

The compact task-row layout gives the task ID a fixed desktop column that is too narrow for IDs such as `CONTENT-0018` and `PROTOCOL-0011`. Those IDs wrap onto two lines even though the title region has room to absorb a small horizontal shift.

The current single-line hierarchy may also make the ID compete too strongly with the task title. A slightly taller row with a smaller, more faded ID above the title could improve scanning, but that should be evaluated visually before becoming the production layout.

## Proposed experiment

- Prevent task IDs and their priority indicators from splitting across lines.
- Create a Storybook comparison using realistic short and long task IDs:
  - A widened horizontal ID column that preserves the current one-line row structure.
  - A slightly taller row where the ID is smaller, more muted, and positioned above the task title.
- Let the title region begin farther to the right in the horizontal variant while retaining useful title width.
- Preserve the priority indicator, dependency and activation icons, track/milestone context, selected state, and existing semantic colors.
- Exercise dense desktop, narrow desktop, mobile, long-title, light-theme, and dark-theme examples.
- Present both variants for visual review before selecting or applying one to the live task board.

## Acceptance criteria

- `CONTENT-0018`, `PROTOCOL-0011`, and similarly sized IDs never break across lines.
- The priority indicator remains visually associated with the ID without crowding it.
- Storybook provides a direct comparison between the wider-column and ID-above-title variants.
- The wider-column variant does not cause task titles or right-side status icons to overlap or overflow.
- The taller variant remains dense enough for real project boards and makes the ID visually quieter than the title.
- Mobile behavior remains readable without horizontal page overflow.
- No production layout, full release, or done-state transition occurs until the preferred variant has been visually reviewed and approved.