---
id: AGENT-0007
title: Implement hardened Docker runtime profiles
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
- AGENT-0003
createdAt: 2026-07-27T06:57:01.3238490Z
modifiedAt: 2026-07-27T06:57:21.3515370Z
---

## Goal

Make the container the actual unattended-agent security boundary and expose only named administrator-defined runtime profiles.

## Implementation

- Implement Docker lifecycle through the trusted runner host without exposing the Docker socket to workers.
- Support named profiles defining immutable image reference, CPU, memory, PID, disk, timeout, network policy, writable volumes, environment allowlist, and validation commands.
- Run containers unprivileged with no privileged mode, no host namespaces, dropped capabilities, `no-new-privileges`, seccomp, and explicit resource limits.
- Mount only the per-run workspace, temporary directory, isolated Codex home or credential channel, and profile-specific caches.
- Keep root filesystem read-only where the selected toolchain permits it.
- Provide explicit network profiles and fail closed when a requested profile is unavailable.
- Ensure cancellation, timeout, runner restart, and abnormal exit trigger deterministic cleanup.

## Acceptance criteria

- Worker containers cannot see the host home, other runs, Git credentials, runner configuration, or Docker socket.
- Every run has explicit CPU, memory, PID, disk, and wall-clock limits.
- Runtime options come only from installed profiles, never arbitrary UI input or repository files.
- Orphaned containers are detected and reconciled after restart.

## Validation

- Add profile validation and Docker command/adapter tests.
- Add Linux integration tests for mounts, identity, limits, cancellation, timeout, and orphan cleanup.
- Document rootless Docker and cgroup v2 prerequisites.