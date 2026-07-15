# PM

PM is a local project-management tool for software work. It stores project state in a `.pm/` directory, provides a .NET CLI for day-to-day changes, and includes a local server-rendered web board for dense task workflows.

## Features

- Task creation, editing, removal, listing, and movement between statuses.
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

Start the local web board from inside an initialized PM project. The legacy UI remains the temporary default during the Angular migration:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll web --port 51237
dotnet PM/bin/Debug/net10.0/PM.dll web --ui legacy --open
```

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

The published application can then serve the embedded client with `pm web --ui angular`. Ordinary .NET builds and tests do not inspect `web/dist`, invoke Node, or include local frontend output.

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
