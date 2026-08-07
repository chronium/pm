---
title: CLI Guide
createdAt: 2026-07-27T06:14:45.2598480Z
modifiedAt: 2026-08-07T08:52:57.5309200Z
---

The CLI is the direct terminal adapter over PM's application services. Run it from the project directory or any descendant.

## Project and workflow commands

| Command | Purpose |
| --- | --- |
| `pm init` | Interactively initialize `.pm/` |
| `pm list` | List tasks, optionally filtered by state, track, or milestone |
| `pm move <task-id>` | Select a new status for a task |
| `pm doctor` | Validate project files and references |
| `pm web` | Serve the embedded Angular app |
| `pm site build` | Export a read-only static site |

Commands that expose `--dry-run` can preview their work without writing files.

## Tasks

```sh
pm task add "Compile shaders" --track RENDER --milestone m2 \
  --description "Compile the platform shader variants."
pm task add "Write migration notes" --track DOCS --edit
pm task metadata RENDER-0001 --priority high --depends-on BUILD-0002,BUILD-0003
pm task search "shader state:todo track:RENDER in:all" --limit 20
pm task edit RENDER-0001
pm task remove RENDER-0001
```

`task metadata` can update title, track, milestone, priority, dependencies, and description without replacing the full file. Use an empty milestone or dependency value to clear it. Priority accepts `inherit`, `none`, `low`, `medium`, `high`, or `urgent`.

`task edit` opens the complete Markdown file. Editor selection follows `$VISUAL`, then `$EDITOR`, and falls back to `vim`.

Task search combines free text with predicates:

- `state:<key>`
- `id:<id-or-numeric-suffix>`
- `track:<key>`
- `milestone:<key>`
- `in:selection` or `in:all`

Multiple predicates narrow the result together. Repeating a predicate such as `state:todo state:review` allows either value.

## Tracks, milestones, and statuses

~~~sh
pm track add API "API and protocol"
pm track rename API "Application API"
pm milestone add public-beta "Public beta"
pm milestone priority public-beta high
pm milestone list
pm status add review "In review"
pm status rename review "Review"
~~~

A track, milestone, or status can only be removed when it is no longer referenced. Milestones are structured deliverables; edit their description and required gates through the web settings experience or the corresponding trusted API/MCP operations.

Activation triggers use typed AND requirements:

~~~sh
pm trigger add beta-entry "Beta entry" \
  --requirements task:FOUNDATION-0001,task:FOUNDATION-0002,milestone:architecture-approved
pm trigger attach beta-entry public-beta
pm trigger list
pm trigger activate beta-entry --reason "Proceed with the reviewed beta risk."
pm trigger reset beta-entry
pm trigger redefine beta-entry --requirements task:FOUNDATION-0003 --yes
~~~

`trigger activate` performs a normal activation for a manual-only trigger and requires `--reason` when it overrides unmet requirements. Reset is rejected while all automatic requirements are satisfied. Redefining an active trigger always previews eligibility impact and requires confirmation when work would become inactive.

Repository edits or interrupted mutations can leave satisfied requirements without a persisted latch. Recover explicitly:

~~~sh
pm doctor
pm trigger reconcile --dry-run
pm trigger reconcile
~~~

Milestone completion and delivery are separate:

~~~sh
pm milestone deliver public-beta
pm milestone deliver public-beta --reason "Accept remaining validation for dogfood." --yes
pm milestone reopen public-beta
~~~

Exceptional delivery records its public reason and accepted unfinished-task snapshot. Reopening removes the delivery record and re-evaluates current gates.

## Wiki

```sh
pm wiki list
pm wiki show guides/cli
pm wiki search "metadata" --limit 20
pm wiki create guides/release --title "Release guide" --body "## Checklist"
pm wiki edit guides/release
pm wiki rename guides/release --path guides/publishing --title "Publishing guide"
pm wiki remove guides/publishing --yes
```

`wiki create --edit` and `wiki edit` open the complete page file, including frontmatter. The web UI intentionally edits only the body and provides separate metadata controls.