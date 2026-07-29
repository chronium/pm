---
id: AGENT-0019
title: Add authenticated runner artifact content transfer
track: AGENT
milestone: agent-runner-evolution
priority: urgent
dependsOn:
- AGENT-0012
createdAt: 2026-07-29T12:09:33.3646720Z
modifiedAt: 2026-07-29T12:09:47.1663970Z
---

## Goal

Transfer retained run artifact bytes from the runner to PM through the authenticated protocol without exposing host paths or weakening artifact retention boundaries.

## Proposed implementation

- Add an additive authenticated endpoint for the content of one artifact owned by one run.
- Resolve content exclusively from persisted artifact metadata; never accept a filesystem path from the client.
- Reject unknown, pruned, symlinked, oversized, length-mismatched, or digest-mismatched artifacts.
- Stream bounded bytes with the recorded media type, length, digest, and safe filename.
- Extend the .NET runner client and PM JSON API to proxy artifact content without loading unbounded files into memory.
- Preserve signed-request replay protection and the existing metadata endpoints.
- Keep credentials, repository paths, and runner storage locations out of responses and logs.

## Acceptance criteria

- PM can retrieve every retained artifact from a completed run through authenticated APIs.
- The received byte count and SHA-256 digest match persisted metadata.
- Path traversal, cross-run artifact access, symlinks, pruned files, corruption, and unauthenticated requests fail closed.
- Existing protocol clients that use metadata only remain compatible.
- Runner, .NET contract, API, and integration tests cover the transfer.