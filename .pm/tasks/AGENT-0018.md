---
id: AGENT-0018
title: Publish signed versioned agent runner releases
track: AGENT
priority: low
dependsOn:
- AGENT-0012
createdAt: 2026-07-29T10:30:08.6559220Z
modifiedAt: 2026-07-29T10:30:08.6677220Z
---

## Goal

Turn the locally verified AGENT-0012 bundle into a repeatable public distribution workflow without changing the v1 runner protocol.

## Proposed implementation

- Add a manually triggered release workflow that builds Linux x64 host artifacts on a pinned runner.
- Publish versioned host bundles and checksums to GitHub Releases.
- Publish the worker image to GHCR by immutable digest.
- Add cryptographic artifact and image signing, SBOMs, provenance, and verification instructions.
- Define release promotion, rollback, retention, and compromised-release response.
- Keep release publication separate from normal branch CI and static PM snapshot deployment.

## Acceptance criteria

- One manual action produces a versioned GitHub Release and matching immutable GHCR image.
- Consumers can verify checksums, signatures, provenance, source revision, and image digest before installation.
- The release process documents rollback and credential rotation.