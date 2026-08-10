---
id: ISSUE-0028
title: Remove the duplicate top inset from Overview surfaces
track: ISSUE
milestone: angular-web
createdAt: 2026-08-10T05:40:17.4596560Z
modifiedAt: 2026-08-10T05:45:40.0277550Z
---

Remove the duplicate top insets from the Overview shell and content wrapper so the first section starts at the shell's top content boundary without changing the floating shell itself.

Acceptance criteria:

- Both the Overview shell and its content wrapper have zero block-start padding at desktop and mobile widths.
- The content wrapper retains its existing responsive inline and bottom padding.
- The Overview shell's outer margin, rounded surface, and the hero section's own spacing remain unchanged.
- Single-column and split-column Overview compositions behave consistently in live and static modes.
- Storybook verifies both rendered top-padding values and focused visual review covers desktop and 390px layouts.
- Full release validation, task completion, and commit wait for visual approval.