---
title: Getting Started
createdAt: 2026-07-27T06:14:45.2545150Z
modifiedAt: 2026-07-27T06:16:55.9363920Z
---

Examples below assume `pm` resolves to the published executable. From this repository, substitute `dotnet PM/bin/Debug/net10.0/PM.dll`.

## Create a project

Run PM from the repository directory that should own the project:

```sh
pm init
```

`init` prompts for a project name, ID format, next-ID service, and initial statuses. It creates `.pm/` in the current directory. Commands launched from descendants discover that project by walking upward.

The default next-ID service registers the project and may display a one-time recovery key. Save that key outside the repository. The public project identifier is written under `.pm/`; the local signing identity is stored in the operating system user configuration.

## Create and inspect work

```sh
pm track add BUILD "Build and repository"
pm milestone add m1 "Application skeleton"
pm task add "Create the solution" --track BUILD --milestone m1 \
  --description "Create the initial solution and test projects."
pm list
```

Move a task through the workflow:

```sh
pm move BUILD-0001
```

`move` presents the configured statuses. Use `pm task metadata BUILD-0001` for structured metadata changes or `pm task edit BUILD-0001` to edit the whole task file.

## Open the web app

A release artifact embeds the Angular client:

```sh
pm web --open
```

For frontend development, start the API and Angular dev server separately:

```sh
pm web --api --port 51237
cd web
npm start
```

## Add documentation

```sh
pm wiki create guides/setup --title "Development setup" --edit
pm wiki list
pm wiki search "setup"
```

## Check project health

```sh
pm doctor
```

Validation checks task files, state references, wiki frontmatter, and persisted task order. Run it after manual `.pm/` edits and before publishing.

## Publish a read-only snapshot

```sh
pm site build
```

The generated `dist/pm-site` can be hosted as static files. See **Static Site Publishing** in the wiki tree.