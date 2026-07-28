---
id: AGENT-0007
title: Implement hardened OCI runtime profiles
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0001
- AGENT-0003
createdAt: 2026-07-27T06:57:01.3238490Z
modifiedAt: 2026-07-28T18:38:08.1811960Z
---

## Goal

Make the container the actual unattended-agent security boundary and expose only named administrator-defined OCI runtime profiles.

## Implementation

- Generalize runner capability discovery from the Docker-only probe to structured OCI runtime capability reporting.
- Implement the first production runtime adapter for rootless Podman on Linux while preserving the runtime seam for later adapters.
- Control Podman only through the trusted runner host; never expose its socket or executable control channel to workers.
- Finalize protocol 1.0 runtime profiles with:
  - a digest-pinned administrator-installed image;
  - CPU, memory, PID, writable-disk, and wall-clock limits;
  - explicit `offline` or unrestricted `open` networking;
  - fixed logical workspace, Codex home, and temporary paths;
  - a bounded safe environment allowlist;
  - optional profile-owned read-only caches;
  - a non-negotiable container hardening baseline.
- Keep host mount sources, credentials, and secret values outside the public protocol and derive all host paths from the runner data root.
- Run containers as an unprivileged rootless user with private namespaces, dropped capabilities, `no-new-privileges`, seccomp, a read-only root filesystem, bounded tmpfs, and explicit cgroup limits.
- Enforce writable storage on ext4 with a symlink-safe usage watchdog, fixed tmpfs sizes, and a host free-space reserve. Filesystem quota support is not required for v1.
- Fail closed when Linux, rootless Podman 5+, cgroup v2/systemd, seccomp, an installed image digest, or the supported LSM policy is unavailable.
- Reconcile only containers carrying this runner's ownership label and make cleanup idempotent.
- Provide a rootless systemd user-service template and maintain the `guides/agent-host-linux` setup guide.
- Keep `pm-agent-host serve` queue-only until AGENT-0008 supplies immutable Git workspaces and per-run credential material.

## Deliberate v1 boundaries

- Hostname-restricted network profiles require an external proxy boundary and are deferred.
- SELinux-specific labeling is deferred; an SELinux-enabled host fails readiness rather than silently disabling labels.
- The production worker image and toolchain profiles belong to AGENT-0012. This task validates against an administrator-installed digest without pulling.
- Docker compatibility remains an adapter-level follow-up.
- Workers report completion but do not authoritatively complete PM tasks.

## Acceptance criteria

- Worker containers cannot see the host home, other runs, Git credentials, runner configuration, or container-engine sockets.
- Every run has explicit CPU, memory, PID, writable-disk, tmpfs, and wall-clock limits.
- Runtime behavior comes only from a validated installed profile, never arbitrary UI input or repository-provided host paths.
- The trusted host reports its actual OCI engine and rejects incompatible runtime requests.
- Cancellation and teardown are idempotent, and runner-owned orphaned containers are reconciled after restart.
- The Arch Linux integration host runs the opt-in rootless Podman suite with cgroup v2.

## Validation

- Add matching TypeScript and .NET contract validation and canonical hash fixtures.
- Add unit tests for profile rejection, engine discovery, hardened Podman arguments, process execution, disk enforcement, and orphan cleanup.
- Add an opt-in Linux integration test covering non-root identity, read-only root, writable workspace, namespace/network policy, host-path and socket isolation, teardown, and reconciliation.
- Run the complete agent-host validation, .NET build/tests, systemd unit verification, and the opt-in integration suite on `codex@agent-box`.

## Notes

- 2026-07-28 18:38 UTC - Implemented and validated the hardened rootless Podman runtime profile slice.

  Validation evidence:
  - Local agent-host validation: 42 passed, 1 opt-in Podman test skipped.
  - Local .NET build succeeded and all 306 tests passed.
  - Arch Linux host `codex@agent-box`: all 43 agent-host tests passed using the preinstalled digest-pinned Alpine image.
  - Live test verified rootless Podman 6 with cgroup v2/systemd and seccomp; offline/open networking, non-root identity, zero effective capabilities, `NoNewPrivs`, read-only root, host-path/socket isolation, workspace writes, teardown, and orphan reconciliation passed.
  - The rootless systemd user unit passed `systemd-analyze --user verify` with the noninteractive user-bus environment configured.
  - No runner-labeled containers remained after the integration suite.

  V1 intentionally remains queue-only until AGENT-0008 supplies immutable workspaces and per-run credentials. Restricted egress, SELinux labeling, production worker images, and Docker support remain explicit follow-ups.