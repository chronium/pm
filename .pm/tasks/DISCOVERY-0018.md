---
id: DISCOVERY-0018
title: Prototype configurable single and split Overview compositions
track: DISCOVERY
milestone: site-overview-discovery
dependsOn:
- DISCOVERY-0015
createdAt: 2026-08-09T10:22:50.2126660Z
modifiedAt: 2026-08-09T10:22:50.2226580Z
---

## Goal

Prototype and validate a bounded project-configurable choice between single-column and two-column Overview compositions without turning publishing configuration into a page builder.

## Contract to explore

- Keep `single` as the implicit default and render one authoritative ordered `sections` list.
- Add an explicit `split` mode with exactly three fixed regions: `primary`, `secondary`, and optional `after`.
- Render `primary` and `secondary` as PM-owned columns at qualifying wide viewports; render `after` across the available width below them.
- Collapse split layouts deterministically to `primary`, then `secondary`, then `after` at narrower widths.
- Require `hero` exactly once and first in `sections` or `primary`.
- Allow only the approved `hero`, `markdown`, `milestone`, `tasks`, and `wiki` section vocabulary in every region.
- Reject nested splits, arbitrary column widths, grid coordinates, gaps, breakpoints, backgrounds, custom CSS, and raw HTML.
- Keep Overview, Tasks, and Wiki navigation owned by PM.

## Prototype and validate

- Add Storybook compositions showing both layout modes across the validated software-product, library, infrastructure, and personal-project archetypes.
- Confirm that projects which do not benefit from a split retain a coherent single-column page.
- Exercise documentation in `primary` and `after`, uneven column heights, long Markdown, dense task lists, empty optional regions, light and dark themes, and realistic wide and narrow viewports.
- Validate DOM order, keyboard focus order, screen-reader reading order, responsive collapse, and absence of horizontal overflow.
- Identify whether the fixed regions and mobile order are expressive enough before the public schema is frozen.

## Deliverable

An owner-reviewed Storybook prototype and a decision-ready layout contract for the final complete Overview review.