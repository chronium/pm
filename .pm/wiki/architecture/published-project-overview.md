---
title: Published Project Overview Configuration
createdAt: 2026-08-09T07:55:11.6039200Z
modifiedAt: 2026-08-09T08:06:36.2132700Z
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

## Routing contract

Overview is a project-scoped workspace mode. It does not replace Tasks or Wiki and it does not introduce configurable navigation.

### Route matrix

| Context | Root route | Overview route | Navigation |
| --- | --- | --- | --- |
| Live current project | / redirects to /tasks | /overview | Show Overview only when the document state is ready or invalid. |
| Embedded current project | / redirects to /tasks | /overview | Same behavior as live mode. |
| Live or embedded linked project | Existing project selection retains its current mode. | /projects/:projectId/overview | Show Overview only for a readable linked project whose document state is ready or invalid. |
| Static export, enabled site | / redirects to /overview after the snapshot is loaded. | /overview | Show Overview, Tasks, and Wiki. |
| Static export, disabled site | / redirects to /tasks. | /overview redirects to /tasks. | Preserve the existing Tasks and Wiki navigation. |

A direct Overview route whose document state is disabled redirects to the Tasks root for the same project. For example, /projects/abc/overview redirects to /projects/abc/tasks. The redirect does not carry remembered task filters; the Tasks workspace may restore its own per-project filters through the existing project-context behavior.

An enabled invalid Overview remains present in navigation. Its route displays the document's actionable diagnostics so the operator can repair the project without losing access to Tasks or Wiki.

An unreadable or unavailable linked project uses the established linked-project access failure. It is not treated as a disabled Overview.

Unknown live and embedded routes continue to fall back to Tasks. Only the empty static route selects Overview conditionally; a malformed deep link does not silently become Overview.

### Navigation and document titles

Overview joins the fixed Overview, Tasks, and Wiki information architecture. Projects cannot rename, reorder, or remove those modes through site configuration.

The current and linked-project mode helpers gain overview as a supported mode when production routing is implemented. Switching projects retains Overview only when the target project exposes it. Otherwise the switch lands on that project's Tasks route.

The Overview document title uses site.title when configured and the project name otherwise. Tasks and Wiki retain their existing document-title behavior.

## Resolved presentation contract

Presentation components consume one atomic, revisioned Overview document. They do not receive ProjectConfig, section filters, file paths, task-order data, or services that read project files.

The future live read routes are:

- GET /api/v1/overview for the current project.
- GET /api/v1/projects/{projectId}/overview for a readable linked project.

Both return the same transport-neutral shape and participate in the existing revisioned-read and ETag conventions. These routes and types are specified here for later implementation; DISCOVERY-0011 does not add them.

### Atomic document

~~~text
OverviewDocument
  status: disabled | ready | invalid
  projectId: string or null
  projectName: string
  documentTitle: string
  sections: ordered OverviewSection[]
  issues: OverviewIssue[]
  revision: string

OverviewIssue
  code: string
  message: string
  path: string
~~~

State invariants:

- disabled has no sections or issues. documentTitle falls back to the operational project name; dormant site presentation fields are not exposed.
- ready has its complete ordered section list and no issues.
- invalid has one or more issues and no sections. PM never returns a partially resolved page.
- A valid section with no current content remains part of a ready document and carries an empty value appropriate to its type.
- Transport, access, and unexpected project-read failures are request failures rather than invalid document states.

The revision covers the effective site configuration and every project value that contributed to the resolved document, including selected task order, task state, milestone lifecycle, activation state, and selected wiki content. An unrelated project change that cannot affect the document need not change the revision.

### Section union

Every resolved section is a member of this closed discriminated union:

~~~text
HeroOverviewSection
  type: hero
  title: string
  description: string or null

MilestoneOverviewSection
  type: milestone
  title: string
  milestone: OverviewMilestone or null

TasksOverviewSection
  type: tasks
  title: string
  tasks: OverviewTask[]

WikiOverviewSection
  type: wiki
  title: string
  pages: OverviewWikiPage[]

MarkdownOverviewSection
  type: markdown
  title: string
  sourcePath: string
  body: string
~~~

Array order is the resolved presentation order. The response does not echo configuration-only fields such as milestone selectors, task filters, task limits, or wiki page selectors.

A section has no user-configured identifier in the first version. Rendering tracks sections by their resolved array position because repeated section types are legal and configuration order is authoritative.

### Hero data

Hero contains the resolved presentation title and optional plain-text description. The fixed Tasks and Wiki links are derived from the selected project context rather than transported as configurable URLs.

documentTitle and the hero title intentionally contain the same resolved site title when a hero is present. documentTitle remains available when an explicit composition omits hero.

### Milestone data

~~~text
OverviewMilestone
  key: string
  title: string
  description: string
  priority: none | low | medium | high | urgent
  lifecycle: inactive | active | ready_to_deliver | delivered
  assignedTaskCount: integer
  doneTaskCount: integer
  requiredActivationTriggers: string[]
  unmetActivationTriggers: string[]
~~~

The renderer derives a completion percentage from doneTaskCount and assignedTaskCount. Zero assigned tasks produce an empty progress state rather than a vacuous 100 percent.

For automatic selection, the resolver considers only milestones whose resolved lifecycle is active, ranks urgent before high, medium, low, and none, then uses configured milestone order as the stable tie-breaker.

An explicit milestone may have any lifecycle. If no automatic candidate exists, milestone is null and the section renders the no-active-milestone empty state.

### Task data

OverviewTask reuses the semantic fields of BoardTaskSummaryResponse:

~~~text
OverviewTask
  id: string
  title: string
  track: string
  milestone: string or null
  priority: string
  prioritySource: string
  state: string
  dependencies: DependencyStatusResponse
  activation: TaskActivationEligibilityResponse
  descriptionPreview: string
  modifiedAt: UTC timestamp
~~~

It never includes Markdown, full descriptions, local metadata, file paths, or mutation revisions.

The resolver begins with the complete BoardService task sequence. It evaluates the existing task-query predicates without adopting search relevance ordering, retains BoardService order, and only then applies the configured limit.

An omitted filter selects tasks that are not in a configured terminal task state. The first implementation uses PM's existing done state semantics; it does not infer completion from labels or array position.

### Wiki data

~~~text
OverviewWikiPage
  path: string
  title: string
  modifiedAt: UTC timestamp
~~~

Wiki links are local to the selected project. Routes are constructed by the project context so linked-project Overview pages lead to the corresponding linked-project Wiki workspace.

Implicit selection uses at most six top-level pages in ordinal path order. Explicit selection preserves configuration order.

### Markdown data

Markdown contains the resolved source page path, resolved heading, and source body. sourcePath is retained for canonical link resolution and provenance; the local file path and wiki frontmatter are not transported.

The renderer uses PM's established sanitized Markdown and project-link translation. Empty source bodies remain valid and render the standard empty-content treatment.

## Resolution pipeline

One application service owns Overview resolution for current, linked, embedded, and static consumers:

1. Read the selected project's structured configuration and validate the complete dormant or enabled site definition.
2. Return disabled immediately when site.enabled is not true.
3. Return invalid with the complete deterministic issue list when the enabled site parses but fails Overview validation.
4. Expand the implicit section list when home.sections is omitted.
5. Read board, activation, task-order, and wiki data once for the selected project.
6. Resolve sections in configuration order using the rules below.
7. Return one ready document and compute its revision.

Resolution must not be duplicated in Angular or in the static exporter. Linked-project resolution uses the selected project's existing read context and the same resolver. It never falls back to the primary project's configuration or content.

The static snapshot builder stores the resolved Overview document and increments the snapshot schema when this contract is implemented. The static interceptor exposes it through the same /api/v1/overview read used by the Angular store. An enabled invalid configuration fails site publishing before a snapshot is emitted.

## Loading, empty, and failure behavior

### Loading

The application loads the atomic document independently of Tasks and Wiki. Existing modes remain usable while it loads.

On an Overview route, the page shows one stable page-level loading state rather than separate section spinners. Overview navigation is asserted only after a ready or invalid state is known; a first-load transport failure is not misrepresented as disabled.

### Valid empty content

A ready document may contain:

- A milestone section with milestone set to null.
- A tasks section with an empty tasks array.
- A wiki section with an empty pages array.
- A Markdown section with an empty body.
- A hero without a description.

Each renders a compact type-specific empty state. Empty content does not remove or reorder configured sections.

### Invalid configuration

A parsed but invalid enabled site returns an invalid document in live and embedded modes. The page presents all issues together with their codes and configuration paths. It does not render valid-looking sections beside invalid ones.

pm doctor reports the same underlying validation issues. Static publishing fails and retains the previously published output.

### Transport and project failures

Network, linked-project access, and unexpected read failures use the established API problem response. The Overview route displays a localized retry state. Tasks and Wiki remain available, and the client does not cache a failure as disabled.

### Read-only modes

Overview has no mutation controls in the first implementation. Static and read-only linked contexts render the same content model, navigation, focus behavior, and links. The existing application shell owns the read-only snapshot indicator; section components do not receive a duplicate readOnly flag.

## Representative resolved scenarios

### Ready current project

A live enabled project returns ready, exposes /overview in navigation, and still opens /tasks from /. Its sections contain only resolved display data in configured order.

### Disabled project

A project with no site mapping returns disabled. Overview is absent from navigation, /overview redirects to /tasks, and static export retains its Tasks-first root.

### Invalid enabled project

A project with site.enabled: true and a missing explicit milestone returns invalid with an issue such as:

~~~text
code: missing_overview_milestone
message: Milestone public-beta was not found.
path: site.home.sections[1].milestone
~~~

Live and embedded Overview show the diagnostics. Static publishing fails.

### Empty project

The implicit ready composition contains hero, a null milestone, an empty tasks list, and an empty wiki list. The page remains structurally complete.

### Enabled linked project

A readable child with its own enabled site is resolved from that child's configuration at /projects/{projectId}/overview. Its task and Wiki links stay in that project context. A disabled child redirects to /projects/{projectId}/tasks.

### Static enabled project

The static snapshot contains one ready Overview document. The empty hash route selects /overview after loading the snapshot; direct Tasks and Wiki deep links remain valid.

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