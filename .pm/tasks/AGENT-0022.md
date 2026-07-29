---
id: AGENT-0022
title: Expose safe actionable runner failure diagnostics
track: AGENT
milestone: agent-runner-evolution
priority: high
dependsOn:
- AGENT-0012
createdAt: 2026-07-29T12:09:34.1289380Z
modifiedAt: 2026-07-29T12:09:47.2075570Z
---

## Goal

Replace generic terminal messages such as `Run processor failed` with bounded, actionable diagnostics while preserving the runner's secret and host-path sanitization boundary.

## Proposed implementation

- Define stable failure codes for workspace preparation, repository policy, fetch and revision checks, runtime startup, Codex startup, validation, artifact collection, cancellation, timeout, and resource limits.
- Map internal exceptions to a safe code and concise operator-facing summary at the subsystem boundary.
- Keep raw commands, remotes with credentials, host paths, environment values, and exception details out of durable events and logs.
- Preserve a generic internal-failure code for unclassified errors.
- Render the code and recommended next action in the Angular checkpoint and output views.
- Ensure retries create new immutable runs and retain the original failure evidence.

## Acceptance criteria

- Common failures identify the failed stage and a useful next action without requiring SQLite or journal inspection.
- Unknown internal failures remain safely generic.
- Sanitization tests cover secrets, paths, command output, and malicious exception text.
- Runner, PM API, Angular, Storybook, and end-to-end tests cover representative failure codes.