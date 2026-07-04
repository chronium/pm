# PM

PM is a local project-management tool for software work. It stores project state in a `.dev-pm/` directory, provides a .NET CLI for day-to-day changes, and includes a local server-rendered web board for dense task workflows.

## Features

- Task creation, editing, removal, listing, and movement between statuses.
- Project tracks, milestones, and configurable task statuses.
- Local web UI for task boards, task dialogs, settings, and wiki pages.
- MCP server support for integrating PM data with agent workflows.
- Project wiki pages stored alongside the project.
- Optional Cloudflare Worker next-ID service for shared task ID allocation.

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

Start the local web board from inside an initialized PM project:

```sh
dotnet PM/bin/Debug/net10.0/PM.dll web --port 51237
```

Run tests:

```sh
dotnet test PM.slnx -m:1 --no-restore
```

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

This repository intentionally includes its own PM project metadata under `.dev-pm/`, including tasks, states, wiki pages, project config, and `.dev-pm/next_id.txt`. In ordinary PM projects, review `.dev-pm/next_id.txt` before publishing because it may authorize ID allocation for that project.
