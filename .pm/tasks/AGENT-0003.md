---
id: AGENT-0003
title: Scaffold the persistent TypeScript agent host
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
createdAt: 2026-07-27T06:57:00.4242720Z
modifiedAt: 2026-07-27T06:57:21.3232730Z
---

## Goal

Create the Linux `pm-agent-host` service foundation with durable scheduling state and no Codex or Docker behavior yet.

## Implementation

- Add a separate TypeScript workspace for the runner host using the repository-pinned Node version.
- Use `socket npm` for every dependency or lockfile mutation.
- Prefer Node built-ins for HTTP, process management, cryptography, and SQLite where they provide a sufficient implementation.
- Persist runs, immutable specifications, state, last event sequence, events, runner metadata, and artifact locations in runner-local SQLite.
- Implement a bounded queue, fixed concurrency limit, restart recovery classification, retention settings, and structured redacted logging.
- Add internal driver interfaces for agent execution and runtime lifecycle without duplicating PM application services.
- Keep runner data under a configurable `/var/lib/pm-runner`-style root outside repositories.

## Acceptance criteria

- Accepted jobs survive service restart and remain inspectable.
- Event sequence allocation is transactional and monotonic per run.
- Concurrency limits prevent excess jobs from starting.
- No secrets or repository paths appear in routine logs.
- The runner can use fake drivers in tests.

## Validation

- Add unit and persistence restart tests.
- Add Socket-reviewed install instructions and package scripts.
- Run formatting, strict TypeScript checks, and runner tests.