---
title: PM Overview
createdAt: 2026-07-27T06:14:45.2347560Z
modifiedAt: 2026-07-27T06:16:55.9277440Z
---

PM is a local-first project management tool for software projects. Tasks, workflow state, ordering, and wiki pages live in a commit-friendly `.pm/` directory beside the code.

The same project can be operated by a person in the CLI or web UI, by an agent through MCP, or published as a read-only static site. These are adapters over the same application services and files, not separate project models.

## Principles

- **Project state is an artifact.** `.pm/` is intended to be reviewed, committed, and published with the repository.
- **Humans and agents share one model.** CLI, web, MCP, and static export read the same tasks and wiki.
- **Metadata stays structured.** Tracks, milestones, statuses, priorities, and dependencies remain machine-readable while descriptions and wiki bodies use Markdown.
- **Local work stays responsive.** The web app and CLI operate on local files; the optional Worker only coordinates task ID allocation.
- **Public project data is deliberate.** Tasks and wiki pages are not secrets. Local signing keys remain outside the project.

## Operating modes

| Mode | Best for | Writes project data |
| --- | --- | --- |
| CLI | Scripts, terminal workflows, editor-based changes | Yes |
| Web | Dense board work, inline task editing, wiki reading and editing | Yes |
| MCP | Agent planning, retrieval, and structured mutations | Yes |
| Static site | Public status, documentation, and demos without a backend | No |

## Core concepts

- A **track** owns the prefix and sequence used for task IDs.
- A **status** describes the current workflow state and is represented by a state reference.
- A **milestone** groups work across tracks and can provide inherited priority.
- A **task** is one Markdown file with structured frontmatter and a Markdown description.
- A **wiki page** is one Markdown file whose directory path forms the wiki hierarchy.

Continue with **Getting Started**, **Tasks, Priority, and Dependencies**, or the **MCP Guide** from the wiki tree.