---
title: Release Versioning
createdAt: 2026-08-11T08:24:42.1013280Z
modifiedAt: 2026-08-11T08:54:21.3047860Z
---

# Release version identity

A PM repository may opt into release versioning by tracking `.pm/release_version.txt`. The file is the canonical release identity for builds produced from that repository.

The file contains exactly one canonical `major.minor.patch` value. A final LF or CRLF newline is allowed. Whitespace, leading zeroes, signs, prerelease or build suffixes, extra components, and extra lines are invalid. Each component must fit PM's four-part CLR assembly identity, where the generated revision is zero.

Projects without this file remain valid and are not version-managed. They do not receive release transition files when tasks or milestones change. A malformed or unreadable file is a `pm doctor` error.

# Transition policy

PM uses an altered SemVer policy:

- moving a task from a non-done state to `done` increments patch;
- delivering a milestone increments minor and resets patch to zero;
- reopening work never decrements a version;
- completing or delivering reopened work is a new transition;
- `pm release major --reason <reason>` advances only to the next `major.0.0`; and
- no Git commit, tag, or unrelated metadata edit advances a version.

The task and milestone lifecycle services apply this policy for CLI, web, API, and MCP callers. Release-affecting lifecycle results include the transition that occurred. Manual major changes are control-plane operations and require an explicit reason.

# Evidence and recovery

Every completed transition has immutable evidence at `.pm/release_transitions/<to-version>.yaml`. Evidence records the timestamp, kind, exact from/to versions, and task or milestone source. A manual major transition records its reason instead of a source. Attribution belongs to the signed Git history rather than a machine or OS identity in public PM metadata.

A lifecycle mutation first creates the exclusive `.pm/release_transition_pending.yaml` journal. While it exists, further task and milestone lifecycle mutations are rejected. If an interrupted mutation left the primary task completion or milestone delivery in place, reconciliation completes the release transition forward. If the primary mutation never applied, reconciliation clears the untouched intent. Manual major intent always completes forward.

Use:

- `pm release status` to inspect current, pending, and latest transition state;
- `pm release reconcile --dry-run` to preview recovery;
- `pm release reconcile` to repair the pending boundary; and
- `pm doctor` to validate the evidence chain and require reconciliation when a journal remains.

Reconciliation only repairs the recorded boundary and is idempotent. It never infers new releases by replaying Git history.

Trusted MCP clients have equivalent `get_release_status`, `reconcile_release_version`, `preview_major_version`, and `advance_major_version` operations, including linked-project selectors. Isolated run workers cannot invoke these control-plane operations.

# Published identity

PM's own build reads the tracked version and stamps the same identity into:

- the CLI `pm --version` result;
- package and informational version metadata;
- the first three CLR assembly and file version components, with revision `0`;
- the generated `pm-release.json` release manifest;
- the GitHub Action `pm-version` output; and
- the OCI `org.opencontainers.image.version` label.

Release publication rejects missing, malformed, or conflicting version inputs. Generated manifests and container labels are evidence derived from the tracked file; they are not additional version sources.

# PM cutover

PM adopts this contract at `1.0.1`. This is an explicit bootstrap from the former hard-coded `1.0.0`; historical task and milestone deliveries are not replayed to synthesize a version.

Automatic transitions begin with PM-0125. Therefore PM-0125 starts from `1.0.1` and its own completion produces the first evidence record at `1.0.2`. PM-0124 intentionally has no synthesized evidence.

CalVer, prerelease identifiers, and build metadata are outside this policy. A later task may introduce those only as an explicit revision of the release contract.