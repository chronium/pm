---
id: DISCOVERY-0009
title: Explore a responsive task detail side pane
track: DISCOVERY
createdAt: 2026-07-30T11:23:56.4587340Z
modifiedAt: 2026-07-30T11:23:56.4587340Z
---

Explore a third task presentation for sufficiently wide screens: selecting a task opens its workspace in a persistent pane that slides or reveals from the right side of the board instead of immediately opening a modal dialog.

The task workspace should remain one reusable component across side-pane, dialog, and full-page presentations. In side-pane mode, expose two expansion actions: open as a dialog and open as a full page. Determine practical width thresholds for 16:9 and ultrawide displays, pane sizing, route/history behavior, dismissal, focus and keyboard semantics, board context preservation, resizing, and the fallback behavior for narrower screens.

Keep this as a discovery item until the interaction can be tested against realistic dense boards and long task descriptions.