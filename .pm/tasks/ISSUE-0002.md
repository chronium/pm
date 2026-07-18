---
id: ISSUE-0002
title: Sidebar scope navigation keeps a closed task URL
track: ISSUE
milestone: angular-web
createdAt: 2026-07-18T15:43:45.4711300Z
modifiedAt: 2026-07-18T15:43:45.4711300Z
---

## Bug

After opening a routed task dialog and closing it, clicking All tasks, a track, or a milestone in the sidebar updates the visible board scope but can leave the browser URL on the prior `/tasks/<task-id>` route.

## Reproduction

1. Open a task from the Angular task board.
2. Close the task dialog.
3. Select All tasks, a track, or a milestone in the sidebar.
4. Observe that the board selection changes while the URL can remain on the previously opened task route.

## Expected behavior

Sidebar scope navigation should navigate to `/tasks` with the selected track or milestone query parameters, removing any stale task-ID segment. Browser history, refresh, and subsequent dialog navigation should reflect the visible scope.

## Notes

The current task detail is a routed modal over the board; future full-page task routes remain out of scope. Fix the current sidebar navigation contract without assuming that future route design.
