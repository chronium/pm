---
id: ISSUE-0024
title: Preserve project accent color in static exports
track: ISSUE
milestone: angular-web
createdAt: 2026-08-08T15:59:55.5884590Z
modifiedAt: 2026-08-08T15:59:55.5884590Z
---

## Goal

Ensure a generated static PM site uses the configured project accent color instead of falling back to the default palette.

## Problem

The live and embedded application can resolve project settings through the API, but the backend-free static build appears not to carry the project's accent color into its initial snapshot. Published static pages therefore may render with a different accent from the source project.

## Proposed investigation and fix

- Reproduce the issue using a project with a deliberately non-default accent color.
- Compare live, embedded, and generated static initialization to identify whether the accent is missing from snapshot data, dropped by the static interceptor, or applied too late during bootstrap.
- Carry the configured accent through the established static snapshot/settings contract and apply it through the existing accent service.
- Preserve the current precedence between project configuration and any supported local user preference.
- Keep the static site backend-free and avoid baking project-specific values into shared application source files.
- Verify initial load, deep-link reloads, hash routing, light theme, dark theme, desktop, and mobile.
- Present the generated static result for visual review before running the full release or marking the task done.

## Acceptance criteria

- A static export generated from a non-default-accent project renders with the same effective accent as its live and embedded views.
- The correct accent is visible on first paint without a noticeable fallback-color flash.
- Direct task and wiki deep links retain the accent after reload.
- Light and dark palettes derive from the configured accent using the existing token system.
- Static operation makes no API requests and remains portable.
- Static snapshot and browser coverage fail when the accent is omitted or replaced by the default.
- Existing projects without an explicit accent retain the current default behavior.
- No full release or done-state transition occurs until the static output has been visually reviewed and approved.