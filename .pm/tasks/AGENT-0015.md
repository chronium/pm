---
id: AGENT-0015
title: Design requirement-based runner capability discovery
track: AGENT
milestone: agent-runner-evolution
dependsOn:
- AGENT-0012
createdAt: 2026-07-28T18:44:20.3773910Z
modifiedAt: 2026-07-29T12:08:46.9573350Z
---

## Goal

Evolve runner discovery from named-machine selection toward requirement-based scheduling without hard-coding runner IDs or current product and toolchain names into PM.

## Investigation and implementation

- Separate static capabilities from dynamic capacity and availability.
- Define additive capability reporting for logical CPU count, total and available memory, GPU inventory, installed runtime and toolchain capability IDs, supported agent providers, runtime profiles, concurrency, and protocol feature flags.
- Prefer stable capability identifiers and profile requirements over free-form SDK descriptions or runner-name checks.
- Define how a run or routing recommendation expresses hard requirements such as GPU access, a toolchain capability, or a named runtime profile.
- Define deterministic preflight matching and useful mismatch reasons.
- Preserve protocol compatibility by ignoring unknown additive capability fields while rejecting unsupported required features.
- Decide which dynamic values are snapshots, how fresh they must be, and whether they belong in normal capability discovery or a separate health response.
- Keep PM responsible for projects and scheduling; the runner reports execution capabilities only.

## Acceptance criteria

- PM can determine whether a runner satisfies a run without comparing display names.
- Static installation facts and dynamic available capacity are represented separately.
- GPU and toolchain reporting is useful without leaking host paths, credentials, or unrelated software inventory.
- Unknown optional features remain forward compatible and unknown required features fail closed.
- The design identifies the smallest protocol-compatible implementation slice and its tests.