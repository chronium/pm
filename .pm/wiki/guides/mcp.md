---
title: MCP Guide
createdAt: 2026-07-27T06:14:45.2638910Z
modifiedAt: 2026-08-06T18:00:25.0001360Z
---

PM exposes a Model Context Protocol server over standard input/output. It gives coding agents structured access to the same services used by the CLI and web API.

## Start the server

```sh
pm mcp
```

An MCP client should launch that command with its working directory set inside the PM project. Project discovery is based on the server process working directory.

A representative client configuration is:

```json
{
  "mcpServers": {
    "pm": {
      "command": "dotnet",
      "args": ["/absolute/path/to/PM.dll", "mcp"],
      "cwd": "/absolute/path/to/project"
    }
  }
}
```

The exact configuration shape is client-specific. The important pieces are the executable, the `mcp` argument, and the project working directory.

## Tool groups

| Group | Representative tools |
| --- | --- |
| Project | `get_project`, `create_project`, `validate_project` |
| Retrieval | `list_tasks`, `get_task`, `search_tasks`, `get_next_task` |
| Task writes | `create_task`, `move_task`, `update_task_metadata`, `update_task_markdown`, `append_task_note`, `remove_task` |
| Bulk and order | `bulk_create_tasks_for_track`, `bulk_assign_tasks_to_milestone`, `reorder_tasks` |
| Wiki | `list_wiki_pages`, `get_wiki_page`, `search_wiki_pages`, `outline_wiki_page`, `create_wiki_page`, `patch_wiki_page`, `rename_wiki_page`, `remove_wiki_page` |
| Configuration | track, milestone, and status list/add/rename/remove tools plus milestone priority |
| Milestone activation | `get_activation_switchboard`, trigger definition and transition tools, milestone delivery and reopening, activation reconciliation |

## Recommended agent workflow

1. Call `get_project` to learn the valid tracks, milestones, and statuses.
2. Call `get_next_task` with `readyOnly: true` when choosing autonomous work. A user-directed task may legitimately override that recommendation.
3. Call `get_task` before editing and read its dependencies and full description.
4. Move the task to the active status.
5. Implement and validate the work in the repository.
6. Update the task body or append a note when durable context belongs with the task.
7. Move the task to the completed status in the same change set as the implementation.

Without `readyOnly`, `get_next_task` may return the best blocked candidate when no dependency-ready task exists. Always inspect `dependenciesReady` and `waitingOnDependencies` before treating a result as actionable.

## Milestone activation

`get_activation_switchboard` is the authoritative structured read for milestone deliverables and activation triggers. Trigger activation provenance is separate from current requirement satisfaction, so clients can distinguish a pending trigger, an automatic activation that remains latched after a requirement reopens, and an override whose requirements were later satisfied. The same payload includes milestone descriptions, lifecycle state, required and unmet triggers, delivery provenance, and coded validation issues.

Trigger requirements are typed objects with `kind` set to `task` or `milestone` and a local source ID or key. Definition tools add, rename, remove, attach, detach, or replace inactive requirements. Active requirements use the guarded workflow: call `preview_activation_trigger_redefinition`, then pass its revision to `redefine_activation_trigger` and explicitly allow deactivation when the preview requires confirmation.

Manual-only triggers use `activate_activation_trigger`; triggers with unmet requirements use `override_activation_trigger` with a reason. Reset is available only while the current requirements are not all satisfied. `reconcile_activation_triggers` persists missing automatic activation records, while its dry-run mode reports the prospective impact without writing.

Milestone delivery follows the same guarded pattern: call `preview_milestone_delivery`, then pass its revision to `deliver_milestone`. Exceptional delivery requires a reason and explicit confirmation. `reopen_milestone` removes the delivery record and re-evaluates the milestone's activation state.

These definition and lifecycle mutations are control-plane operations available only in the normal trusted MCP profile. Isolated run workers may read the switchboard but cannot preview or invoke activation, override, reset, redefine, delivery, reopening, or reconciliation operations.

## Safe wiki patching

For narrow changes, first call `outline_wiki_page`. It returns heading IDs and a body version. Pass both to `patch_wiki_page` with one of these operations:

- `append_to_section`
- `prepend_to_section`
- `replace_section_body`
- `insert_before_heading`
- `insert_after_section`

The version guard prevents an agent from silently overwriting a page that changed after it was inspected. Use `update_wiki_page_markdown` only when replacing the full document is intentional.

## Mutation boundaries

MCP write tools mutate `.pm/` immediately. Destructive tools are marked as destructive in their schemas, but confirmation behavior belongs to the MCP client. Review diffs and run `validate_project` before committing.
