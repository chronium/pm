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

The published release artifact serves the embedded Angular client with `dotnet artifacts/release/PM.dll web`. `pm web` now means `pm web --ui angular`; a normal Debug build has no embedded client and should use `--api` for development. During the stability release only, `pm web --ui legacy` explicitly starts the previous server-rendered interface as a temporary fallback.

Run tests:

```sh
dotnet test PM.slnx -m:1 --no-restore
```

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

API-only mode binds to `127.0.0.1:51237` by default, accepts an optional `--port`, exposes only `/api/v1` and `/openapi/{documentName}.json`, and never opens a browser. UI modes retain the existing available-port default and support `--port` and `--open`.

Production Angular assets are included only when explicitly requested. Build the browser bundle first, then publish PM with embedding enabled:

```sh
cd web
npm run build
cd ..
dotnet publish PM/PM.csproj -p:EmbedAngularAssets=true
```

The published application then serves the embedded client with plain `pm web` (or the equivalent `pm web --ui angular`). Ordinary .NET builds and tests do not inspect `web/dist`, invoke Node, or include local frontend output.

The same published application can export a backend-free, read-only project site:

```sh
dotnet artifacts/release/PM.dll site build
dotnet artifacts/release/PM.dll site build --output public/project --force
```

The default output is `dist/pm-site`. It contains relative Angular assets, `.nojekyll`, and a sanitized `pm-snapshot.json`, and uses hash URLs so it can be hosted under a repository path. Exported tasks and wiki content are intentionally public; identity, recovery, next-ID, credential, and local-path metadata are omitted. Static v1 keeps board filters, task details and dependencies, wiki folders/pages, Markdown, themes, and responsive layouts, but hides search, settings, and every mutation action.

## GitHub Pages

`.github/workflows/pages.yml` validates the full release, generates this repository's project site, uploads it with the official Pages artifact/deployment actions, and force-updates an orphaned `gh-pages` branch with the identical tree for inspection. Configure the repository's Pages source as **GitHub Actions** before running the workflow. The artifact deployment is authoritative because pushes made by a workflow with `GITHUB_TOKEN` do not trigger a separate branch-source Pages build. The workflow uses no PAT and every action is pinned to an immutable reviewed commit.

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
