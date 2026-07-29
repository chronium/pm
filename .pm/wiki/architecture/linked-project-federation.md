---
title: Linked-Project Federation
createdAt: 2026-07-29T19:13:34.3784459Z
modifiedAt: 2026-07-29T19:26:56.1493200Z
---

This page is the source-of-truth design for linked PM projects. It defines how independently useful projects form a small family for navigation and read federation without sharing ownership or granting implicit write authority.

The words **must**, **must not**, **should**, and **may** are normative.

## Goals

- Let an active project navigate to one parent and the parent's other direct children.
- Preserve every project's independent configuration, history, tasks, wiki, repository, and authority.
- Make project ownership explicit in task, wiki, CLI, MCP, API, web, static-site, and agent-run results.
- Use stable project IDs for persisted identity while retaining friendly aliases and portable path hints for local use.
- Permit partial family reads when some projects are unavailable.
- Require an explicit project target and local trust before mutating a linked repository.
- Leave existing standalone projects unchanged until they opt into linking.

## Non-goals

The first version does not provide:

- general project graphs, multiple parents, or recursive federation through grandchildren
- inherited or overlaid configuration, tracks, milestones, statuses, task state, task order, wiki content, membership, next-ID allocation, or authority
- globally unique task IDs
- automatic filesystem scanning or guessing of writable checkout locations
- cached family indexes or reads from a repository's last committed tree
- coordinated or atomic mutations across repositories
- bundled family static sites
- writable sibling repositories in remote agent runs

Canonical cross-project resources in the first version are tasks and wiki pages. A federated result can report the owning project's local track, milestone, and status, but cross-project track, milestone, and status references are not persisted resources and do not imply inheritance.

## Family model and ownership

A project has zero or one declared parent. A parent owns an ordered list of its direct children. The supported family view is deliberately shallow:

```text
parent
├── active child
└── sibling children
```

When the parent is active, the view contains that parent and its direct children. When a child is active, it contains the child, its optional parent, and the parent's other direct children. Federation must not recursively include a parent's parent or a child's children. A traversal is bounded to 32 distinct projects; exceeding either the depth or count limit produces a warning and ignores the excess declarations.

Each project remains authoritative for all content under its own `.pm/` directory. In particular:

- the parent owns its child declarations and child order
- a child owns its optional parent back-reference
- each project owns its own name, aliases offered to its local family, configuration, tasks, task state, task order, wiki, and public-site location
- user-local configuration owns checkout bindings and write trust

Parent and child declarations should be reciprocal. The parent's child entry is authoritative for membership and sibling order. The child's parent entry enables upward discovery and records which parent it expects. A missing or non-reciprocal declaration is a warning, not permission to rewrite either project and not a reason to reject an otherwise valid active project.

Aliases are unique within one manifest and are never authoritative identity. The same project ID must not occur twice in a child list, and one project ID must not be assigned multiple aliases in that list. A collision encountered while combining manifests makes the ambiguous alias unusable; stable-ID selection continues to work.

## Committed declarations

An opted-in project stores a versioned, human-readable YAML manifest at `.pm/linked_projects.yaml`. Absence of this file means that the project is standalone and preserves all existing behavior.

The manifest contains an optional `parent` entry and an ordered `children` sequence:

```yaml
version: 1
parent:
  projectId: prj_games
  alias: games
  repositoryUrl: https://example.test/games.git
  pathHint: ..
  publicSiteUrl: https://docs.example.test/games/
children:
  - projectId: prj_royale
    alias: royale
    repositoryUrl: https://example.test/royale.git
    pathHint: royale
    publicSiteUrl: https://docs.example.test/royale/
  - projectId: prj_starfall
    alias: starfall
    repositoryUrl: https://example.test/starfall.git
    pathHint: starfall
    publicSiteUrl: https://docs.example.test/starfall/
```

`projectId` is the stable identity from the target's `.pm/project_id.txt` and is the only persisted authority for matching a project. A project must have a stable ID before it can participate in a manifest.

`alias` is a short, case-insensitively unique name for selectors and display. It must not contain `/`, `\`, whitespace, `:`, or URI delimiters. Renaming an alias does not rewrite persisted task or wiki references.

`repositoryUrl` is a portable clone or submodule origin hint. It is not a credential, does not prove identity, and must never be fetched automatically during ordinary resolution.

`pathHint` is an optional relative path from the repository root containing the declaring `.pm/` directory to the linked repository root. It must be normalized, relative, and free of an empty segment or `.` segment. `..` segments are allowed because a child commonly points to its parent, but resolution must still verify the expected project ID. Absolute paths, home-directory forms, environment substitutions, and URI forms are invalid.

`publicSiteUrl` is optional publication metadata. It is used only for navigation in static output and grants neither local read access nor write authority.

Everything in the manifest is public, portable project metadata. Credentials, private keys, absolute checkout paths, and write-trust decisions must not be committed.

## Local registry and path repair

Machine-specific state lives in the existing per-user PM configuration area outside every repository. Its logical records are:

```text
project ID -> canonical local checkout root
project ID -> write trusted: true/false
```

A binding is learned or updated only after PM successfully opens a project and verifies that its `.pm/project_id.txt` matches the stable ID. Merely encountering a path hint or repository on disk must not grant write trust.

For a declared target, resolution uses this order:

1. If the target ID is the active project's ID, use the already discovered active root.
2. Try the user-local registry binding for the target project ID.
3. Try the declaration's `pathHint`, resolved from the declaring repository root.
4. Report the target as unavailable.

An explicit user-local binding overrides a valid portable path hint. This lets a user select the working checkout intentionally while keeping the committed hint as a zero-configuration fallback for conventional layouts and Git submodules.

Every candidate must contain a valid `.pm/` project and its `.pm/project_id.txt` must exactly match the declared project ID. An identity mismatch is never bypassed by alias, directory name, repository URL, or Git remote. Invalid and mismatched candidates are reported and resolution may continue to the next candidate; they are never exposed as the requested project.

PM does not search parent directories, siblings, Git remotes, or arbitrary local checkouts to repair a link. It provides an explicit link-binding command that can:

- inspect declarations, candidate paths, identity checks, and current registry bindings
- bind a verified checkout root to a stable project ID
- replace or remove a stale binding
- report an uninitialized Git submodule and print a safely quoted `git submodule update --init -- <path>` suggestion

The command must not initialize a submodule, clone a repository, or enable write trust without a separate explicit operation.

## Project selectors and canonical references

The active project is the default target. At command and MCP boundaries, an optional project selector may be:

- `current`
- an exact stable project ID
- `parent`
- a unique local alias such as `royale`

Selectors are ergonomic input only. An unknown or ambiguous selector fails with a bounded diagnostic and candidate identities; PM must not guess. Successful responses always include the resolved stable project ID and display name.

Persisted cross-project references use a canonical `pm` URI whose fixed authority is `project`:

```text
pm://project/<project-id>/task/<task-id>
pm://project/<project-id>/wiki/<wiki-path>
```

For example:

```text
pm://project/prj_royale/task/ROY-0042
pm://project/prj_games/wiki/architecture/rendering
```

Project IDs, task IDs, and wiki path segments use their canonical stored spelling and are percent-encoded as URI path segments when necessary. Wiki paths omit `.md`, have no leading slash, and use `/` between hierarchy segments. Dot segments and empty segments are invalid. Parsing and formatting a valid canonical reference must round-trip without access to the target project.

A plain task ID in `dependsOn`, such as `ROY-0041`, remains a local-project reference. A cross-project dependency uses the canonical task URI. Same-project canonical URIs may be accepted, but writers should preserve the compact plain-ID form for local dependencies.

Aliases, relationship selectors, filesystem paths, repository URLs, project display names, and web URLs must never be persisted as resource identity. Renderers may show current aliases and names beside the canonical target.

## Resolution states and standalone guarantees

Resolution reports one of these states for every requested or traversed project:

- `available`
- `unregistered`
- `missing`
- `uninitialized-submodule`
- `identity-mismatch`
- `invalid`
- `untrusted-for-write`

The last state means reads may be available while linked writes are denied. Diagnostics must name the declaration owner, expected stable ID, alias when present, state, and a safe repair action when one exists. Paths in public or remote output must be omitted or sanitized.

Topology validation detects:

- more than one parent declaration
- duplicate child IDs or aliases
- cycles back to an already visited stable ID
- a child that declares a different parent
- non-reciprocal parent and child entries
- the same stable ID resolving to different roots
- traversal beyond the depth or 32-project limit

Invalid local syntax is an error for the linked manifest, but it does not invalidate unrelated project content. Missing, unregistered, uninitialized, mismatched, non-reciprocal, cyclic, and limit-exceeded links produce bounded warnings and partial family results. Most importantly, they never prevent the active project from opening, validating its local files, serving local reads, or accepting ordinary local mutations.

## Read behavior

Read services support three scopes:

- `current`: the active project only; this remains the existing fast path
- `project`: one explicitly selected, available family member
- `family`: all available members in the bounded family view

Family traversal uses a deterministic order. The active project is first. If it is a child, its parent follows, then available siblings in the parent's declared child order. If the parent is active, its available children follow in declared order. Duplicate stable IDs are returned once. Warnings occupy the position of unavailable members rather than silently erasing them.

Task and wiki reads use each available checkout's current working tree, including uncommitted changes. They are live, cancellable queries with bounded result sizes; the first version maintains no shared persistent index. Federated records include at least:

- owning project ID, display name, and current local alias when available
- resource-local ID or wiki path
- source working-tree Git revision and dirty state when Git metadata is available
- resolution warnings associated with the query

List, get, and search operations return partial results plus warnings when optional family members are unavailable. An explicitly selected unavailable project returns no resource result and a targeted resolution failure, while the active project remains usable.

The Angular shell and API expose the same scopes and ownership fields as CLI and MCP. Changing the selected project changes the read context; it does not merge configurations or silently change a mutation target.

### Dependencies and next-task ranking

A local dependency is resolved only in its owning project. A canonical cross-project task dependency is resolved against its stable project ID.

A dependency is ready only when the referenced task is available and in that project's configured done state. An unavailable project, missing task, invalid reference, or unreadable state is unresolved: the dependent task is blocked and carries a warning. It is never treated as satisfied and never makes the active project fail to open.

Family next-task ranking applies the existing ranking rules within each owning project, then prefers candidates owned by the active project before candidates with otherwise equivalent rank from linked projects. The final deterministic tie-break includes owning project ID and task ID. Status, milestone order, explicit task order, and inherited priority are interpreted only using the candidate's owning project configuration.

## Write boundary and authority

An ordinary mutation with no project selector targets the active project and keeps current behavior. A mutation of a different project must satisfy all of the following before any file changes:

1. The caller supplies an explicit project selector.
2. The selector resolves to exactly one stable project ID and verified checkout.
3. The user-local registry marks that project ID as trusted for writes.
4. The adapter and execution profile permit that mutation.
5. The complete operation has been validated for that one target.

Filesystem proximity, a Git submodule relationship, read availability, matching repository URL, parentage, membership, and trust of another family member do not grant write authority.

One operation may mutate files in at most one repository. It returns the owning project ID and changed paths. If authorization or validation fails, it changes nothing. Workflows needing changes in two repositories must issue separate, explicit operations and accept separate commits and recovery; coordinated atomic multi-repository mutation is future work.

Project write trust can be granted, inspected, and revoked explicitly in local settings. It is keyed by stable project ID, is not synchronized, and should be rechecked against the verified checkout at each mutation. Restricted remote-run MCP profiles keep cross-project mutation disabled even if a desktop user's registry trusts the project.

## Static publishing

Each project publishes its own static site and sanitized snapshot independently. A static export reads only the project being exported and must succeed without any linked checkout.

A linked declaration's `publicSiteUrl` is an outbound navigation hint owned by the declaring project. The target project remains authoritative for its actual publication configuration and generated site. When the target is available, PM may warn if its self-declared publication URL disagrees with the outbound hint, but an export must not rewrite either project automatically.

Family navigation and canonical task or wiki references translate through the declaring project's `publicSiteUrl` hint for the target. If no public URL is declared, the renderer presents the stable project identity and a clear unavailable-link state rather than embedding a local filesystem path. Static output must not expose registry bindings, write trust, dirty working-tree paths, or private repository information.

Combining multiple projects into one static artifact, synchronizing their revisions, and defining a single family URL space are future work.

## Remote agent context

A run may opt into immutable task or wiki context from selected linked projects. Run preflight resolves each selected stable project ID and captures an exact published commit. The immutable run specification records:

- the linked project ID
- the exact commit
- which task and wiki resources or read scope are provided
- whether that context is required or optional

The runner must separately allowlist each linked repository. It materializes linked repositories in isolated read-only locations without write credentials and exposes only linked read operations through the run-worker MCP profile. Required context that cannot be resolved at its captured commit fails preflight; unavailable optional context produces a warning. Reconnect and final reports retain the captured identities and revisions.

The primary run repository remains the only writable project. Local desktop write trust is never copied into a run specification and cannot grant the runner sibling authority.

## Games example

The Games repository is the parent project:

```text
games/                         prj_games
├── .pm/
├── royale/                    prj_royale (Git submodule)
│   └── .pm/
└── starfall/                  prj_starfall (Git submodule)
    └── .pm/
```

Games declares `royale` and `starfall` in that order with relative path hints and public site URLs. Each child declares Games as its optional parent with the `games` alias and `..` path hint. Royale and Starfall retain their own tracks, milestones, statuses, tasks, task order, wiki, IDs, repositories, membership, and release history.

### Task and wiki lookup

From Royale, an unqualified `get task ROY-0042` reads Royale. Selecting `--project games` resolves the alias and reads Games; selecting `--project prj_starfall` reads Starfall. A family search returns available Royale results first, then Games, then Starfall, and labels every record with its owner.

Royale can link to shared rendering guidance with:

```text
pm://project/prj_games/wiki/architecture/rendering
```

It can link to a Starfall task with:

```text
pm://project/prj_starfall/task/STAR-0017
```

Changing the `starfall` alias or moving its checkout does not change either reference.

### Cross-project dependency

Royale task `ROY-0042` may contain:

```yaml
dependsOn:
  - ROY-0039
  - pm://project/prj_games/task/ENGINE-0012
```

The first dependency uses Royale's state. The second uses Games' state. If Games is missing, its ID mismatches, or `ENGINE-0012` does not exist, `ROY-0042` is blocked with a named warning. Royale still opens and all unrelated Royale commands continue to work.

### Local path repair

In a complete Games checkout, the verified `royale` and `starfall` path hints resolve without machine-local setup. If Starfall is an uninitialized submodule, link inspection reports that state and suggests:

```text
git submodule update --init -- starfall
```

It does not run the command. In an independent Royale clone elsewhere, opening Games once can register its verified root. An explicit bind operation can repair a stale Games binding. No absolute path is committed, and binding Games does not trust it for writes.

### Writes

From Royale, an unqualified task edit changes Royale only. Editing Games requires an explicit `games` or `prj_games` selector plus local write trust for `prj_games`. Updating a Games task and a Royale task requires two operations; PM does not promise a cross-repository transaction.

### Static sites

Royale's static site links the shared rendering URI to Games' separately published `publicSiteUrl` and the Starfall task URI to Starfall's site. If Starfall has no public URL, Royale's export still succeeds and renders an unavailable external target. A bundled Games/Royale/Starfall site is future work.

### Remote-agent context

A remote run rooted in Royale may capture Games rendering wiki pages and Starfall task context at exact commits. Those checkouts are read-only, their revisions appear in the run specification and report, and the runner receives no credentials or mutation tools for either linked repository. Royale remains the single writable run repository.

## Migration and compatibility

Existing projects with no `.pm/linked_projects.yaml` remain standalone and require no file rewrite, stable-ID allocation, registry entry, or new trust decision. Existing plain dependency IDs remain local. Existing CLI, MCP, API, web, and static behavior remains the `current` scope.

Opt-in is incremental:

1. Ensure each participating project has a stable `.pm/project_id.txt`.
2. Add a manifest to the parent with ordered child declarations.
3. Optionally add reciprocal parent entries to children.
4. Open or explicitly bind available local checkouts.
5. Grant write trust only for projects that should accept explicit linked mutations.
6. Add public site URLs or remote-run context only when those workflows are needed.

Removing a manifest returns the project to standalone behavior. It does not delete tasks, wiki pages, registry history, repositories, or remote data. Stale local bindings may be inspected and removed separately.
