---
title: Published project Overview archetype review
createdAt: 2026-08-09T09:51:26.2107540Z
modifiedAt: 2026-08-09T10:59:29.4768120Z
---

## Decision

Approve the Overview prototype composition and carry the section vocabulary and bounded layout choice into concrete implementation.

PM owns the responsive presentation and navigation. A project controls the content and order of its Overview without receiving grid, column, width, spacing, color, HTML, or other page-builder controls.

The implicit layout remains `single`, using one ordered section list. Projects may instead choose `split`, with ordered `primary` and `secondary` regions and an optional full-width `after` region. At wide widths PM owns a fluid, bounded 44/56 presentation; primary section N and secondary section N share an intrinsic row whose height is the larger of the pair, and unmatched trailing sections receive their own rows. At narrow widths the semantic order is primary, secondary, then after.

## Approved section vocabulary

- `hero`: project identity, optional description, and stable Tasks and Wiki destinations.
- `markdown`: a project-authored narrative sourced from one wiki page.
- `milestone`: one resolved deliverable and its lifecycle, progress, priority, and activation state.
- `tasks`: a resolved, limited task selection using the established task-row presentation.
- `wiki`: selected documentation destinations.
- `copyright`: an optional plain notice rendered as the final section of a single composition or in the split after region.

Section order is authoritative. A section kind may appear more than once when the project story benefits from it. Copyright remains a bounded semantic footer and is not available inside either split column.

## Default composition

Projects without explicit home composition use:

1. `hero`
2. `milestone`
3. `tasks`
4. `wiki`

The default remains useful when the site description is absent, no active milestone can be resolved, or no documentation pages are selected. Empty sections use explicit, quiet empty states rather than disappearing unpredictably.

## Archetype review

### Software product

A narrative-first sequence—hero, introduction, current delivery, active work, documentation—communicated both product purpose and delivery status without turning task statistics into the dominant feature. The split prototype paired identity with delivery and introduction with active work, while documentation remained full width.

### Library

A documentation-first sequence worked naturally. Placing guides before the release milestone and using a second Markdown section for the compatibility promise confirmed that ordering and repeated section kinds provide sufficient flexibility. Both single and split compositions remained legible without introducing library-specific layout rules.

### Infrastructure repository

Leading with the current change window, followed by operational work, operator notes, and runbooks made activation state and safe execution more prominent than introductory narrative. The aligned split exposed a useful future question: closely related material such as operator notes and runbooks could eventually share one composite section to balance a taller task list.

### Personal project

The implicit composition remained understandable with no custom description, no active milestone, two unassigned tasks, and no selected documentation pages. This is an acceptable zero-configuration baseline in both single and responsive split presentations.

## Validation

The four archetypes were reviewed at desktop, ultrawide, and mobile widths in light and dark themes. Twenty-five Storybook scenarios cover single and split compositions, aligned split rows, long and uneven content, implicit empty defaults, copyright placement, configured heading and DOM order, keyboard traversal through primary links, container-responsive task rows, and absence of horizontal overflow. Existing resolved-section stories continue to cover long narrative and path content, missing sources, linked-project URLs, and lifecycle variants. Storybook accessibility checks report errors as test failures.

## Rejected ideas

- Free-form columns, widths, grid coordinates, padding, backgrounds, custom CSS, or raw HTML.
- Per-project navigation design; Overview, Tasks, and Wiki remain PM concepts.
- Hiding an explicitly configured empty section without a stable semantic rule.
- Adding archetype-specific production components or configuration variants.
- Treating split alignment as permission for arbitrary nested layout controls.

## Deferred work

- Composite or merged section grouping may later combine closely related material, such as operator notes and runbooks, within one semantic region. It requires a separate design that preserves heading order and accessibility and does not become a nested page builder.
- The production configuration schema, API contract, and renderer are not frozen or implemented by this discovery task.
- Final visual refinement should occur after the complete vocabulary is visible in the production Overview.

## Vocabulary changes

Add `copyright` as a bounded optional footer section. The content vocabulary otherwise remains `hero`, `markdown`, `milestone`, `tasks`, and `wiki`. No compound section kind is approved by this review.
