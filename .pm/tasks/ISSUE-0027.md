---
id: ISSUE-0027
title: Publish a favicon with PM web artifacts
track: ISSUE
createdAt: 2026-08-09T19:53:35.1270950Z
modifiedAt: 2026-08-09T19:53:35.1270950Z
---

## Goal

Serve a PM favicon consistently from live, embedded, and static builds without browser console errors.

## Problem

The Project Model Overview dogfood review found that both the embedded application and generated static site request `/favicon.ico` and receive a 404. The missing asset does not block the interface, but it produces avoidable console noise and leaves published sites without an intentional browser identity.

## Acceptance criteria

- Live, embedded, and backend-free static PM pages expose an intentional favicon through path-safe generated markup.
- Static exports hosted below a repository path do not request an absolute root asset.
- Browser smoke coverage proves the favicon request succeeds without weakening unrelated console-error detection.
- The favicon uses the approved PM branding available when this task is implemented; do not invent new branding as part of this fix.