---
title: CLI Guide
createdAt: 2026-07-27T06:14:45.2598480Z
modifiedAt: 2026-07-27T06:14:45.2598480Z
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

```sh
pm track add API "API and protocol"
pm track rename API "Application API"
pm milestone add m3 "Public API"
pm milestone priority m3 high
pm milestone list
pm status add review "In review"
pm status rename review "Review"
```

A track, milestone, or status can only be removed when it is no longer referenced by tasks.

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