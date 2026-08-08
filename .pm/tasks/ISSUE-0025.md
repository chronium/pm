---
id: ISSUE-0025
title: Keep sidebar controls visible when project navigation overflows
track: ISSUE
milestone: angular-web
createdAt: 2026-08-08T17:16:40.8534660Z
modifiedAt: 2026-08-08T17:22:14.5615920Z
---

## Goal

Keep sidebar navigation and settings continuously reachable when a project contains more milestones and tasks than fit within the viewport.

## Problem

The sidebar currently grows with its milestone and task content without establishing an internal scroll region. Once that content overflows vertically, the Settings control is pushed out of view and the sidebar exposes no scrollbar, leaving users unable to reach it.

## Proposed implementation

- Keep the sidebar's top-level controls pinned at the top and the Settings control pinned in its established footer position.
- Make only the milestone and task navigation region independently scrollable within the remaining sidebar height.
- Give the scroll region a visible, compact scrollbar using the project's accent color and existing surface/border tokens, including consistent thumb, track, hover, and active treatment where the browser supports it.
- Preserve wheel, trackpad, touch, and keyboard scrolling without causing the page behind the sidebar to scroll unexpectedly.
- Avoid horizontal overflow and preserve the current sidebar density, selection state, and responsive behavior.
- Reuse the existing sidebar structure and CSS token system rather than introducing a page-level layout workaround.

## Acceptance criteria

- With enough milestones and tasks to exceed the viewport height, the top controls and Settings remain visible while the milestone/task region scrolls independently.
- The scrollable region has a discoverable scrollbar whose thumb reflects the project accent color and remains legible against the sidebar surface.
- The first and last milestone/task entries can both be reached using mouse wheel or trackpad, keyboard navigation, and touch at applicable viewport sizes.
- Scrolling the sidebar does not create horizontal page overflow or move its pinned controls.
- Short project lists retain the current layout with an intentionally reserved stable scrollbar gutter but no visible empty scrollbar track.
- Automated component or browser coverage exercises a realistically overflowing sidebar and verifies that Settings remains reachable.