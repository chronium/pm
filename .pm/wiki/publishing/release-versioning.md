---
title: Release Versioning
createdAt: 2026-08-11T08:24:42.1013280Z
modifiedAt: 2026-08-11T08:24:42.1013280Z
---

# Release version identity

A PM repository may opt into release versioning by tracking `.pm/release_version.txt`. The file is the canonical release identity for builds produced from that repository.

The file contains exactly one canonical `major.minor.patch` value. A final LF or CRLF newline is allowed. Whitespace, leading zeroes, signs, prerelease or build suffixes, extra components, and extra lines are invalid. Each component must fit PM's four-part CLR assembly identity, where the generated revision is zero.

Projects without this file remain valid and are not version-managed. A malformed or unreadable file is a `pm doctor` error.

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

PM uses an altered SemVer policy:

- completing a release-managed task increments the patch version;
- delivering a release-managed milestone increments the minor version and resets patch to zero;
- major versions change only through an explicit release decision.

Automatic task and milestone transitions begin with PM-0125. Therefore PM-0125 starts from `1.0.1` and its own completion will produce `1.0.2`.

CalVer, prerelease identifiers, and build metadata are outside this policy. A later task may introduce those only as an explicit revision of the release contract.