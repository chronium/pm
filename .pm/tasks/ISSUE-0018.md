---
id: ISSUE-0018
title: Refresh frontend transitive dependencies for disclosed CVEs
track: ISSUE
createdAt: 2026-08-07T06:53:23.6222950Z
modifiedAt: 2026-08-07T06:53:23.6222950Z
---

Update the frontend lockfile so the affected transitive packages resolve to patched releases without broad dependency upgrades.

Acceptance criteria:
- Undici resolves to patched 6.28.x and 7.29.x releases or later compatible versions.
- PostCSS resolves to 8.5.23 or later.
- Socket npm installation completes without accepting the identified CVE risks.
- Frontend and release validation pass.
- No unrelated package manifest changes are introduced.