---
id: DISCOVERY-0006
title: Explore an alternative web UI visual direction
track: DISCOVERY
priority: low
createdAt: 2026-07-28T21:00:05.2812540Z
modifiedAt: 2026-07-30T07:26:35.3309000Z
---

## Goal

Explore a possible alternative visual direction for the web UI without committing the product to a redesign. The current interface works well, so this investigation should happen only when there is spare capacity and a concrete idea worth testing.

## Approach

- Record the proposed visual direction and the specific problems it is intended to improve before changing production screens.
- Build a small representative prototype in isolation, preferably through Storybook or another disposable comparison surface.
- Include realistic dense states rather than judging the direction from an empty or idealized mockup:
  - a large task board;
  - task detail and editing;
  - wiki reading and navigation;
  - settings or another control-heavy screen;
  - desktop, mobile, light, and dark themes.
- Compare the prototype directly with the current UI for content hierarchy, scanability, information density, accessibility, responsive behavior, interaction clarity, and implementation cost.
- Preserve the existing Linear-inspired principles: content over chrome, restrained semantic color, minimal decoration, stable layouts, and no unnecessary cards, gradients, shadows, or visual noise.
- Do not broadly restyle production components during discovery.
- If the experiment is not clearly better, document the result and keep the current design.

## Expected outcome

Produce a small prototype and a short comparison containing:

- what improved;
- what regressed;
- which parts, if any, are worth adopting;
- whether the direction should be rejected, refined, or split into narrowly scoped implementation tasks.

This task is deliberately very low priority. It should not interrupt functional work or the agent-runner roadmap while the current UI remains effective.

## Notes

- 2026-07-30 07:26 UTC - UI exploration workflow: keep the current A/B comparison available while iterating. Make coherent checkpoint commits as the background, accent system, and other visual layers evolve. Once the direction is settled, consolidate the chosen design, remove visual style A and the temporary comparison controls, clean up experimental infrastructure, and run the full validation suite. The initial checkpoint is commit 5ef68c4.