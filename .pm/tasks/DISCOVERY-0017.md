---
id: DISCOVERY-0017
title: Re-review and refine the complete Overview design
track: DISCOVERY
milestone: site-overview-discovery
dependsOn:
- DISCOVERY-0018
createdAt: 2026-08-09T08:23:59.9154520Z
modifiedAt: 2026-08-09T10:22:50.3434470Z
---

## Goal

Re-review the complete Overview composition after every discovery section, project-archetype scenario, and bounded layout mode is available, then iterate the shared visual direction before the implementation contract is frozen.

## Review and iterate

- Assemble the hero, milestone, task, wiki, and Markdown sections into complete representative Storybook compositions.
- Review desktop, mobile, light, and dark presentations across the validated project archetypes.
- Reassess hierarchy, density, content width, surfaces, spacing, typography, navigation context, primary actions, section transitions, and empty states.
- Iterate the prototype components as a coherent page rather than polishing sections in isolation.
- Preserve the approved configuration and resolved-data boundaries unless the complete composition reveals a concrete contract defect.
- Capture worthwhile refinements that remain outside discovery as explicit follow-up tasks.

## Layout mode review

- Compare the implicit single-column composition with the bounded, project-selected split composition prototyped in DISCOVERY-0018.
- Confirm which archetypes benefit from each mode and that choosing `split` remains optional.
- Review documentation in the primary region and in the full-width `after` region; choose representative guidance without creating an archetype-specific schema.
- Confirm that PM-owned widths, breakpoints, responsive collapse, surfaces, and navigation produce one coherent experience.
- Revalidate DOM order, keyboard focus order, screen-reader reading order, uneven column heights, long Markdown, empty sections, and dense task lists.
- Reject any pressure to add nested layouts, arbitrary geometry, or styling controls.

## Deliverable

An owner-approved complete Overview design direction, with the prototype refinements incorporated and any deferred polish recorded before the final contract is frozen.