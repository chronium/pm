---
id: ISSUE-0015
title: Resolve linked dependencies without write trust
track: ISSUE
priority: urgent
createdAt: 2026-08-02T11:57:21.6868740Z
modifiedAt: 2026-08-02T11:57:30.8647380Z
---

## Problem

A child project can read its linked parent successfully, but PM may mark a canonical dependency on a completed parent task as unavailable when that parent has not been granted local write trust.

Observed from Starfall: a canonical dependency on completed `COORD-0008` in the readable ChronoFall parent was reported unavailable while the parent was untrusted for writes. Removing the cross-project edge avoided the false blocker, but discarded valid dependency information.

Canonical dependency inspection is read-only. Local write trust should gate linked mutations, not task lookup, state resolution, dependency readiness, validation, or recommendation ranking.

## Proposed implementation

- Reproduce dependency resolution from a child to a readable parent that is not write-trusted.
- Trace linked-project authorization through canonical task lookup and dependency graph construction.
- Separate readable-project eligibility from mutation trust at the shared boundary.
- Audit related read-only paths such as validation, next-task ranking, task retrieval, and dependency summaries for the same coupling.
- Add family tests covering readable/untrusted, readable/trusted, unavailable, and missing linked projects.

## Acceptance criteria

- A canonical dependency on a completed task in a readable, write-untrusted linked project resolves as complete.
- Incomplete readable linked dependencies resolve and block normally.
- Missing or unavailable linked projects remain warnings/blockers as designed.
- Linked mutations remain rejected until write trust is granted.
- Dependency readiness, `pm doctor`, CLI, MCP, and web-facing project data agree.
- `dotnet build PM.slnx -m:1 --no-restore` and `dotnet test PM.slnx -m:1 --no-restore` pass.