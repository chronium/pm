---
title: Architecture
createdAt: 2026-07-27T06:14:45.2713530Z
modifiedAt: 2026-07-27T09:18:07.0984210Z
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

## Agent run protocol

The transport-neutral contracts for supervised agent execution live in `PM/AgentRuns/`. See **Agent Run Protocol** under Reference for immutable specifications, lifecycle, replay, authority, and v1 security boundaries.