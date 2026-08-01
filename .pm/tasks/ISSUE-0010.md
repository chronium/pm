---
id: ISSUE-0010
title: Make task storage resilient to missing directories
track: ISSUE
createdAt: 2026-08-01T04:54:21.7790200Z
modifiedAt: 2026-08-01T05:44:05.6198270Z
---

## Problem

Task creation and state changes can fail when required task-storage directories do not exist under `.pm/`. This commonly occurs in a newly cloned project with no tasks because Git does not retain empty directories such as `.pm/tasks/` or `.pm/states/<status>/`.

Task creation currently writes the task file before ensuring `.pm/tasks/` exists. State changes also delete the source ref before writing the destination ref. Missing directories therefore cause filesystem exceptions and can leave partially written tasks or tasks without a state association.

Linked-project MCP writes expose the same failure when creating the first task in an otherwise valid, trusted empty project.

## Proposed implementation

- Add tracked `.gitkeep` placeholders to empty task and configured status directories created during project initialization.
- Create and remove status-directory placeholders consistently when statuses are added or deleted.
- Ensure task, state, doctor, orphan detection, and validation logic ignores placeholder files.
- Make every task-file write create `.pm/tasks/` before writing.
- Make every task-state write create the configured destination status directory before writing the ref.
- Ensure a failed destination write cannot remove the existing state association.
- Apply the same invariant to task creation, bulk creation, structured updates, state moves, CLI, MCP, web, and linked-project mutation targets.
- Treat placeholders as layout preservation rather than a correctness dependency: older clones and manually repaired projects without them must still work.
- Return bounded application errors instead of allowing filesystem exceptions to escape through MCP or other adapters.

## Acceptance criteria

- A freshly initialized project's committed `.pm/` layout retains its task and configured status directories after cloning.
- Creating the first task succeeds when `.pm/tasks/`, every `.pm/states/<status>/` directory, and all placeholders are absent.
- Creating the first task through a trusted linked-project MCP target succeeds in the same clone-like layout.
- Moving the first task into a configured status succeeds when its destination state directory does not exist.
- Adding and deleting statuses maintains placeholder files without making an otherwise unused status appear in use.
- A failed task write or state move does not leave a partial task or remove the original state ref and task-order placement.
- New task creation, bulk creation, structured task updates, and state changes handle missing required directories consistently.
- Tests reproduce fresh initialization, clone-like projects containing no empty directories, and legacy projects without placeholders.
- MCP returns a structured bounded failure for any remaining storage error.
- `pm doctor` reports a valid project after successful creation and movement.