---
id: AGENT-0008
title: Build immutable Git workspaces and run artifact collection
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0007
createdAt: 2026-07-27T06:57:01.5504640Z
modifiedAt: 2026-07-29T07:05:35.6654860Z
---

## Goal

Use Git as the control-plane boundary and activate one complete runner-owned execution lifecycle while keeping credentials and normal checkouts outside agent containers.

## Lifecycle boundary

This task owns the cohesive per-run lifecycle from immutable workspace preparation through validation, artifact collection, retention, and cleanup. It composes the existing scheduler, Podman runtime, and Codex driver; completed work must no longer remain in the queue-only controller.

## Implementation

- Maintain host-side bare mirrors and isolated per-run workspaces beneath the runner data root.
- Require a reachable remote and committed base SHA for v1.
- Fetch and verify the exact base commit before container startup.
- Prepare the directory and ownership contract expected by the hardened Podman runtime.
- Present the worker with a credential-free Git checkout and prevent access to host SSH configuration, Git credentials, normal checkouts, and other run workspaces.
- Stage only the runner-owned Codex authentication and PM MCP configuration required for the run into an isolated owner-only Codex home. Never accept credential values or host paths from the run request, repository, or UI.
- Compose workspace preparation, the existing `DriverRunProcessor`, Podman runtime, Codex SDK driver, controlled validation, and artifact collection behind the scheduler.
- Start the repository-local restricted PM MCP against the isolated checkout.
- After Codex exits, run profile-defined validation in the controlled phase and collect status, bounded output, changed files, diff statistics, and resource usage.
- Produce a bounded Git patch, final agent response, validation report, sanitized event/log export, and artifact manifest.
- Define restart reconciliation, retention, and deterministic cleanup behavior for successful, failed, cancelled, timed-out, and interrupted runs.
- Do not commit, create branches, push, merge, or accept dirty-worktree snapshots in v1.

## Acceptance criteria

- An accepted run progresses beyond queued state through the existing scheduler and driver composition.
- The run workspace exactly matches the requested base SHA before agent execution.
- The agent container has no push credential and cannot access host Git or runner configuration.
- Per-run Codex and PM MCP material is isolated, owner-only, and removed or retained only according to explicit policy.
- Patch and validation artifacts are attributable to the immutable run specification.
- Oversized or binary changes are reported safely without unbounded API payloads.
- Failure or cancellation at every lifecycle phase triggers deterministic cleanup without removing another run's state.
- Runner restart reconciles owned runtime and workspace state without silently rerunning completed work.

## Validation

- Test unknown commits, changed remotes, fetch failure, credential staging, MCP startup failure, binary changes, oversized patches, validation failure, cancellation, timeout, restart recovery, retention, and cleanup.
- Add a disposable local Git remote integration test.
- Add a runner-level execution test using fake agent output before exercising the real Codex SDK profile on Linux.

## Notes

- 2026-07-29 07:05 UTC - Implemented the complete runner-owned lifecycle: exact allowlisted Git mirrors and immutable task revision checks, isolated Codex auth, separate agent and credential-free validation containers, bounded evidence collection, resource usage, restart reconciliation, and transient-state cleanup. Local validation passed 48 agent-host tests (one opt-in Podman test skipped) and 307 .NET tests. On the Arch runner, all 49 agent-host tests passed with real rootless Podman. A real Codex SDK run against the private chronium/pm-agent-smoke fixture completed end to end, changed only runner-smoke.txt, passed fresh-container validation, retained its artifacts, and removed workspace/auth/runtime state. The service startup and user-systemd unit also verified successfully.