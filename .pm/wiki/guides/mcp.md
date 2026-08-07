---
title: MCP Guide
createdAt: 2026-07-27T06:14:45.2638910Z
modifiedAt: 2026-08-07T13:25:59.3549600Z
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

`get_activation_switchboard` is the authoritative structured read for milestone deliverables and activation triggers. Activation provenance remains separate from current requirement satisfaction, so clients can distinguish pending, automatically active, latched-with-unmet-requirements, and overridden states.

Trusted MCP clients may pass `project` to `get_project`, `list_milestones`, or `get_activation_switchboard`. The selector accepts `current`, `parent`, a stable project ID, or a unique linked-project alias. Each successful response preserves its established `data` shape and adds top-level `project` ownership metadata plus structured `warnings`; activation requirements are always resolved from the selected project's own tasks and milestones. These tools select one project rather than aggregating a family.

Trigger requirements are typed `task` or `milestone` references combined with AND. Trusted definition tools add, rename, remove, attach, detach, or replace inactive requirements. Active requirements use a guarded workflow: call `preview_activation_trigger_redefinition`, then pass its revision to `redefine_activation_trigger` and explicitly allow eligibility loss when required.

Manual-only triggers use `activate_activation_trigger`; unmet factual requirements use `override_activation_trigger` with a public reason. Reset is available only while current requirements are not all satisfied. `reconcile_activation_triggers` dry-run previews missing automatic latches and the normal call persists them without deactivating anything.

Milestone delivery also uses preview and apply: call `preview_milestone_delivery`, then `deliver_milestone` with the returned revision. Exceptional delivery requires a reason and explicit confirmation. `reopen_milestone` removes delivery provenance and re-evaluates the milestone's gates.

Every successful activation mutation returns a project mutation receipt and a refreshed switchboard. Clients should compare that switchboard with an immediate `get_activation_switchboard` reread when authoritative post-mutation state matters. Rebuilds do not hot-reload an already running MCP process; restart the server before diagnosing changed code.

These are normal-profile control-plane operations. A run-worker advertises `get_project`, `list_milestones`, and `get_activation_switchboard` for current-project context, but linked selectors return `mcp_project_scope_denied`. It does not advertise trigger definition, activation, override, reset, redefine, delivery, reopening, or reconciliation tools. The tool implementations retain the same denial guard as defense in depth.

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
