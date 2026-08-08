---
id: ISSUE-0020
title: Replace task-row badges with compact status icons
track: ISSUE
milestone: angular-web
createdAt: 2026-08-08T09:09:33.3068190Z
modifiedAt: 2026-08-08T09:09:33.3068190Z
---

## Goal

Replace the task-row priority, dependency, and activation badges with compact, accessible icons that preserve the current semantic colors and detailed status meaning.

## Proposed implementation

- Add a reusable priority indicator with an empty none state, clockwise quarter/half/three-quarter fills for low/medium/high, and a full urgent state with an exclamation mark.
- Place the priority indicator immediately before the task ID.
- Replace dependency badges with the cssBlock and cssUnblock icons.
- Replace activation badges with the cssLock and cssLockUnlock icons.
- Preserve complete accessible names, status summaries, priority provenance, and native tooltips.
- Add Storybook coverage for every priority level and representative task-row states.
- Preserve dense desktop and responsive task-row layouts without adding dependencies.

## Acceptance criteria

- None, low, medium, high, and urgent priorities are visually distinct and use the established semantic colors.
- The priority fill begins at 12 o'clock and progresses clockwise with a visible gap inside the outer ring.
- Priority appears directly left of the task ID.
- Dependency and activation status use the requested css.gg icons instead of full-width labels.
- Icon-only information remains available to assistive technology and through tooltips.
- Ready, blocked, missing-dependency, inactive, active, delivered, and ungated states remain truthful.
- Storybook shows the complete priority scale and representative desktop and mobile task rows.
- Component and end-to-end coverage verifies icon selection, labels, colors, responsive behavior, and the absence of obsolete badges.