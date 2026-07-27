---
title: Static Site Publishing
createdAt: 2026-07-27T06:14:45.2936720Z
modifiedAt: 2026-07-27T06:14:45.2936720Z
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

## Included behavior

Static mode preserves:

- board scopes, status groups, and task details
- task priority and dependency information
- client-side task search
- wiki tree, folders, pages, and Markdown
- client-side wiki search
- themes and responsive layouts

Settings and every mutation action are hidden because there is no backend.

## Data boundary

Tasks and wiki content are intentionally public. The snapshot omits local file paths, identity keys, recovery data, next-ID credentials, and mutation metadata that does not belong in the published interface.

Review `.pm/` content before publishing anyway: sanitization removes system credentials, not secrets that someone manually wrote into a task or wiki page.

## GitHub Pages

This repository's `.github/workflows/pages.yml`:

1. validates the release
2. generates the static project site
3. uploads it with the official Pages artifact action
4. deploys it with the official Pages deployment action
5. force-updates an orphaned `gh-pages` branch with the same tree for inspection

Configure the repository Pages source as **GitHub Actions**. The artifact deployment is authoritative; the branch is a reviewable copy of the generated output.

## Local verification

Serve the output through any static HTTP server and exercise both task and wiki hash routes. Direct navigation should target `index.html` plus a fragment, for example `/#/wiki/overview`.