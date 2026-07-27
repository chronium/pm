---
title: Web UI Guide
createdAt: 2026-07-27T06:14:45.2675600Z
modifiedAt: 2026-07-27T06:14:45.2675600Z
---

The Angular web client is the primary visual workspace. It uses the versioned JSON API while preserving `.pm/` as the source of truth.

## Task workspace

The task mode provides:

- sidebar scope selection by milestone and track
- collapsible status groups with completed work collapsed by default
- structured task search in the shared top bar
- task creation and full-page or dialog detail routes
- inline title, placement, priority, dependency, and Markdown description editing
- project settings and validation

Task search understands free text plus `state:`, `id:`, `track:`, `milestone:`, and `in:` predicates. Search is scoped to the current sidebar selection unless the query includes `in:all`.

## Wiki workspace

Wiki mode replaces the task sidebar with a page tree. It provides folder navigation, breadcrumbs, full-text search, page creation, Markdown reading, body editing with synchronized preview, metadata editing, rename, and delete.

Wiki paths are slash-separated. A page at `guides/setup` appears at `#/wiki/guides/setup` and is stored at `.pm/wiki/guides/setup.md`.

## Development mode

A normal Debug build does not embed Angular assets. Run the API and frontend separately:

```sh
pm web --api --port 51237
cd web
npm start
```

The Angular dev server proxies `/api` to `http://127.0.0.1:51237`. API-only mode serves `/api/v1` and the OpenAPI document on loopback.

## Embedded mode

A release artifact built with Angular embedding serves both API and client:

```sh
pm web
pm web --open
pm web --port 5200
```

`pm web --ui legacy` remains a temporary fallback during the Angular stability period.

## Concurrent changes

API resources carry revisions. Mutations use HTTP preconditions so a stale browser does not silently overwrite a task or wiki page changed by another process. The client polls for external changes and surfaces conflicts for review.

## Static mode

The same Angular client can run against an exported snapshot without a backend. Static mode preserves task/wiki browsing and search while hiding every mutation and settings action.