---
id: ISSUE-0019
title: Remove duplicate activation details from task sidebar
track: ISSUE
milestone: milestone-activation
createdAt: 2026-08-08T08:39:04.3763000Z
modifiedAt: 2026-08-08T08:39:04.3763000Z
---

## Goal

Remove duplicated activation information from the task detail sidebar while preserving a clear explanation of why the task is ineligible.

## Problem

The Activation section currently includes every unmet trigger identifier in the ineligibility sentence and then renders the same identifiers again under **Unmet gates**. This makes a compact diagnostic panel feel repetitive and harder to scan.

For example, it presents both:

`Ineligible: milestone M8 is inactive; unmet activation triggers: authoritative_mana_available, connected_basic_arrow_available.`

and a separate list containing those same two gate identifiers.

## Proposed implementation

- Keep the primary activation status concise: show that the task is ineligible and identify the inactive milestone without embedding the unmet gate identifiers in the sentence.
- Render unmet trigger identifiers once under a count-aware **Unmet gate** or **Unmet gates (N)** label.
- Preserve the existing semantic status color, readable trigger keys, and task-detail information hierarchy.
- Keep eligible, inactive-without-gates, delivered, and ungated task presentations truthful and compact.
- Preserve keyboard, screen-reader, desktop, and narrow-layout behavior.
- Reuse the existing activation response; this is a presentation change and should not require an API contract change.

## Acceptance criteria

- An inactive task with unmet activation triggers does not display any trigger identifier twice.
- The task still clearly communicates that it is ineligible and names the inactive milestone.
- The unmet-gate label uses correct singular/plural wording and exposes the gate count when present.
- Every unmet gate remains individually readable.
- Eligible, ungated, delivered, and empty-gate states retain clear non-duplicative messages.
- Component tests cover multiple gates, one gate, and no unmet gates.
- Desktop and narrow task-detail layouts remain readable without introducing overflow.