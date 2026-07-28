---
id: DISCOVERY-0001
title: Explore linked parent, child, and sibling PM projects
track: DISCOVERY
createdAt: 2026-07-27T19:42:50.1035480Z
modifiedAt: 2026-07-27T19:42:50.1035480Z
---

## Idea

Explore a linked-project model in which one PM project can describe shared context and navigate related projects without erasing each project's independent history or authority.

A motivating layout is:

    games/
      .pm/                 shared technology, engine, conventions, and product context
      original-game/
        .pm/               original game's complete history and ongoing work
      second-game/
        .pm/               independent project using much of the same stack

The original game remains active for bug fixes and features. The second game owns its own tasks and wiki, while both can reference relevant shared context and selected task or wiki entries from each other.

## Desired workflows

- Navigate from a child project to its parent context and back.
- Discover and navigate sibling or otherwise linked projects from the main project.
- List, search, inspect, and explicitly update tasks across linked projects.
- Reference tasks, dependencies, milestones, and wiki entries across project boundaries.
- Preserve project-local behavior by default so an ordinary command cannot silently mutate another project.
- Show the owning project clearly in CLI, MCP, web, static exports, search results, and agent context.
- Keep every project's .pm directory independently useful, public, commit-friendly, and portable.
- Allow an existing mature project to remain the source of its own history rather than flattening or migrating it into a new umbrella project.

## Questions to resolve

- Is the relationship hierarchical, a general project graph, or a hierarchy with optional explicit links?
- Are projects discovered from the filesystem, declared in a parent manifest, linked by stable project ID, or some combination?
- Must related projects share a physical parent directory, or can repositories live elsewhere?
- What is the unambiguous reference syntax for a project, task, milestone, track, status, and wiki page?
- How do references survive directory moves, repository renames, clones, and unavailable linked projects?
- Does shared information use inheritance, read-only references, overlays, imports, or ordinary links?
- Which metadata may be inherited, and which must always remain project-owned?
- Can task dependencies cross projects, and how would that affect readiness and next-task ranking?
- How are cycles, nested parents, duplicate aliases, missing projects, and incompatible schemas handled?
- How should cross-project writes be authorized, confirmed, audited, and committed when projects are separate Git repositories?
- How do next-ID allocation, project membership, local identity, remote runners, and restricted run-worker MCP profiles interact with links?
- Should aggregated views be live queries, cached indexes, or explicit snapshots?
- What should static publishing expose when linked projects are public at different locations?
- How should CLI project selectors and MCP schemas make the target project explicit without burdening normal local workflows?

## Design constraints

- Do not make a parent project an invisible source of truth for child-owned task state.
- Do not infer writable trust merely from filesystem proximity.
- Avoid globally unique task IDs as an accidental requirement; project-qualified references should remain possible.
- Missing linked projects must degrade visibly without making the current project unusable.
- Cross-project operations must never partially update several repositories without a clear recovery model.
- Preserve functional consistency across CLI, MCP, and web, while bulk cross-project operations may remain MCP-only if deliberately chosen.

## Discovery deliverable

Produce a design note with candidate models and tradeoffs, a recommended reference format, discovery and trust rules, example CLI/MCP/web workflows, failure behavior, and a migration path that does not alter existing standalone projects. Validate the proposal against the two-game scenario before splitting implementation into tasks.

This item records the concept only. It should not begin implementation until the project topology, reference semantics, and cross-repository mutation boundary are understood.