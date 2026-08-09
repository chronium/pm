---
title: Generated deliverable timeline
createdAt: 2026-08-09T10:26:50.9789900Z
modifiedAt: 2026-08-09T10:26:50.9789900Z
---

## Idea

Add a dedicated generated Delivery timeline to PM now that milestones are explicit deliverables with lifecycle, activation, progress, and delivery provenance.

The primary visual direction is vertical:

- A vertical line represents the passage of time.
- Deliverable cards alternate to the left and right of the line on wide screens.
- Each card connects to one point on the spine.
- The alternating treatment is presentational only; chronological and semantic order remains linear in the document.
- On narrow screens, the spine moves to one side and every card uses the same content column.

This should feel like a project narrative rather than a task scheduler or dashboard.

## Three timeline concepts

### Delivery history

Delivery history is factual. It uses persisted milestone delivery records and their actual acceptance timestamps.

It can show:

- Delivered milestone title and description.
- Delivery timestamp and mode.
- Accepted task count and exceptional-delivery reason where applicable.
- Evidence or provenance retained by the delivery record.
- Reopened delivery events if PM later retains an explicit event history.

History must never be reconstructed from task completion timestamps when no delivery record exists.

### Delivery path

The delivery path is forward-looking but not calendar-scheduled. PM generates it from current project semantics:

- Milestone lifecycle.
- Activation requirements and unmet gates.
- Milestone and task requirements that connect deliverables.
- Current task progress.
- Priority when a deterministic presentation choice is needed.
- Manual activation or override provenance.

Upcoming deliverables receive relative placement, such as active, next, later, or parallel. They do not receive invented dates.

When the activation graph branches, the timeline must represent parallel or alternative-looking lanes honestly rather than forcing a false total order. A central present marker may separate delivered history from active and upcoming work.

### Calendar roadmap

A calendar roadmap is a later optional capability. It requires explicit project-authored target dates or target windows.

The model must distinguish:

- Actual delivery timestamps.
- Authored target dates or windows.
- Generated relative delivery order.

PM must never infer a calendar date from priority, task count, progress percentage, activation state, or graph position.

## Card contents

A deliverable card may present:

- Title, key, and concise deliverable description.
- Lifecycle: inactive, active, ready to deliver, or delivered.
- Actual delivery time or explicitly authored target window.
- Completed and assigned task counts.
- Unmet activation gates.
- Manual override provenance where relevant.
- A link to the milestone’s tasks or deliverable details.

Task lists should remain behind progressive disclosure; the timeline is organized around deliverables, not individual work items.

## Generation rules

1. Order delivered milestones by their persisted delivery timestamps.
2. Place current active and ready-to-deliver milestones at the present boundary.
3. Resolve upcoming relationships from the activation graph without treating ordinary task dependencies as activation edges.
4. Preserve branches where multiple deliverables can proceed independently.
5. Use priority only as a stable presentation tie-breaker, never as a claim about dates or duration.
6. Show disconnected or manual-only deliverables honestly instead of inventing prerequisite relationships.
7. Keep manual overrides visible as provenance, even after waived requirements later become satisfied.

## Interaction and presentation

The first version should be a dedicated `/timeline` view in both the live application and static publication.

Potential interactions include:

- Filtering delivered, current, and upcoming deliverables.
- Expanding a card for activation requirements and delivery evidence.
- Selecting a deliverable to open its scoped task board.
- Moving between deliverables with keyboard navigation.
- Later, selecting linked projects or displaying project lanes.

The vertical spine and connector lines are decorative and must not carry information unavailable in text.

## Responsive and accessibility contract

- DOM order follows the resolved temporal and graph reading order.
- Alternation is applied with CSS and never changes focus or screen-reader order.
- Mobile and narrow layouts place all cards on one side of the spine.
- Status and provenance are communicated with text as well as color.
- Long descriptions, empty histories, a single deliverable, dense futures, and large branches remain readable.
- Reduced-motion preferences are respected if transitions are introduced.
- Static export retains the same semantics without requiring a backend.

## Boundaries

The timeline is not:

- A task-level Gantt chart.
- An estimation system.
- A resource or personnel scheduler.
- A license to infer deadlines.
- Initially another configurable Overview section.
- Part of the current published Overview discovery milestone.

The standalone timeline model should be approved before deciding whether a compact delivery-path summary belongs on the Overview.

## Discovery questions

- What is the clearest visual treatment for branching future deliverables while retaining one vertical time spine?
- Does PM need an append-only delivery/reopen event history before history can be considered complete?
- Should target scheduling use a single date, a date window, or both?
- How should milestones with no activation relationship be positioned?
- When should linked-project deliverables appear as lanes rather than separate timelines?
- What information belongs on the card versus behind expansion?
- What is the deterministic reading order for parallel future branches?