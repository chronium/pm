---
title: Linked Projects
createdAt: 2026-07-31T15:55:14.2753290Z
modifiedAt: 2026-07-31T15:55:38.1967610Z
---

# Linked Projects

Linked projects let independent PM projects discover and read one another without merging their task, wiki, or configuration files. Each repository remains a complete standalone project.

## Recommended layout

A parent repository can keep child repositories as Git submodules:

```text
games/
  .pm/
  royale/
    .pm/
  starfall/
    .pm/
```

The parent declares children in `.pm/linked_projects.yaml`. Each child declares exactly one parent. Declarations use stable project IDs for identity, an alias for commands and UI labels, a repository URL, and an optional relative `pathHint`.

## Set up a family

1. Initialize every repository as its own PM project and commit its `.pm` directory.
2. Add the children as submodules of the parent repository.
3. Add matching parent and child declarations in each project's linked-project settings.
4. Open each checkout once, or bind it explicitly with `pm project bind`, so PM can remember the verified local path.
5. Run `pm doctor` from the parent and each child.

Use `pm project links` to inspect current, parent, child, and sibling resolution. Reads may select a project by `current`, `parent`, stable project ID, or unique alias:

```sh
pm list --project starfall
pm list --family
pm task search "network protocol" --family
pm wiki search "rendering contract" --family
pm wiki show architecture/rendering --project royale
pm task next --family
```

MCP read tools expose the same `project` and `family` selection model. Results retain the owning project ID so callers do not confuse identical local task or wiki paths.

## References and writes

Use canonical references when one project points at another:

```text
pm://project/<project-id>/task/<task-id>
pm://project/<project-id>/wiki/<wiki-path>
```

Linked projects are read-only by default. Grant local write trust deliberately:

```sh
pm project trust starfall
pm task note STAR-0001 "Validated by Royale." --project starfall
pm project untrust starfall
```

Trust is local to the machine and project. It is not inherited through the family and does not coordinate multi-repository commits.

## Clone and recovery

After cloning the parent, initialize its children:

```sh
git submodule update --init --recursive
pm doctor
```

An uninitialized, missing, or unavailable linked project is a warning rather than a fatal project error. The active project continues to work, family reads return available members, and `pm doctor` reports a targeted repair command such as:

```sh
git submodule update --init -- royale
```

A child cloned without its parent also remains a valid standalone PM project. Parent and sibling navigation becomes available again when those repositories are present or explicitly bound.

## Static sites and agent context

Static exports do not bundle sibling sites. A linked project with `publicSiteUrl` appears as an external project destination.

Agent runs may attach published linked wiki contexts. PM requires each selected linked repository to be clean, to have a credential-free origin URL, and to have its exact HEAD published on an origin branch. Required unavailable contexts block preflight; optional unavailable contexts are omitted with a visible check.
