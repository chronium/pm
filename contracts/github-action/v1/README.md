# Project Model GitHub Action v1 contract

This document defines the proposed public contract for the root
`chronium/pm@<ref>` GitHub Action. It is normative for the v1 implementation,
but the Action is not available until a release ref containing the root
`action.yml` has been promoted.

The Action is a read-only CI interface to a released Project Model runtime. It
validates PM projects, builds static sites, and reports the packaged PM version.
It does not expose arbitrary PM commands, mutate project state, upload
artifacts, or deploy sites.

## Runtime boundary

V1 is a Docker container Action for GitHub-hosted Linux runners. GitHub mounts
the checked-out workspace at `/github/workspace`; every project and output path
must remain within that mount.

Released Action metadata selects an immutable OCI image digest. The metadata
passes the four declared inputs to the container as fixed positional arguments.
The entrypoint treats them as data and invokes PM directly. It must not join the
arguments into a command string, invoke `sh -c`, evaluate substitutions, or
accept additional arguments.

The repository keeps this interface in the root `action.template.yml`. PM-0121
materializes `action.yml` only after replacing the template token with the
promoted image digest; an unresolvable placeholder is never published as an
Action entrypoint. The resulting root metadata is equivalent to:

```yaml
name: Project Model
description: Validate a PM project or build its read-only static site.
author: Chronium

inputs:
  command:
    description: One of doctor, site-build, or version.
    required: true
  working-directory:
    description: Workspace-relative directory in which PM runs.
    required: false
    default: .
  output-directory:
    description: Workspace-relative destination used by site-build.
    required: false
    default: dist/pm-site
  force:
    description: Replace a non-empty site output directory when true.
    required: false
    default: "false"

outputs:
  pm-version:
    description: Version reported by the packaged PM runtime.
  site-path:
    description: Workspace-relative generated site path, or empty for other commands.

runs:
  using: docker
  image: docker://ghcr.io/chronium/pm@sha256:<promoted-digest>
  args:
    - ${{ inputs.command }}
    - ${{ inputs.working-directory }}
    - ${{ inputs.output-directory }}
    - ${{ inputs.force }}
```

Docker Action inputs are strings. Names and values in this contract are
case-sensitive unless a rule below explicitly says otherwise.

## Inputs

### `command`

`command` is required and accepts exactly these lowercase values:

| Value | PM invocation | Project required | Side effects |
| --- | --- | --- | --- |
| `doctor` | `pm doctor` | Yes | Reads and validates project state |
| `site-build` | `pm site build --output <path> [--force]` | Yes | Writes only to the validated output directory |
| `version` | `pm --version` | No | None |

Missing and unknown values fail before PM is invoked. V1 does not expose
`doctor --fix`, unrestricted CLI arguments, mutation commands, or repository
write-back.

### `working-directory`

`working-directory` defaults to `.` and is resolved relative to the GitHub
workspace root, not relative to the container image. It must be a non-empty
relative path whose canonical target exists within the workspace. Absolute
paths, traversal outside the workspace, and symlink escapes are rejected.

For `doctor` and `site-build`, PM performs its normal upward project discovery
from this directory. `version` requires the directory to exist but does not
require a `.pm` project.

### `output-directory`

`output-directory` defaults to `dist/pm-site` and is resolved relative to the
workspace root, independently of `working-directory`. This makes multi-project
workflows unambiguous:

```yaml
with:
  command: site-build
  working-directory: starfall
  output-directory: dist/starfall
```

The canonical destination must remain within the workspace. The Action rejects
the workspace root, the selected working directory, the discovered project
repository root, any `.pm` subtree, traversal outside the workspace, and
symlink escapes as output destinations.

Static exports already use relative assets and hash routing, so v1 has no base
path input.

### `force`

`force` accepts exactly `true` or `false` and defaults to `false`. It is valid
only with `site-build`. `true` maps to PM's existing `--force` option; `false`
preserves PM's refusal to replace a non-empty output directory. A true value on
another command fails as a contradictory request.

`output-directory` has no effect for `doctor` or `version` because Action
metadata cannot make an input conditional.

## Outputs and diagnostics

The entrypoint writes outputs through GitHub's `GITHUB_OUTPUT` file:

- `pm-version` is the trimmed output of `pm --version` for every successful
  command.
- `site-path` is the normalized, workspace-relative output directory after a
  successful `site-build`. It is empty for `doctor` and `version`.

PM standard output and standard error stream to the job log. The Action
preserves PM's process exit code. In particular, `doctor` succeeds when PM
reports only warnings and fails when project validation is invalid.
`site-build` retains PM's validation-before-write behavior.

The Action adds a concise command result to `GITHUB_STEP_SUMMARY`. V1 does not
parse PM output into GitHub annotations or expose issue counts as outputs.

## Consumer examples

Validate a project:

```yaml
- name: Check out repository
  uses: actions/checkout@v7

- name: Validate PM project
  uses: chronium/pm@v1
  with:
    command: doctor
```

Build a site and pass its path to the consumer-owned upload step:

```yaml
- name: Check out repository
  uses: actions/checkout@v7

- name: Build PM site
  id: pm-site
  uses: chronium/pm@v1
  with:
    command: site-build
    output-directory: dist/pm-site

- name: Upload Pages artifact
  uses: actions/upload-pages-artifact@v5
  with:
    path: ${{ steps.pm-site.outputs.site-path }}
```

The consumer owns checkout, artifact retention, GitHub Pages permissions,
deployment, and any hosting-provider integration.

## Version and promotion policy

Supported Action references have distinct stability guarantees:

- `chronium/pm@latest` is a moving tag for coordinated repositories such as PM
  and the ChronoFall family. It identifies the newest successfully promoted
  release, never an unvalidated `main` commit.
- `chronium/pm@v1` is a moving compatible-major tag.
- `chronium/pm@v1.x.y` is an immutable release tag.
- A full commit SHA is the strongest consumer pin.

Promotion is intentionally two-stage so Action metadata can pin the OCI image
by digest without introducing an unsigned generated commit on `main`:

1. Build, test, and publish the OCI image from the intended source revision.
2. Materialize the root `action.yml` from `action.template.yml` with the
   published digest.
3. Create an authorized signed promotion commit on `main`.
4. Exercise that commit through the public Action interface against disposable
   `doctor`, `site-build`, and `version` consumers.
5. Create the immutable release ref and move the compatible-major and `latest`
   refs only after those checks pass.

Immutable release refs never move. Logs and `pm-version` must make the packaged
PM version visible; the release workflow must additionally record the Action
commit and OCI digest. Consumers may roll a moving ref back to a previously
promoted signed commit without changing an immutable release ref.
