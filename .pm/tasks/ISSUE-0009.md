---
id: ISSUE-0009
title: Prevent static mobile top-bar overflow
track: ISSUE
createdAt: 2026-07-30T13:47:46.1745160Z
modifiedAt: 2026-07-30T13:47:46.1745160Z
---

## Goal

Keep the generated read-only static site's mobile top bar within the viewport on Linux Chromium and other platforms with slightly different text metrics.

## Implementation

- Do not render the global HTTP synchronization indicator in static mode because a generated snapshot has no backend synchronization.
- Preserve the project name, Tasks and Wiki navigation, mobile search, read-only context, and theme control.
- Add focused shell coverage and rerun the generated static-site browser tests.

## Acceptance criteria

- The 390 px static mobile top bar has no horizontal overflow.
- Dynamic web mode retains its global synchronization indicator.
- Static desktop and mobile workflows pass.