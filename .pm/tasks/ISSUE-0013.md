---
id: ISSUE-0013
title: Isolate project switcher filter persistence tests
track: ISSUE
priority: high
createdAt: 2026-08-02T06:34:17.8113310Z
modifiedAt: 2026-08-02T06:35:32.0618860Z
---

## Problem

The Angular release gate can fail when `ProjectSwitcher` inherits task filters left in `sessionStorage` by another spec. The production behavior intentionally remembers filters independently per project, but the switcher renderer test assumes empty storage without establishing that precondition.

## Proposed implementation

- Reset task-filter session storage before project-switcher tests.
- Keep the default-link assertion for a project with no remembered filters.
- Add explicit coverage proving a linked project's remembered filters are restored in its switcher URL.
- Run the focused test repeatedly and the complete release gate to catch order-dependent regressions.

## Acceptance criteria

- Project-switcher tests pass independently and as part of the full Angular suite.
- An unfiltered linked project produces a clean task URL.
- A linked project with remembered filters produces a URL containing those filters.
- The complete release gate passes.