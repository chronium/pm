---
id: ISSUE-0014
title: Fix linked-project wiki outline resolution
track: ISSUE
priority: high
createdAt: 2026-08-02T07:38:42.0941440Z
modifiedAt: 2026-08-02T07:38:54.2261720Z
---

## Problem

The MCP `outline_wiki_page` tool can report that a wiki page is missing when targeting a readable linked project, even though `get_wiki_page` and `update_wiki_page` resolve and operate on the same page successfully.

Observed from the ChronoFall project family while targeting Starfall. A full-page supported update succeeded, so the failure appears specific to outline lookup rather than family discovery or page availability.

## Proposed implementation

- Reproduce the mismatch using a linked-project selector by alias and project ID.
- Compare project selection, path normalization, and error mapping across `outline_wiki_page`, `get_wiki_page`, and `update_wiki_page`.
- Route outline lookup through the same linked-project resolution and wiki service behavior used by the working tools.
- Add focused MCP and application-service coverage for current, child, unavailable, and genuinely missing pages.

## Acceptance criteria

- `outline_wiki_page` returns the outline for a readable linked wiki page.
- Alias and project-ID selectors behave consistently.
- Missing pages still return the correct not-found failure.
- Unavailable or unreadable projects retain their existing bounded errors.
- Current-project outline behavior remains unchanged.
- `dotnet build PM.slnx -m:1 --no-restore` and `dotnet test PM.slnx -m:1 --no-restore` pass.