---
id: ISSUE-0004
title: Keep the mobile task count on one line
track: ISSUE
milestone: angular-web
createdAt: 2026-07-28T05:00:57.7944590Z
modifiedAt: 2026-07-28T05:00:57.7944590Z
---

## Goal

Keep the task navigation label and remaining-task count readable on narrow mobile headers when read-only snapshot context is present.

## Problem

In the generated read-only site at phone width, the additional `Read-only` context consumes enough top-bar space that the task count wraps from `23 left` onto two lines. This makes the primary mode navigation look broken and increases the header's visual noise.

## Proposed implementation

- Keep the task mode label and count on one line.
- Use a more compact mobile count presentation when the full `n left` label cannot fit, while preserving the full accessible name.
- Preserve the visible read-only indicator, Wiki navigation, search, theme control, and project context.
- Avoid horizontal page overflow or overlapping top-bar controls.
- Keep the desktop header unchanged.

## Acceptance criteria

- At a 390px viewport with a multi-line project name and read-only context, the Tasks label and count do not wrap.
- The remaining count remains understandable visually and announces the full `n tasks left` meaning to assistive technology.
- All top-bar controls remain visible, distinct, and operable.
- The header does not overflow horizontally.
- Live mode and wider layouts do not regress.

## Validation

- Add or extend a narrow static-site header assertion for non-wrapping mode navigation and no horizontal overflow.
- Add a representative Storybook state if the top bar is covered there.
- Run Angular formatting, strict checks, relevant tests, production build, and static E2E.