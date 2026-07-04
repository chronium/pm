---
title: Web Wiki Architecture
createdAt: 2026-07-04T16:42:39.3290110Z
modifiedAt: 2026-07-04T16:42:39.3290110Z
---

# Web Wiki Architecture

The first wiki slice is intentionally read-only in the web UI.

## Route shape

- `/wiki` lists available pages.
- `/wiki/{path}` opens a page by slash-separated path.
- Invalid paths return a normal web error response.

## Rendering

Markdown is emitted as escaped text and rendered client-side with pinned Marked and DOMPurify scripts.