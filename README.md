# PM

PM is a local project-management tool for software work. It stores project state in a `.pm/` directory, provides a .NET CLI for day-to-day changes, and includes a local Angular web board for dense task workflows.

## Features

- Task creation, editing, removal, listing, structured search, and movement between statuses.
- Project tracks, milestones, and configurable task statuses.
- Local web UI for task boards, task dialogs, settings, and wiki pages.
- MCP server support for integrating PM data with agent workflows.
- Project wiki pages stored alongside the project.
- Optional Cloudflare Worker next-ID service for shared task ID allocation with signed local identity auth.

## Quick Start

Restore dependencies:

```sh
dotnet restore PM.slnx
```

Build the solution:

```sh
dotnet build PM.slnx -m:1 --no-restore
```

Initialize a project:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll init
```

List tasks:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll list
```

Search tasks with free text and composable metadata filters:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll task search "render state:todo track:BUILD in:all" --limit 20
```

Search wiki page titles, paths, and bodies:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll wiki search "render pipeline" --limit 20
```

For Angular development, start the API-only host from inside an initialized PM project, then run the Angular development server in another terminal:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll web --api
cd web
npm start
```

The published release artifact serves the embedded Angular client with `dotnet artifacts/release/PM.dll web`. A normal Debug build has no embedded client and should use `--api` for Angular development.

Run tests:

```sh
dotnet test PM.slnx -m:1 --no-restore
```

## Milestone configuration

Milestones are ordered deliverable definitions in `.pm/pm_config.yaml`. The canonical shape keeps every field explicit even before activation and delivery behavior is configured:

```yaml
milestones:
  public-beta:
    title: Public beta
    description: Deliver an installable beta covering the complete local workflow.
    priority: high
    requiredActivationTriggers: []
    delivery:
```

PM continues to read the earlier scalar `milestones` map and separate `milestonePriorities` map, but project-setting mutations are blocked until that legacy representation is migrated. `pm doctor` diagnoses the legacy schema without writing files; `pm doctor --fix` performs the explicit, idempotent migration and then validates the project.

## MCP Profiles

`pm mcp` starts the normal MCP server and preserves the complete project-management tool surface for trusted local clients.

Isolated agent runs must start the restricted profile with the task assigned by the trusted runner:

```sh
pm mcp --profile run-worker --task-id AGENT-0002
```

The `run-worker` profile can read and search project, task, dependency, and wiki context, inspect wiki outlines, validate the project, and append implementation notes to its assigned task. Other mutations and every membership-administration tool are absent from its advertised MCP schema. The profile and assigned task come only from process arguments and cannot be changed by repository configuration. A run must fail if this MCP process cannot initialize.

Agent conclusions and notes are advisory. Moving a task to its completed state remains an authoritative PM control-plane action after validation or review.

## Agent Host Workspace

The Linux execution-plane service lives in `agent-host/`. It is a standalone TypeScript 7 workspace using Node 26 built-ins for SQLite, HTTPS, tests, cryptography, and process lifecycle. It accepts immutable protocol 1.x run commands, provides authenticated pairing and capability discovery, prepares isolated rootless Podman workspaces, executes Codex, journals sanitized events before publication, streams replayable SSE, collects bounded artifacts, recovers interrupted state, and prunes expired terminal runs. Protocol 1.2 can attach explicitly selected linked-project wiki snapshots as read-only context without widening task or write authority beyond the primary project.

Install its locked development tooling through Socket and run its complete gate separately:

```sh
cd agent-host
socket npm install
npm run validate
```

See `agent-host/README.md` for configuration and data-layout details.

## Angular Web Workspace

The replacement web client lives in `web/` as a standalone Angular 22 application. It requires the Node version pinned in `.node-version` (`26.5.0`) and npm 11. Normal .NET builds and tests do not install or build the Angular application.

Install its locked dependencies through Socket, then run the frontend commands from `web/`:

```sh
cd web
socket npm install
npm start
npm run check
npm test
npm run build
npm run storybook
npm run test:storybook
npm run build-storybook
npm run e2e
npm run frontend:validate
```

`npm start` uses `proxy.conf.json` to forward `/api` requests to the local .NET web server at `http://127.0.0.1:51237`. Use `npm run test:watch` for interactive unit-test development. Any command that changes frontend dependencies or its lockfile must be run through Socket.

For Angular development, run the API-only host and Angular development server in separate terminals:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll web --api
cd web
npm start
```

API-only mode binds to `127.0.0.1:51237` by default, accepts an optional `--port`, exposes only `/api/v1` and `/openapi/{documentName}.json`, and never opens a browser. The embedded Angular UI uses an available port by default and supports `--port` and `--open`.

Production Angular assets are included only when explicitly requested. Build the browser bundle first, then publish PM with embedding enabled:

```sh
cd web
npm run build
cd ..
dotnet publish PM/PM.csproj -p:EmbedAngularAssets=true
```

The published application then serves the embedded client with `pm web`. Ordinary .NET builds and tests do not inspect `web/dist`, invoke Node, or include local frontend output.

The same published application can export a backend-free, read-only project site:

```sh
dotnet artifacts/release/PM.dll site build
dotnet artifacts/release/PM.dll site build --output public/project --force
```

The default output is `dist/pm-site`. It contains relative Angular assets, `.nojekyll`, and a sanitized `pm-snapshot.json`, and uses hash URLs so it can be hosted under a repository path. Exported tasks and wiki content are intentionally public; identity, recovery, next-ID, credential, and local-path metadata are omitted. Static mode keeps board filters, task details and dependencies, client-side task/wiki search, wiki folders/pages, Markdown, themes, and responsive layouts, but hides settings and every mutation action.

Projects may opt into a published Overview landing page with `site.enabled: true` in `.pm/pm_config.yaml`. PM supplies a useful implicit composition, while explicit `single` and `split` compositions can select project identity, current milestone, active tasks, wiki documentation, wiki-sourced Markdown, and an optional copyright notice. Live and embedded PM remain Tasks-first; an enabled static export opens Overview first. See **Published Project Overview Configuration** and **Static Site Publishing** in the project wiki for the complete YAML contract and validation behavior.

## GitHub Pages

`.github/workflows/pages.yml` is a consumer of PM's published GitHub Action. After a successful Action promotion, it resolves the promoted `latest` identity, verifies the signed Action revision and digest-pinned runtime metadata, runs `doctor`, and then runs `site-build`. A manual dispatch may instead select an immutable `vMAJOR.MINOR.PATCH` Action ref or full signed commit SHA for rollback. The workflow uploads the generated directory with the official Pages artifact/deployment actions and force-updates an orphaned `gh-pages` branch with the identical tree for inspection. Configure the repository's Pages source as **GitHub Actions**. The artifact deployment is authoritative because pushes made by `GITHUB_TOKEN` do not trigger a separate branch-source Pages build.

## GitHub Action

Released PM versions are also available as the read-only `chronium/pm` Docker
Action. It supports `doctor`, `site-build`, and `version`; deployment remains the
consumer workflow's responsibility. Use `@latest` for coordinated edge consumers,
`@v1` for the latest delivered v1 milestone, `@vMAJOR.MINOR.PATCH` for an immutable
release, or a full signed commit SHA for the strongest source pin. Released Action
metadata always selects the tested `ghcr.io/chronium/pm` runtime by immutable digest.

`npm run frontend:validate` is the complete local frontend gate: formatting, generated API types, strict and production builds, unit tests, Storybook tests/build, and desktop/mobile Chromium E2E against disposable small and large projects. `npm run release` is the complete release gate: it begins with `socket npm ci`, builds and tests .NET, runs the frontend gate, publishes PM with embedded Angular assets under `artifacts/release/`, then runs the embedded-production and static-export smoke profiles. Socket findings stop the release and must be accepted explicitly outside the release script when appropriate. E2E uses a temporary identity/config home, dynamically assigned loopback ports, and a fake next-ID service; it does not access the deployed Worker or this repository's `.pm` project.

Storybook runs as an isolated zoneless component workshop on port 6006. Its browser checks require Chromium, installed with `socket npx playwright install chromium`; reusable visual components should maintain collocated stories, while routed containers and feature stores generally should not add them.

## Worker

The optional next-ID Worker lives in `next-id-worker/`. Its npm scripts cover tests, local development, deployment, and D1 migrations:

```sh
cd next-id-worker
npm test
npm run dev
npm run deploy
```

See `next-id-worker/README.md` for the Worker API and trust model.

## Publishing Note

PM project metadata under `.pm/` is intended to be public, commit-friendly project state: tasks, states, wiki pages, project config, and the public Worker project identifier. Hosted Worker operations are authenticated by a local user identity stored in the operating system user config, not by a secret committed into `.pm/`.
