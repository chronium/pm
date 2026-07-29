---
id: ISSUE-0006
title: Avoid false Git LFS detection during runner workspace preparation
track: ISSUE
createdAt: 2026-07-29T11:44:34.4146190Z
modifiedAt: 2026-07-29T11:44:34.4146190Z
---

## Goal

Allow repositories that mention the Git LFS pointer signature in ordinary source or documentation while continuing to reject actual Git LFS pointer files before agent execution.

## Proposed implementation

- Replace the broad signature substring check in `GitWorkspaceService` with parsing of candidate Git blobs.
- Treat a file as an LFS pointer only when its contents match the version, object ID, and size structure of a real pointer.
- Preserve the v1 prohibition on repositories that require Git LFS.
- Add regression coverage for source code that contains the signature literal and for a genuine pointer file.
- Rebuild, package, reinstall, and retry the PM-0039 UI run on `agent-box`.

## Acceptance criteria

- The PM repository passes workspace preparation despite containing the signature in runner source code.
- A genuine Git LFS pointer remains rejected.
- Existing workspace isolation and unsupported-feature tests pass.
- The installed runner reports the exact fixed revision.