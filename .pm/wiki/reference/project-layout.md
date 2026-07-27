---
title: Project Layout
createdAt: 2026-07-27T06:14:45.2754500Z
modifiedAt: 2026-07-27T06:14:45.2754500Z
---

A PM project is represented by a `.pm/` directory discovered from the current working directory or one of its parents.

```text
.pm/
├── pm_config.yaml
├── project_id.txt
├── task_order.yaml
├── tasks/
│   └── BUILD-0001.md
├── states/
│   ├── todo/
│   │   └── BUILD-0001.ref
│   └── done/
└── wiki/
    └── guides/
        └── setup.md
```

`task_order.yaml` is created only when explicit ordering is stored. `project_id.txt` exists when the hosted next-ID service is used.

## Configuration

`pm_config.yaml` contains:

- project name and ID width
- default ID prefix
- next-ID service URL
- status key/display-name pairs
- track key/display-name pairs
- milestone key/title pairs
- optional milestone priorities

Keys are stable machine identifiers. Renaming a track, milestone, or status changes its display title without rewriting the key.

## Tasks and state references

Each task lives in `.pm/tasks/<ID>.md`. Its current status is represented by exactly one `.ref` file under `.pm/states/<status>/`. Moving a task changes the reference, not the task filename.

This separation keeps task content stable while workflow state remains easy to inspect and diff.

## Wiki hierarchy

Wiki directory structure is navigation structure. `.pm/wiki/reference/tasks.md` has the path `reference/tasks` and appears under the `reference` folder in the web sidebar.

## Public and private data

Everything under `.pm/` is intended to be a public project artifact, including `project_id.txt`. Do not put secrets in task descriptions, wiki pages, or configuration.

The signing identity is outside the repository:

- macOS: `~/Library/Application Support/pm/identity.json`
- Linux: `$XDG_CONFIG_HOME/pm/identity.json` or `~/.config/pm/identity.json`
- Windows: `%APPDATA%\pm\identity.json`

Set `PM_IDENTITY_PATH` to override that location. The identity file contains the private key and must not be committed.