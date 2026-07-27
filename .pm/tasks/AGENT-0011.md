---
id: AGENT-0011
title: Build the split-screen run supervision workspace
track: AGENT
milestone: agent-runs
dependsOn:
- AGENT-0005
- AGENT-0009
- AGENT-0010
createdAt: 2026-07-27T06:57:02.2240320Z
modifiedAt: 2026-07-27T06:57:21.3796750Z
---

## Goal

Present durable run progress and detailed live output in one focused full-page workspace.

## Implementation

- Add a routed run page with a shared header showing task, run state, elapsed time, runner connectivity, and contextual Cancel/Retry actions.
- Use a stable split layout: a compact left control pane and a dominant independently scrollable right output pane.
- Project lifecycle events into checkpoints for request accepted, workspace preparation, runtime startup, Codex execution, validation, artifact collection, and terminal outcome.
- Keep runner connectivity separate from run progress so reconnecting never implies the remote job stopped.
- Render the right side as a virtualized structured log, not a PTY: sequence, timestamp, source, message, search, source filters, follow toggle, pause, copy, and download.
- Resume from the last sequence, deduplicate replayed events, bound browser memory, and keep full logs downloadable from runner artifacts.
- Sanitize ANSI/control sequences and render all output as text.
- On mobile, replace the split with Progress and Output tabs while keeping status and Cancel visible.

## Acceptance criteria

- Left checkpoints and right output are derived from the same event journal.
- Long runs remain responsive and independently scrollable.
- Disconnect/reconnect resumes without duplicated or missing displayed events.
- Failure and cancellation remain understandable without reading raw logs.
- Keyboard and screen-reader behavior does not announce every high-volume log line.

## Validation

- Add component stories for queued, running, reconnecting, failed, cancelled, completed, long-log, and task-drift states.
- Add replay, deduplication, virtualization, responsive, and accessibility tests.
- Run Angular formatting and the complete relevant frontend gates.