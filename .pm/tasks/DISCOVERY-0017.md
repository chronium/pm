---
id: DISCOVERY-0017
title: Re-review and refine the complete Overview design
track: DISCOVERY
milestone: site-overview-discovery
dependsOn:
- DISCOVERY-0015
createdAt: 2026-08-09T08:23:59.9154520Z
modifiedAt: 2026-08-09T08:24:03.8546900Z
---

## Goal

Re-review the complete Overview composition after every discovery section and project-archetype scenario is available, then iterate the shared visual direction before the implementation contract is frozen.

## Review and iterate

- Assemble the hero, milestone, task, wiki, and Markdown sections into complete representative Storybook compositions.
- Review desktop, mobile, light, and dark presentations across the validated project archetypes.
- Reassess hierarchy, density, content width, surfaces, spacing, typography, navigation context, primary actions, section transitions, and empty states.
- Iterate the prototype components as a coherent page rather than polishing sections in isolation.
- Preserve the approved configuration and resolved-data boundaries unless the complete composition reveals a concrete contract defect.
- Capture worthwhile refinements that remain outside discovery as explicit follow-up tasks.

## Wide-screen composition experiment

- Compare the approved single-column composition with a PM-owned responsive split at genuinely wide viewports.
- Prototype a narrative column containing the hero and introductory Markdown beside a work column containing the current milestone and tasks.
- Compare documentation in the narrative column, below the work column, and spanning the page; choose the placement that best supports scanning without making the page feel like a dashboard.
- Preserve one coherent single-column layout at narrower widths rather than exposing layout controls in project configuration.
- Validate DOM order, keyboard focus order, screen-reader reading order, uneven column heights, long Markdown, empty sections, and dense task lists so the visual split does not produce a confusing content sequence.
- Treat the split as an optional PM presentation behavior derived from available width, never as user-configurable grid geometry.

## Deliverable

An owner-approved complete Overview design direction, with the prototype refinements incorporated and any deferred polish recorded before the final contract is frozen.