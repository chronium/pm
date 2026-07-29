---
id: ISSUE-0008
title: Keep patch collection actions visible
track: ISSUE
createdAt: 2026-07-29T19:29:10.5708950Z
modifiedAt: 2026-07-29T19:29:10.5708950Z
---

## Problem

The patch-collection review dialog can hide its entire action footer after a successful preflight. The safety checks remain visible, but **Cancel** and **Collect patch** are clipped outside the dialog, leaving keyboard traversal or viewport resizing as the only workaround.

Observed while collecting the remote result for `LINK-0001` on a tall desktop viewport.

## Cause

The dialog has a capped height and `overflow: hidden`, while `.patch-body` calculates its own maximum height only from the viewport. On viewports taller than the dialog's fixed cap, the body can consume more space than the capped dialog allows and push the footer beyond the clipped boundary.

## Proposed implementation

- Make the native dialog a three-row layout: intrinsic header, `minmax(0, 1fr)` scrollable body, and intrinsic action footer.
- Give the body `min-height: 0` and local overflow scrolling instead of a competing viewport-derived maximum height.
- Keep **Cancel** and **Collect patch** visible at every supported desktop and mobile viewport.
- Preserve the existing full-screen mobile dialog behavior.
- Verify long safety-check and changed-path lists scroll only the body.
- Preserve logical focus order and keyboard activation for all actions.

## Acceptance criteria

- A successful patch preflight always shows the action footer without resizing the browser.
- Long review content scrolls inside the dialog body while header and footer remain visible.
- The dialog works on tall desktop, short desktop, and mobile viewports.
- **Close**, **Cancel**, and **Collect patch** remain keyboard reachable with visible focus.
- Loading, failed-preflight, retry, applying, and disabled states retain their existing behavior.
- Focused component or browser coverage reproduces the previous clipping condition and verifies the corrected layout.