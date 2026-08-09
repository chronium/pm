---
id: DISCOVERY-0011
title: Specify Overview routing and resolved presentation data
track: DISCOVERY
milestone: site-overview-discovery
dependsOn:
- DISCOVERY-0010
createdAt: 2026-08-09T07:14:47.0524080Z
modifiedAt: 2026-08-09T07:14:56.6735770Z
---

## Goal

Define how one Overview composition is resolved consistently for live, embedded, and static PM modes.

## Explore

- Keep Tasks as the default live and embedded route while exposing Overview in navigation for opted-in projects.
- Make Overview the default route for opted-in static exports.
- Resolve the featured milestone as the highest-priority active milestone, using configured milestone order as the tie-breaker, unless the section names one explicitly.
- Reuse existing task-search predicates and normal project task ordering.
- Define the minimum transport-neutral data needed by each section without leaking configuration or file access into presentation components.
- Cover disabled sites, empty projects, missing eligible milestones, read-only mode, loading, and validation failures.

## Deliverable

A settled route, resolution, and read-model contract suitable for isolated fixtures now and production services later.