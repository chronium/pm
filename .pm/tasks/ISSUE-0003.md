---
id: ISSUE-0003
title: Prevent mobile task description and metadata overlap
track: ISSUE
milestone: angular-web
createdAt: 2026-07-28T04:49:48.8409930Z
modifiedAt: 2026-07-28T04:49:48.8409930Z
---

## Goal

Prevent long task descriptions from flowing behind or being obscured by the metadata section on narrow mobile viewports.

## Problem

On the read-only static site, a sufficiently long task description can continue underneath the task metadata region. The dependencies, timestamps, and other metadata then overlap the description and acceptance criteria, making both areas difficult to read.

The layout should remain document-flow-driven: description content must determine its own height, and metadata must begin only after the description has finished.

## Proposed implementation

- Reproduce the issue on a narrow mobile viewport using a task with a long Markdown body.
- Inspect the shared task-detail layout and determine whether the overlap comes from fixed sizing, grid placement, positioning, overflow, or a responsive breakpoint.
- Make the mobile layout a single ordered flow: primary fields, description, dependencies and other metadata, then timestamps.
- Remove or override any mobile height, positioning, or overflow rule that allows metadata to occupy the description's space.
- Preserve the desktop split layout and the existing dialog/full-page component reuse.
- Verify the fix in both the normal Angular application and generated read-only static site when they share the affected component or stylesheet.
- Avoid clipping the Markdown body or introducing nested page-level scrolling.

## Acceptance criteria

- Long Markdown descriptions never render behind metadata at mobile widths.
- Metadata begins below the complete description with clear separation.
- Headings, lists, code, and long acceptance-criteria sections expand naturally.
- The page uses one coherent vertical scroll region on mobile.
- Desktop task dialog and full-page layouts do not regress.
- Both editable and read-only/static task detail variants behave correctly.
- The layout remains stable when dependencies are absent, short, or numerous.

## Validation

- Add a component or Storybook regression case with a deliberately long task body.
- Add a narrow-viewport browser assertion that the description and metadata bounding boxes do not overlap.
- Run Angular formatting, strict checks, tests, Storybook tests where applicable, production build, and the relevant static-site smoke test.