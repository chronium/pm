---
id: DISCOVERY-0010
title: Define the opt-in Overview configuration contract
track: DISCOVERY
milestone: site-overview-discovery
createdAt: 2026-08-09T07:14:46.8219870Z
modifiedAt: 2026-08-09T07:14:51.2251750Z
---

## Goal

Settle the YAML contract that lets a project opt into a composed Overview without exposing arbitrary layout or styling controls.

## Explore

- Define the dedicated `site:` and `site.home.sections` shapes using uniform section objects.
- Specify optional title and description fallbacks.
- Define hero, milestone, tasks, wiki, and Markdown section fields and defaults.
- Keep `site.enabled: true` as the explicit opt-in; absent configuration preserves current behavior.
- Define actionable validation for unsupported section types, invalid references, malformed filters, duplicates, and unsafe values.
- Document compatibility and migration expectations for future schema evolution.

## Deliverable

An approved configuration contract with representative minimal, fully configured, and invalid examples.