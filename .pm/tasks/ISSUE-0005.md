---
id: ISSUE-0005
title: Right-align the mobile task Back action
track: ISSUE
milestone: angular-web
createdAt: 2026-07-28T05:07:40.7179300Z
modifiedAt: 2026-07-28T05:07:40.7179300Z
---

## Goal

Align the mobile full-page task Back action to the right without compromising the wider inline-edit action cluster.

## Proposed implementation

- Give the shared task action container an explicit clean/read-state modifier.
- On mobile full-page task views, right-align the clean Back action.
- Keep dirty Save, Cancel, and Save-and-close controls using the existing flexible wrapping layout.
- Preserve desktop page and dialog action alignment.

## Acceptance criteria

- A clean mobile task page shows Back at the right edge of the task context row.
- Entering inline edit mode replaces it with the existing edit actions without unstable alignment or overflow.
- Desktop pages and dialogs remain unchanged.
- The action remains keyboard accessible and retains its current behavior.

## Validation

- Add a Storybook or browser geometry assertion for clean mobile alignment.
- Preserve existing dirty-action tests.
- Run Angular formatting, strict checks, relevant tests, and mobile E2E.