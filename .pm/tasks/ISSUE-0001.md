---
id: ISSUE-0001
title: Remove empty status directories when deleting statuses
track: ISSUE
createdAt: 2026-07-04T09:04:42.1562330Z
modifiedAt: 2026-07-04T09:04:42.1562330Z
---

When a status is removed, ProjectConfigService.RemoveStatus removes it from pm_config.yaml but leaves the empty .pm/states/<status-key>/ directory behind. Deletion should remove the empty state directory after confirming there are no assigned task refs, keeping config and state folders tidy. Add service tests for directory removal and keep blocked deletes unchanged.
