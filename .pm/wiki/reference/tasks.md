---
title: Tasks, Priority, and Dependencies
createdAt: 2026-07-27T06:14:45.2793910Z
modifiedAt: 2026-08-07T08:52:57.5225510Z
---

A task combines structured frontmatter with a Markdown description.

```markdown
---
id: RENDER-0002
title: Draw one indexed cube
track: RENDER
milestone: m2
priority: high
dependsOn:
  - BUILD-0003
createdAt: 2026-07-01T10:00:00Z
modifiedAt: 2026-07-01T10:00:00Z
---

Render a cube with indexed geometry and a depth buffer.
```

## IDs and tracks

IDs are allocated per track. A `RENDER` track therefore produces `RENDER-0001`, `RENDER-0002`, and so on. Tasks from multiple tracks may share the same milestone.

## Status

Status is intentionally not stored in task frontmatter. The task's `.ref` file under `.pm/states/` is the authoritative state. Changing state does not rewrite task content or its modified timestamp.

## Milestones

A milestone is a deliverable rather than a task bucket. Its description states the outcome, scope, exclusions, and expected evidence; assigned tasks describe the work used to produce it. Milestone order still contributes to board grouping and recommendation ranking, and a task may remain unassigned.

A milestone with no required activation triggers is active by default. Otherwise all referenced triggers must have persisted activation records before its tasks become recommendation-eligible. Completing every assigned task makes a non-empty milestone ready to deliver; delivery is a separate explicit decision. Delivered milestones remain visible but their remaining tasks are not recommended.

## Priority

Priorities are `none`, `low`, `medium`, `high`, and `urgent`.

A milestone can provide inherited priority to every task assigned to it. A task may:

- inherit its milestone priority
- override it with a specific priority
- explicitly use `none` to suppress inherited priority

The UI and MCP responses expose both the resolved priority and its source.

## Dependencies

`dependsOn` accepts task IDs without track or milestone restrictions. Dependencies are advisory readiness signals: they influence recommendation ranking but do not prevent status changes.

A dependency is ready when its referenced task is in the `done` status. Missing dependency IDs are reported separately from existing unfinished dependencies.

Dependencies do not activate milestones. Activation is evaluated first through the owning project's milestone triggers; dependency readiness is evaluated only for work that is already activation-eligible.

## Next-task ranking

`get_next_task` first removes completed work and tasks assigned to inactive or delivered milestones. Unassigned tasks and tasks in active or ready-to-deliver milestones remain eligible.

PM then ranks eligible candidates by:

1. dependency-ready before blocked
2. resolved priority
3. configured status order
4. configured milestone order
5. explicit task order
6. most recently modified
7. task ID as a deterministic tie-breaker

Use `readyOnly: true` when an agent should receive no result rather than a dependency-blocked fallback. Include-blocked may relax dependency readiness, but it never returns activation-ineligible work. Family recommendations evaluate activation through each task's owning project before federated ranking.

## Linking

The hosted app and static export currently use different Angular location strategies. Use the form that matches the intended publication target:

```markdown
[Open task PM-0008](/tasks/PM-0008) <!-- hosted app -->
[Open task PM-0008](#/tasks/PM-0008) <!-- static export -->
```

The Markdown renderer does not currently rewrite one form into the other, so documentation intended for both modes should prefer page/task names and rely on the sidebar or search. PM does not create backlinks automatically.