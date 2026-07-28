---
id: DISCOVERY-0003
title: Explore supervised host execution for GUI and GPU workloads
track: DISCOVERY
createdAt: 2026-07-28T06:01:16.6197970Z
modifiedAt: 2026-07-28T06:01:16.6197970Z
---

## Idea

Explore a supervised host-execution capability for remote agent runs that need a real desktop session, consumer GPU access, or other hardware and GUI facilities that are unavailable or impractical inside the isolated worker container.

A motivating case is a game project developed by Codex inside a container on a Linux/Bazzite runner with an RTX 4090. The game has a test harness that launches the application and captures screenshots, but that harness must execute on the host. The agent still needs a safe way to request the run, observe progress, receive screenshots and logs, and use the evidence while implementing the task.

## Desired workflow

- Codex edits and reasons inside its isolated run workspace.
- Codex requests a named host capability such as `build-game`, `run-render-smoke`, or `capture-scene`.
- The trusted runner validates the request against an administrator-defined profile and executes it outside the worker container.
- The host operation uses the exact run revision or synchronized run workspace rather than an unrelated checkout.
- Logs, exit status, screenshots, videos, crash dumps, and validation results become sequenced run events and artifacts.
- Codex can inspect returned artifacts and continue the same run without receiving general host-shell access.
- Cancellation, timeout, runner restart, and application crashes produce deterministic cleanup and terminal status.
- PM clearly distinguishes container execution from privileged host-assisted validation in the run timeline and UI.

## Questions to resolve

- Is the right abstraction a host capability broker, validation sidecar, privileged runner action, or a separate hardware runner class?
- How does a container workspace become available to a host process without exposing unrelated host files or allowing path traversal?
- Should the host build from the agent workspace, receive a patch into a clean host checkout, or consume immutable build artifacts produced by the container?
- How are named operations declared, installed, versioned, and audited without allowing repository-controlled arbitrary host commands?
- Which arguments may an agent supply, and how are paths, environment variables, working directories, ports, and output locations constrained?
- How are GUI applications launched into a logged-in Wayland/X11 session, a dedicated compositor, or an isolated virtual display?
- Can the runner use NVIDIA CDI or another container GPU path for some workloads, and when is true host execution still required?
- How should Bazzite, SELinux, systemd user services, NVIDIA drivers, Steam/runtime dependencies, audio, input, and display ownership affect the design?
- How are screenshots and other visual artifacts returned to Codex in a form it can inspect during the active run?
- Can an agent request mouse or keyboard input, and if so, what capability and confirmation boundary is required?
- How are host application windows prevented from interfering with the operator's active desktop session?
- What concurrency rules prevent several agents from competing for the GPU, display, ports, controllers, or exclusive project resources?
- How are CPU, GPU memory, wall-clock time, disk output, and orphaned processes measured and limited?
- Which host-assisted operations may run unattended, which require per-run confirmation, and which must remain prohibited?
- How are secrets, the user's home directory, Git credentials, runner configuration, and unrelated processes kept outside the operation's visibility?
- How should the protocol represent progress, screenshots, failure categories, and retryability without exposing arbitrary process control?
- Should host capability availability participate in runner capability discovery and future task routing?

## Candidate model to evaluate

Treat host execution as an explicit trusted-runner capability, not as an escape hatch from the worker container:

- Administrators install immutable named host-operation profiles.
- A profile owns the executable, fixed command shape, allowed arguments, environment allowlist, workspace mapping, display/GPU requirements, resource limits, output contract, and cleanup policy.
- Repository files may request an installed profile but cannot define or modify the host command.
- The runner records every request, normalized command summary, caller run, timestamps, outputs, and result.
- Host operations execute under a dedicated unprivileged account or constrained user service where the GUI/GPU stack permits it.
- Only declared artifacts return to the worker workspace; no general host filesystem or shell channel is exposed.
- PM policy can disable host execution globally, per runner, per project, or per profile.

## Threats and failure modes

The discovery must explicitly address:

- Prompt-injected repositories attempting arbitrary host execution.
- Argument, environment, path, and artifact-name injection.
- Symlink and workspace-escape attacks.
- Host process inheritance of credentials or desktop-session authority.
- Malicious screenshots, logs, or artifact payloads flowing back into the agent.
- Orphaned GUI processes after cancellation or runner restart.
- Concurrent runs racing over the same checkout, display, GPU, ports, or output paths.
- An agent using input automation against unrelated desktop applications.
- Driver crashes, GPU hangs, compositor failures, and unrecoverable host state.
- Results produced from stale source or a different task revision.

## Discovery deliverable

Produce a design note with:

- A recommended trust boundary and protocol between worker, runner, and host-operation broker.
- At least two workspace/artifact exchange models and their security tradeoffs.
- A proposed named capability/profile model with example game build, launch, screenshot, and shutdown operations.
- Bazzite/Wayland/NVIDIA feasibility findings, including whether dedicated desktop sessions or headless compositors are practical.
- Event, artifact, cancellation, timeout, restart, and concurrency behavior.
- Operator confirmation and audit policy.
- An end-to-end sequence for a Codex run that modifies a game, requests a GPU screenshot harness, inspects the result, and continues.
- A narrow implementation slice that can be tested on the Linux RTX 4090 host without granting the agent arbitrary host access.

## Acceptance criteria

- The proposal enables useful GUI/GPU validation without giving Codex a host shell, Docker/Podman socket, home-directory access, or unrestricted desktop control.
- The exact source revision used by the host operation is unambiguous.
- Returned visual evidence can be consumed during the active agent run.
- Host capability discovery can inform runner selection and future task routing.
- Security, cleanup, concurrency, and operator-impact tradeoffs are explicit.
- The design is validated against the existing game screenshot-harness scenario before implementation tasks are created.

This item records the capability and its risks only. Do not implement host execution until the trust boundary, desktop-session model, workspace exchange, and operation profile contract are decided.