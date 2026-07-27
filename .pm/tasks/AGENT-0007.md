---
id: AGENT-0007
title: Implement hardened OCI runtime profiles
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
- AGENT-0003
createdAt: 2026-07-27T06:57:01.3238490Z
modifiedAt: 2026-07-27T11:03:42.9498840Z
---

## Goal

Make the container the actual unattended-agent security boundary and expose only named administrator-defined OCI runtime profiles.

## Implementation

- Generalize runner capability discovery from the current Docker-only probe to an installed container-runtime capability without changing repository-controlled policy.
- Implement the first production runtime driver for rootless Podman on Bazzite/Linux; keep the runtime seam open for a later Docker adapter.
- Control Podman only through the trusted runner host and never expose the Podman socket or executable control channel to workers.
- Support named profiles defining immutable image digest, CPU, memory, PID, disk, timeout, network policy, writable volumes, environment allowlist, and validation commands.
- Run containers unprivileged with no privileged mode, no host namespaces, dropped capabilities, `no-new-privileges`, seccomp, SELinux separation, explicit user namespaces, and explicit resource limits.
- Mount only the per-run workspace, temporary directory, isolated Codex home or credential channel, and profile-specific caches.
- Keep the root filesystem read-only where the selected toolchain permits it.
- Provide explicit network profiles and fail closed when a requested profile or container engine is unavailable.
- Ensure cancellation, timeout, runner restart, and abnormal exit trigger deterministic cleanup.
- Provide a rootless user-service deployment path suitable for Bazzite and reference the `guides/agent-host-bazzite` prerequisite guide.

## Acceptance criteria

- Worker containers cannot see the host home, other runs, Git credentials, runner configuration, Podman socket, or Docker socket.
- Every run has explicit CPU, memory, PID, disk, and wall-clock limits.
- Runtime options come only from installed profiles, never arbitrary UI input or repository files.
- The trusted host reports its actual OCI engine and rejects incompatible runtime requests.
- Orphaned containers are detected and reconciled after restart.
- The Bazzite host can run the integration suite with rootless Podman and cgroup v2.

## Validation

- Add profile validation, engine capability, and Podman command/adapter tests.
- Add Bazzite/Linux integration tests for mounts, user namespaces, SELinux, identity, limits, cancellation, timeout, network policy, and orphan cleanup.
- Verify workers cannot access the host home, runner data, credentials, or container-engine socket.
- Keep Docker compatibility as an adapter-level follow-up unless it can be covered without weakening the Podman-first slice.
- Validate the documented rootless Podman, user systemd, cgroup v2, Tailscale, storage, and Distrobox-boundary prerequisites.