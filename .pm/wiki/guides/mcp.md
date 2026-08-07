---
title: MCP Guide
createdAt: 2026-07-27T06:14:45.2638910Z
modifiedAt: 2026-08-07T17:59:48.7696930Z
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

Trigger requirements are typed `task` or `milestone` references combined with AND. Milestone definition tools (`add_milestone`, `rename_milestone`, `remove_milestone`, `set_milestone_priority`, and `set_milestone_description`) and inactive trigger definition tools (`add_activation_trigger`, `rename_activation_trigger`, `remove_activation_trigger`, `set_activation_trigger_requirements`, `attach_activation_trigger_to_milestone`, and `detach_activation_trigger_from_milestone`) accept the same `project` selector.

Trigger lifecycle tools also accept `project`: `activate_activation_trigger`, `override_activation_trigger`, `reset_activation_trigger`, `reconcile_activation_triggers`, `preview_activation_trigger_redefinition`, and `redefine_activation_trigger`. A linked target must be locally write-trusted for both previews and mutations. Manual-only triggers use activation; unmet factual requirements use override with a public reason and a target-local waived-requirement snapshot. Reset is available only while current requirements are not all satisfied. Reconciliation dry-run previews missing automatic latches without writing; the normal call persists them and never deactivates an existing latch.

Active requirement changes use a guarded workflow: preview the selected project's trigger, then apply the returned revision to that same project and explicitly allow eligibility loss when required. Redefinition revisions are bound to the stable project ID, or to the canonical repository path for legacy current projects without an ID, so a preview from one project cannot authorize another.

Milestone delivery and reopening also accept `project`. Call `preview_milestone_delivery` against any readable linked project, then pass its revision to `deliver_milestone`; delivery and `reopen_milestone` require local write trust. Ordinary delivery requires all assigned tasks to be done. Exceptional delivery requires a public reason and explicit confirmation, and snapshots only the selected project's unfinished task IDs. Delivery revisions are bound to the selected project, so a preview cannot authorize another project. Reopening removes delivery provenance and re-evaluates that milestone's current gates.

Every change-producing activation or delivery mutation returns the selected project ID and repository-relative paths in its mutation receipt, plus a switchboard reread resolved from that same project. A no-op or reconciliation dry-run returns `changed: false` without a receipt. Clients may compare the returned switchboard with an immediate `get_activation_switchboard` reread when authoritative post-mutation state matters.

These are normal-profile control-plane operations. A run-worker advertises `get_project`, `list_milestones`, and `get_activation_switchboard` for current-project context, but linked selectors return `mcp_project_scope_denied`. It does not advertise trigger definition, activation, override, reset, redefine, delivery, reopening, or reconciliation tools. The tool implementations retain the same denial guard as defense in depth.

Rebuilds do not hot-reload an already running MCP process; restart the server before diagnosing changed code.

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
