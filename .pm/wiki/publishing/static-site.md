---
title: Static Site Publishing
createdAt: 2026-07-27T06:14:45.2936720Z
modifiedAt: 2026-08-11T11:27:39.0448530Z
---

PM can export the Angular UI as a backend-free, read-only project site. This is useful for public project status, documentation, demos, and GitHub Pages.

## Build

Use a release artifact that contains embedded Angular assets:

```sh
pm site build
pm site build --output public/project --force
```

The default output is `dist/pm-site`. `--force` replaces an existing non-empty output directory.

The export contains:

- relative Angular assets
- `.nojekyll`
- a sanitized `pm-snapshot.json`
- hash-based routes suitable for hosting below a repository path

## Overview landing pages

Overview is an optional, YAML-authored presentation of the same project data included in the static snapshot. Enable it in `.pm/pm_config.yaml`:

~~~yaml
site:
  enabled: true
~~~

That minimal configuration uses PM's implicit `single` composition in this order: hero, automatically selected milestone, open tasks, and top-level wiki pages. Projects without `site.enabled: true` keep the existing Tasks-first site.

An explicit single composition selects and orders the sections:

~~~yaml
site:
  enabled: true
  title: Example Project
  description: A public description of the project.
  home:
    layout: single
    sections:
      - type: hero
      - type: milestone
        title: Current milestone
      - type: tasks
        title: Current work
        filter: state:in-progress
        limit: 6
      - type: wiki
        title: Documentation
        pages:
          - getting-started
          - architecture
      - type: copyright
        notice: Copyright © 2026 Example Project
~~~

Use the bounded split composition when the introduction and delivery information benefit from separate columns:

~~~yaml
site:
  enabled: true
  title: Example Project
  home:
    layout: split
    primary:
      - type: hero
      - type: markdown
        title: Introduction
        source: wiki:overview
    secondary:
      - type: milestone
        title: Current milestone
      - type: tasks
        title: Current work
        filter: state:in-progress
        limit: 6
    after:
      - type: wiki
        title: Documentation
        pages:
          - getting-started
          - publishing/static-site
      - type: copyright
        notice: Copyright © 2026 Example Project
~~~

The milestone section selects the highest-priority active milestone when `milestone` is omitted; configured milestone order breaks priority ties. Supplying `milestone` selects that exact deliverable. Task sections accept PM's normal free text, `state:`, `id:`, `track:`, and `milestone:` predicates and preserve normal project task order. `in:selection` is invalid because Overview has no board-selection context.

Live and embedded applications continue to open Tasks first and expose Overview as a project-scoped mode. Enabled static exports open Overview from the empty root. Linked projects resolve their own site configuration and Overview data; static project switching still requires each linked project to publish its own artifact and declare a `publicSiteUrl`.

`pm doctor` validates Overview structure, referenced milestones and wiki pages, Markdown sources, task filters, limits, section placement, and unknown fields. Live and embedded PM show actionable diagnostics for a semantically invalid Overview while keeping Tasks and Wiki usable. `pm site build` refuses an enabled invalid Overview and leaves an existing destination untouched.

See **Published Project Overview Configuration** in the wiki tree for the normative field-by-field contract, responsive composition rules, and complete validation behavior.

## Included behavior

Static mode preserves:

- board scopes, status groups, and task details
- task priority and dependency information
- client-side task search
- wiki tree, folders, pages, and Markdown
- client-side wiki search
- themes and responsive layouts

Settings and every mutation action are hidden because there is no backend.

## Linked project sites

Projects in a linked family still publish independent artifacts. Configure `publicSiteUrl` on parent or child declarations to make those sites available from the static project switcher and from canonical `pm://project/...` task and wiki links.

Publishing never requires linked checkouts. When a child can read its parent, it may include sibling publication metadata from the parent's ordered declarations. If that parent is absent, the child still publishes with the direct parent hint it already owns. Targets without a public URL remain visible but unavailable.

A published URL may include a hosting path and query. PM preserves both and replaces only its fragment with the target hash route. Static output never converts a missing public URL into a local checkout path.

## Data boundary

Tasks and wiki content are intentionally public. The snapshot omits local file paths, identity keys, recovery data, next-ID credentials, and mutation metadata that does not belong in the published interface.

Review `.pm/` content before publishing anyway: sanitization removes system credentials, not secrets that someone manually wrote into a task or wiki page.

## GitHub Pages

This repository separates Action publication from site publication:

1. `.github/workflows/action-release.yml` builds, validates, publishes, and promotes the PM Action and OCI runtime.
2. `.github/workflows/pages.yml` starts after that release workflow completes and publishes only when the successful pushed revision is the revision now addressed by `latest`.
3. The site workflow verifies the signed Action revision and its digest-pinned release metadata.
4. It consumes `chronium/pm@latest` to run `doctor` before `site-build`, then uploads and deploys the generated site with the official Pages actions.
5. It force-updates an orphaned `gh-pages` branch with the identical tree for inspection.

A manual dispatch accepts `latest`, an immutable `vMAJOR.MINOR.PATCH` Action tag, or a full signed commit SHA. Immutable overrides are checked out from the published repository at the verified revision and are intended for rollback or incident recovery. The workflow records the site revision, requested and resolved Action identities, PM version, OCI digest, and published tree in its summary.

Configure repository Pages with **GitHub Actions** as the source. The Pages artifact deployment is authoritative; the branch remains a reviewable copy of the generated output.

## Local verification

Serve the output through any static HTTP server and exercise both task and wiki hash routes. Direct navigation should target `index.html` plus a fragment, for example `/#/wiki/overview`.