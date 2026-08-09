---
title: Published Project Overview Configuration
createdAt: 2026-08-09T07:55:11.6039200Z
modifiedAt: 2026-08-09T07:55:11.6039200Z
---

This page defines the discovery contract for configurable project Overview pages. It is an implementation target, not documentation of a currently available feature.

PM owns the visual system, responsive layout, accessibility, navigation, and rendering behavior. A project chooses the ordered content that tells its story. The contract deliberately does not expose arbitrary layout, CSS, colors, HTML, or page-builder controls.

## Decision summary

- Overview is opt-in through site.enabled: true.
- A project without enabled site configuration behaves exactly as it does today.
- Enabled live and embedded applications expose Overview in navigation but continue to open Tasks by default.
- Enabled static exports open Overview by default.
- Overview uses one shared composition model across live, embedded, and static modes.
- PM provides hero, milestone, tasks, wiki, and wiki-sourced Markdown sections.
- Configuration controls content and order, not grid geometry or visual styling.
- Explicit invalid configuration is reported; publishing never silently drops broken sections.
- Production parsing, routes, APIs, snapshots, and components are deferred to the implementation milestone.

## Storage boundary

Overview configuration belongs under a dedicated top-level site mapping in pm_config.yaml. It does not become part of the existing operational ProjectSettingsData model for accent, task states, tracks, milestones, and activation triggers.

The site mapping has no independent schema version. It evolves additively with ProjectConfig. A future breaking change requires an explicit project migration.

A disabled site may retain a fully configured home. Dormant configuration is still validated and preserved so enabling it later cannot expose a silently broken page.

## Minimal configuration

~~~yaml
name: Foo
accent: purple

site:
  enabled: true
~~~

This produces the implicit composition:

~~~yaml
site:
  enabled: true
  home:
    sections:
      - type: hero
      - type: milestone
      - type: tasks
      - type: wiki
~~~

Markdown is not implicit because it requires a source page.

## Full configuration

~~~yaml
site:
  enabled: true
  title: PM
  description: >
    Local project management built for software projects and agents.
  home:
    sections:
      - type: hero

      - type: markdown
        title: Introduction
        source: wiki:overview

      - type: milestone
        title: Current milestone
        milestone: public-beta

      - type: tasks
        title: What's being worked on
        filter: "state:in-progress"
        limit: 5

      - type: wiki
        title: Documentation
        pages:
          - getting-started
          - architecture
          - publishing/static-site
~~~

Section objects are always mappings with an explicit type. Scalar shorthand such as - hero is not supported.

## Disabled configuration

~~~yaml
site:
  enabled: false
  title: PM
  description: This composition is retained while publishing is disabled.
  home:
    sections:
      - type: hero
      - type: markdown
        source: wiki:overview
~~~

When site is absent, enabled is absent, or enabled is false:

- Live and embedded applications do not expose Overview.
- Static exports retain their current Tasks-first behavior.
- Configured site content is retained and validated but not rendered.

## Site fields

### enabled

enabled is optional and defaults to false. Only the Boolean value true opts a project into Overview behavior.

### title

title is optional plain text. When absent, the project name is used. When present, it becomes both the visible hero title and the document title for the Overview presentation. It does not change the operational project identity.

An explicitly present title must contain non-whitespace text.

### description

description is optional plain text. Folded or literal YAML strings are allowed, but the value is rendered as text rather than Markdown. Rich narrative content belongs in a Markdown section.

An absent description removes the hero description. An explicitly present value must contain non-whitespace text.

### home.sections

sections is an optional ordered sequence of uniform section objects. Omission selects the implicit composition. An explicitly present sequence must contain at least one section.

Array order is presentation order. One hero is allowed. Milestone, tasks, wiki, and Markdown sections may repeat with different selections.

## Common section behavior

Every section requires a supported type. Non-hero sections may provide an optional non-empty title. When omitted, PM supplies a type-specific title.

A section that resolves to no content remains visible as a compact, type-appropriate empty state. This keeps the configured story stable as project data changes and communicates current truth without layout collapse.

Repeated content section types are intentional and valid. A second hero is invalid. Duplicate page paths within one wiki section are invalid.

Unknown fields are invalid. This prevents accidental configuration typos and rejects layout controls that PM does not own.

## Hero section

~~~yaml
- type: hero
~~~

Hero uses site.title or the project name and the optional site.description. It provides restrained links into Tasks and Wiki using PM's fixed information architecture.

Hero accepts no section-specific content or styling fields. It may be omitted from an explicit composition, but it may appear at most once.

Default title: not applicable; the hero uses the site presentation title.

## Milestone section

Automatic selection:

~~~yaml
- type: milestone
  title: Current milestone
~~~

Explicit selection:

~~~yaml
- type: milestone
  title: Public beta
  milestone: public-beta
~~~

Fields:

- type: required and equal to milestone.
- title: optional plain-text section heading. Default: Current milestone.
- milestone: optional existing milestone key.

When milestone is omitted, PM selects the highest-priority milestone whose lifecycle is Active. Priority order uses PM's established urgent, high, medium, low, and none ranking. Ties use configured milestone order.

Ready-to-deliver, inactive, and delivered milestones do not participate in automatic selection. If no active milestone exists, the section renders a compact no-active-milestone state.

An explicit milestone may select any existing lifecycle. The section presents the selected milestone's actual title, description, lifecycle, task completion, and progress rather than pretending it is active.

A missing explicit milestone is a validation error.

## Tasks section

~~~yaml
- type: tasks
  title: Current work
  filter: "state:in-progress track:PM"
  limit: 6
~~~

Fields:

- type: required and equal to tasks.
- title: optional plain-text section heading. Default: Current work.
- filter: optional PM task-search query.
- limit: optional integer from 1 through 20. Default: 6.

When filter is omitted, the section selects open tasks: tasks that are not complete under PM's established task lifecycle semantics.

When filter is present, PM uses the existing task-query parser and predicates for free text, state, id, track, and milestone. Overview searches the complete current project:

- in:all is accepted but redundant.
- in:selection is invalid because Overview has no board-selection context.
- Missing referenced state, track, or milestone values are validation errors.
- A syntactically valid free-text query that currently matches nothing is valid and produces an empty state.

After filtering, tasks retain normal BoardService project ordering. Overview does not reorder free-text matches by relevance or fall back to TaskService's ID-based search-result order.

The section displays a compact task representation and preserves status, priority, dependency, and activation meaning without reproducing the full board.

## Wiki section

Implicit selection:

~~~yaml
- type: wiki
  title: Documentation
~~~

Explicit selection:

~~~yaml
- type: wiki
  title: Documentation
  pages:
    - overview
    - architecture
    - publishing/static-site
~~~

Fields:

- type: required and equal to wiki.
- title: optional plain-text section heading. Default: Documentation.
- pages: optional ordered sequence of existing local wiki paths.

When pages is omitted, PM selects up to six top-level wiki pages—paths without a slash—in existing ordinal path order.

When pages is present, PM preserves the configured order and renders every listed page. The sequence must be non-empty, paths must be normalized local wiki paths, every page must exist, and duplicates within the section are invalid.

No implicit family-wide wiki selection occurs. Cross-project links inside displayed content continue to use PM's established project-link translation.

## Markdown section

~~~yaml
- type: markdown
  title: Introduction
  source: wiki:overview
~~~

Fields:

- type: required and equal to markdown.
- title: optional plain-text section heading. When absent, use the source wiki page title.
- source: required and formatted exactly as wiki:<normalized-local-path>.

Only PM wiki pages are valid sources in the first version. Raw Markdown, file paths, URLs, inline HTML, and arbitrary resource schemes are not accepted.

The source page must exist and parse successfully. Rendering reuses PM's established sanitized Markdown pipeline and canonical project-link handling.

## Validation

Project validation rejects:

- A non-mapping site or home value.
- A non-Boolean enabled value.
- Blank explicit site or section titles.
- A blank explicit site description.
- A non-sequence or explicitly empty sections value.
- Scalar section shorthand.
- Missing or unknown section types.
- Unknown fields on site, home, or section mappings.
- More than one hero.
- Fields that attempt to control columns, widths, colors, backgrounds, padding, raw HTML, CSS, or arbitrary layout.
- Missing explicit milestone references.
- Invalid task-query syntax or referenced task metadata.
- in:selection in a tasks section.
- A tasks limit outside 1 through 20.
- An explicitly empty wiki pages sequence.
- Missing, invalid, or duplicate wiki page paths.
- Missing or unsupported Markdown sources.
- A Markdown source whose wiki page is missing or invalid.

pm doctor reports these as actionable project configuration errors. Validation includes dormant configuration so stored compositions remain trustworthy.

## Failure boundaries

A parse-level YAML or ProjectConfig shape error remains a project configuration failure.

A configuration that parses but contains invalid Overview references or filters has a narrower boundary:

- pm doctor reports the errors.
- pm site build fails when Overview is enabled.
- Live and embedded Tasks and Wiki remain usable.
- Overview renders actionable diagnostics instead of a partially composed page.
- PM never silently omits the invalid configured section.

Valid selections with no current content are not errors. They render the empty states defined by their section type.

## Compatibility

Projects without site configuration require no migration and preserve all current live, embedded, and static behavior.

The future serializer must omit an absent site value so unrelated configuration writes do not add site: null or otherwise churn older project files. A present disabled site remains round-trippable.

The contract does not require older PM releases to understand configuration introduced by a newer release. New PM releases must continue to read older projects without site configuration. Breaking future site-schema changes require an explicit, owned migration rather than an indefinite internal compatibility path.

Unknown section types and unknown fields remain errors. New section types are additive capabilities that require a PM version that explicitly understands them.

## Deliberate exclusions

The first implementation must not add:

- Grid, column, width, breakpoint, spacing, background, color, or typography controls.
- Raw HTML, arbitrary Markdown strings, external embeds, scripts, or CSS.
- User-defined navigation or replacement routes.
- A general page builder or nested arbitrary layouts.
- A Settings editor before the YAML contract and production behavior are implemented.
- Activity feeds, task statistics, linked-project sections, or other section types not approved by the discovery milestone.
- Production code as part of DISCOVERY-0010.

The subsequent discovery tasks own the resolved read model, isolated Storybook prototypes, cross-project-archetype review, and final implementation contract. Production work is planned only after those decisions are approved.