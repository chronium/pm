---
title: Architecture
createdAt: 2026-07-27T06:14:45.2713530Z
modifiedAt: 2026-08-07T08:52:57.5662530Z
---

PM is organized around one application model with several adapters.

```text
CLI ---------+
MCP ---------+--> Application services --> ProjectRoot / .pm files
JSON API ----+              |
                              +--> next-ID client --> Cloudflare Worker

Angular web client --> JSON API
Static Angular client --> sanitized pm-snapshot.json
```

## .NET application

- `PM/Project/` owns project discovery, configuration, state references, ordering, and persistence paths.
- `PM/Tasks/` and `PM/Wiki/` contain domain models and CLI commands.
- `PM/Application/` owns workflows shared across adapters, including task, board, wiki, configuration, and validation behavior.
- `PM/Mcp/` maps application results to MCP tools and structured responses.
- `PM/Api/` maps the same services to the versioned JSON API.
- `PM/Site/` builds a sanitized read-only snapshot and static export.
- `PM/Web/` hosts the API, embedded Angular assets, and temporary legacy UI.

Application failures use `AppResult` or `AppResult<T>` so CLI, MCP, and HTTP adapters can present the same validation outcome appropriately.

## Angular client

The standalone Angular 22 workspace lives in `web/`. It is zoneless, strictly typed, routed, and uses generated OpenAPI types. A shared top bar switches between task and wiki shells; each shell owns its sidebar and feature workspace.

The client uses resource revisions and `If-Match` preconditions for mutations. Static mode swaps HTTP-backed data for a sanitized snapshot adapter while reusing the routed read components.

## Next-ID Worker

The optional Cloudflare Worker allocates track-scoped numeric IDs in D1. Requests are signed with a local P-256 identity. The Worker does not store task or wiki content and is not required for reading an existing project.

## Design boundary

Adapters should not invent parallel business logic. New behavior belongs in an application service first, then receives CLI, MCP, API, or UI exposure as appropriate.

Milestone activation is resolved before dependency readiness and ranking. Lifecycle mutations use the shared task, trigger, delivery, validation, and configuration-persistence services so every adapter observes the same latch, rollback, cycle, and provenance rules. Linked-family reads evaluate those rules through each task's owning project.

Do not preserve an obsolete internal path merely because older code or tests still call it. Once an approved replacement is complete, update callers and remove the old implementation. Compatibility is retained only for a demonstrated external consumer or stored-data need with an owner and removal condition; the legacy milestone reader is such a data migration boundary, not permission to keep parallel application behavior.

## Agent run protocol

The transport-neutral contracts for supervised agent execution live in `PM/AgentRuns/`. See **Agent Run Protocol** under Reference for immutable specifications, lifecycle, replay, authority, and v1 security boundaries.