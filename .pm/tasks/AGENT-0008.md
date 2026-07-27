---
id: AGENT-0008
title: Build immutable Git workspaces and run artifact collection
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0007
createdAt: 2026-07-27T06:57:01.5504640Z
modifiedAt: 2026-07-27T06:57:21.3581790Z
---

## Goal

Use Git as the control-plane boundary while keeping credentials and normal checkouts outside agent containers.

## Implementation

- Maintain host-side bare mirrors and isolated per-run workspaces beneath the runner data root.
- Require a reachable remote and committed base SHA for v1.
- Fetch and verify the exact base commit before container startup.
- Present the worker with a credential-free checkout and prevent access to host SSH/Git configuration.
- After Codex exits, run profile-defined validation in the controlled phase and collect status, output, changed files, diff statistics, and resource usage.
- Produce a bounded Git patch, final agent response, validation report, event/log export, and artifact manifest.
- Define retention and cleanup behavior for successful, failed, and cancelled runs.
- Do not commit, create branches, push, merge, or accept dirty-worktree snapshots in v1.

## Acceptance criteria

- The run workspace exactly matches the requested base SHA before execution.
- The agent container has no push credential.
- Patch and validation artifacts are attributable to the immutable run specification.
- Oversized or binary changes are reported safely without unbounded API payloads.

## Validation

- Test unknown commits, changed remotes, fetch failure, binary changes, oversized patches, validation failure, retention, and cleanup.
- Add a disposable local Git remote integration test.