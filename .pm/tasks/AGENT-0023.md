---
id: AGENT-0023
title: Add safe runner repository allowlist management
track: AGENT
milestone: agent-runner-evolution
priority: medium
dependsOn:
- AGENT-0012
createdAt: 2026-07-29T12:09:34.3808660Z
modifiedAt: 2026-07-29T12:09:47.2161930Z
---

## Goal

Let an operator authorize multiple trusted repositories without rerunning the full installer or manually editing runner configuration.

## Proposed implementation

- Add owner-local runner commands to list, add, and remove exact repository remotes using the existing repository-policy validation.
- Update the policy atomically with owner-only permissions and reject credentials, local remotes, duplicates, malformed URLs, and removal of the final allowed repository unless explicitly supported.
- Define whether changes require service restart or can be safely reloaded, and make the command output explicit.
- Extend `doctor` to report policy validity and the number of allowlisted repositories without printing private repository names by default.
- Keep repository authorization local to the runner host; the remote PM UI must not silently expand execution authority.
- Document the workflow for moving between trusted PM projects.

## Acceptance criteria

- An operator can authorize PM and another trusted repository through supported commands.
- Existing pairings, TLS identity, run history, and Codex credentials remain unchanged.
- Concurrent or interrupted updates cannot corrupt the policy.
- Unauthorized remote requests continue to fail closed.
- Tests cover add, list, remove, duplicates, invalid remotes, permissions, and atomic failure.