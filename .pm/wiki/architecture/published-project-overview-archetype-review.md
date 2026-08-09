---
title: Published project Overview archetype review
createdAt: 2026-08-09T09:51:26.2107540Z
modifiedAt: 2026-08-09T09:51:26.2107540Z
---

## Decision

Approve the Overview prototype composition and carry the section vocabulary into concrete implementation.

PM owns the responsive presentation and navigation. A project controls the content and order of its Overview without receiving grid, column, width, spacing, color, HTML, or other page-builder controls.

## Approved section vocabulary

- `hero`: project identity, optional description, and stable Tasks and Wiki destinations.
- `markdown`: a project-authored narrative sourced from one wiki page.
- `milestone`: one resolved deliverable and its lifecycle, progress, priority, and activation state.
- `tasks`: a resolved, limited task selection using the established task-row presentation.
- `wiki`: selected documentation destinations.

Section order is authoritative. A section kind may appear more than once when the project story benefits from it.

## Default composition

Projects without explicit home composition use:

1. `hero`
2. `milestone`
3. `tasks`
4. `wiki`

The default remains useful when the site description is absent, no active milestone can be resolved, or no documentation pages are selected. Empty sections use explicit, quiet empty states rather than disappearing unpredictably.

## Archetype review

### Software product

A narrative-first sequence—hero, introduction, current delivery, active work, documentation—communicated both product purpose and delivery status without turning task statistics into the dominant feature.

### Library

A documentation-first sequence worked naturally. Placing guides before the release milestone and using a second Markdown section for the compatibility promise confirmed that ordering and repeated section kinds provide sufficient flexibility.

### Infrastructure repository

Leading with the current change window, followed by operational work, operator notes, and runbooks made activation state and safe execution more prominent than introductory narrative.

### Personal project

The implicit composition remained understandable with no custom description, no active milestone, two unassigned tasks, and no selected documentation pages. This is an acceptable zero-configuration baseline.

## Validation

The four archetypes were reviewed at desktop and mobile widths in light and dark themes. Storybook interaction coverage verifies configured heading order, repeated Markdown sections, empty defaults, keyboard traversal through primary links, and absence of horizontal overflow. Existing resolved-section stories continue to cover long narrative and path content, missing sources, linked-project URLs, and lifecycle variants. Storybook accessibility checks report errors as test failures.

## Rejected ideas

- Free-form columns, widths, grid coordinates, padding, backgrounds, custom CSS, or raw HTML.
- Per-project navigation design; Overview, Tasks, and Wiki remain PM concepts.
- Hiding an explicitly configured empty section without a stable semantic rule.
- Adding archetype-specific production components or configuration variants.

## Unresolved work

The wide-screen vertical split remains a separate composition experiment. It must not introduce layout controls into the public schema. Final visual refinement should occur after the complete vocabulary is visible in the production Overview.

## Vocabulary changes

No section-vocabulary change is required by this review. The approved vocabulary remains `hero`, `markdown`, `milestone`, `tasks`, and `wiki`, with the four-section implicit default above.