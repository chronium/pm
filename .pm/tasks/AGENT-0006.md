---
id: AGENT-0006
title: Integrate the Codex SDK agent driver
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0002
- AGENT-0003
- AGENT-0005
createdAt: 2026-07-27T06:57:01.1030050Z
modifiedAt: 2026-07-27T06:57:21.3442530Z
---

## Goal

Execute one Codex task inside the run environment and convert SDK activity into PM runner events.

## Implementation

- Add the server-side Codex SDK through a Socket-reviewed dependency change.
- Start a Codex thread from an immutable run specification using the advertised model, effort, workspace, `approval_policy = never`, and `workspace-write` sandbox.
- Configure the restricted PM MCP server as required and fail the run if it cannot initialize.
- Build the task-execution prompt from the assigned task, repository instructions, base commit, authority rules, and validation expectations.
- Convert thread, turn, agent message, command, file-change, MCP, usage, and failure activity into normalized runner events.
- Keep raw credentials out of the repository, child logs, events, and artifacts.
- Record the Codex thread ID for diagnostics and future continuation without exposing app-server transport through the runner protocol.

## Acceptance criteria

- A fake or isolated workspace can complete a Codex run and stream normalized events.
- The driver cannot silently continue when required PM MCP initialization fails.
- Approval requests do not block unattended execution.
- A Codex conclusion does not mark the PM task complete.
- Cancellation reaches the active Codex run and produces one terminal result.

## Validation

- Add mocked SDK lifecycle, error, cancellation, MCP failure, and redaction tests.
- Add an opt-in credentialed smoke test that is excluded from normal CI.
- Run runner formatting, strict checks, and tests.