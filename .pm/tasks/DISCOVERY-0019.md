---
id: DISCOVERY-0019
title: Design the generated vertical deliverable timeline
track: DISCOVERY
milestone: deliverable-timeline-discovery
createdAt: 2026-08-09T10:26:51.6141650Z
modifiedAt: 2026-08-09T10:26:51.6141650Z
---

## Goal

Settle the information model and interaction contract for a generated vertical deliverable timeline before visual prototyping or production planning.

## Design

- Define the relationship between factual delivery history, generated delivery path, and optional future calendar roadmap.
- Specify the vertical time spine and alternating left/right deliverable-card presentation without coupling semantic order to visual placement.
- Define the generated ordering and branching rules for delivered, active, ready, inactive, parallel, disconnected, and manual-only milestones.
- Identify the minimum delivery, activation, progress, provenance, and optional target-date data required by each concept.
- Decide whether complete history requires an append-only delivery and reopen event model.
- Define responsive collapse, DOM order, keyboard order, screen-reader semantics, empty states, and static-export behavior.
- Keep the first surface as a dedicated `/timeline` view rather than extending the Overview vocabulary.

## Boundaries

- Do not infer dates, duration, effort, or deadlines from task progress, priority, activation, or graph position.
- Do not design a task-level Gantt chart or resource scheduler.
- Do not implement production APIs, routes, configuration, or UI in this task.
- Treat linked-project lanes and authored calendar targets as explicit decisions, not assumptions.

## Reference

Use `ideas/generated-deliverable-timeline` as the canonical idea record.

## Deliverable

An owner-reviewed design decision that is specific enough to scope the first Storybook prototype task without reopening the foundational timeline semantics.