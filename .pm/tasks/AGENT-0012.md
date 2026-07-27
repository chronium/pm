---
id: AGENT-0012
title: Harden and package the v1 remote runner
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0002
- AGENT-0006
- AGENT-0007
- AGENT-0008
- AGENT-0011
createdAt: 2026-07-27T06:57:02.4472020Z
modifiedAt: 2026-07-27T06:57:21.3861490Z
---

## Goal

Prove the complete Mac control-plane to Linux Docker execution path and make it operable as a personal worker appliance.

## Implementation

- Package `pm-agent-host` for Linux with configuration validation, a systemd unit, data-directory ownership, log rotation, health checks, and upgrade instructions.
- Provide installation and pairing commands without adding automatic privileged host modification.
- Build one pinned reference runtime image containing Codex, PM CLI/MCP, Git, and the required validation toolchain.
- Add an end-to-end test covering pair, preflight, submit, disconnect, runner restart where safe, event replay, Codex/fake execution, validation, artifact retrieval, cancellation, and cleanup.
- Audit container isolation, credential lifetime, event/log redaction, TLS storage, SQLite recovery, resource limits, and network-profile enforcement.
- Document trusted-repository scope, incident response, credential rotation, backup, retention, and known v1 exclusions.
- Keep push, merge, branch creation, live steering, dirty snapshots, milestone execution, and arbitrary runtime options disabled.

## Acceptance criteria

- A run continues while the Mac is disconnected and becomes inspectable after reconnect.
- No credential is present in the returned patch, logs, events, or worker checkout.
- Failed validation produces a completed artifact set without marking the PM task done.
- Reinstall and credential rotation have a documented recovery path.
- The full release gate is reproducible on the target Linux host.

## Validation

- Run runner, .NET, Angular, protocol, Docker integration, and end-to-end gates.
- Perform a manual security review of mounts, processes, network access, secrets, and cleanup before declaring v1 complete.