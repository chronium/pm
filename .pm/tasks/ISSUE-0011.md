---
id: ISSUE-0011
title: Preserve linked-project write trust when reopening projects
track: ISSUE
priority: urgent
createdAt: 2026-08-01T06:24:15.3582120Z
modifiedAt: 2026-08-01T06:24:35.1640330Z
---

## Problem

Opening a PM project automatically remembers its verified local binding but currently rewrites writeTrusted to false. Running any CLI command or starting MCP from a trusted linked checkout can silently revoke trust and make family inspection disagree with later mutation attempts.

## Proposed implementation

- Preserve write trust when remembering the same project ID at the same canonical repository path.
- Preserve trust when explicitly binding the same verified path.
- Reset trust when a binding moves to a different path.
- Keep new and recovered bindings untrusted by default.
- Cover CLI/MCP startup and linked family mutation behavior.

## Acceptance criteria

- Reopening a trusted project does not revoke write trust.
- Family inspection and linked mutation authorization observe the same trust state.
- Rebinding a project ID to a different path still requires trust to be granted again.
- No registry schema migration or implicit trust grant is introduced.