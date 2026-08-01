---
id: ISSUE-0012
title: Make wiki preview debounce test deterministic
track: ISSUE
priority: high
createdAt: 2026-08-01T08:40:57.9470420Z
modifiedAt: 2026-08-01T08:41:07.4553290Z
---

## Problem
The Angular release gate intermittently fails `WikiMarkdownWorkspace` because its debounce test uses real wall-clock sleeps. Under suite load, an 80 ms sleep can resume after the 120 ms preview timer and invalidate the intermediate assertion.

## Proposed implementation
- Replace real delays with Vitest fake timers.
- Assert the preview remains stale immediately before the 120 ms boundary.
- Assert the sanitized preview updates exactly at the boundary.
- Preserve coverage of signal-form dirty state and script sanitization.

## Acceptance criteria
- The test no longer depends on scheduler timing.
- The debounce boundary remains explicitly covered.
- Repeated focused runs and the Angular test suite pass.