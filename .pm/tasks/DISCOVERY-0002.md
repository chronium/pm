---
id: DISCOVERY-0002
title: Design policy-constrained agent task routing
track: DISCOVERY
createdAt: 2026-07-28T04:45:05.9230990Z
modifiedAt: 2026-07-28T04:45:05.9230990Z
---

## Goal

Design a policy-constrained task router that recommends the least expensive suitable agent configuration, identifies tasks that should not execute as written, and eventually schedules approved work against runner capacity.

This is a discovery and design task. It does not authorize automatic task mutation, automatic execution, or a particular model naming scheme.

## Architectural placement

The router belongs in the trusted PM control plane, where task state, dependencies, wiki context, repository context, project policy, and human authority are available.

The Linux runner continues to advertise concrete provider, model, effort, runtime-profile, validation, and capacity capabilities. It receives and enforces an immutable resolved run specification; it does not decide project priorities or silently rewrite routing policy.

The intended flow is:

    task and selected project context
      -> deterministic eligibility and minimum-policy rules
      -> optional model-assisted recommendation
      -> policy normalization
      -> human review or explicit automation gate
      -> immutable concrete run specification
      -> runner execution

## Recommended domain

A routing assessment should be bound to:

- the task ID and exact task revision;
- the routing-policy revision;
- the runner capability snapshot or compatible capability requirements;
- the repository revision or shallow repository context used;
- the context sources supplied to the router.

Routing outcomes should include:

- ready;
- split recommended;
- needs specification;
- needs human decision;
- blocked;
- not suitable for automation.

A ready recommendation should describe abstract capability and reasoning tiers, an installed runtime-profile class, an installed validation-policy selection, complexity, risk, confidence, relevant context, and a concise rationale. Expected files, subsystem count, duration, diff size, and resource use are advisory estimates only.

Concrete model names and machine-specific runtime IDs should be resolved at launch from current advertised capabilities. Durable task metadata should not become stale merely because a provider renames or replaces a model.

## Policy boundaries

- Deterministic rules establish eligibility, minimum capability, mandatory review, and prohibited automation before a routing model runs.
- Authentication, cryptography, persistence migrations, protocol compatibility, destructive operations, and other configured sensitive areas may require stronger minimum tiers and human review.
- Runner-owned profiles remain administrator-defined. A router may select an installed profile or validation policy but cannot invent container options, network access, mounts, secrets, or arbitrary shell commands.
- A split, specification update, dependency change, or task rewrite is a proposal that PM applies only after review.
- Routing never moves authoritative task state or marks work complete.
- Task, policy, repository, or relevant capability changes invalidate or visibly stale the assessment.
- Manual overrides are allowed within hard policy and are audited with the original recommendation.
- Low-confidence or malformed tasks do not launch automatically.
- Environment and infrastructure failures do not justify escalating model capability.
- Repository task and wiki content is untrusted input to the router. The routing adapter is read-only, schema constrained, and receives no project mutation tools.

## Configuration ownership

Evaluate a split in which public project routing policy lives in the repository-owned PM model while machine-specific mappings remain local:

- public project policy: task classes, sensitive paths or tracks, minimum tiers, review gates, permitted automation, and routing defaults;
- local control-plane or runner configuration: concrete provider/model mapping, available effort levels, runtime profiles, validation profiles, capacity, credentials, and cost preferences;
- durable assessment: revision-bound recommendation and rationale suitable for review and audit;
- runner journal: actual resolved specification, events, usage, resource evidence, validation, and outcome.

Do not settle exact file names or JSON shapes until the existing PM application, protocol, and persistence boundaries are reviewed.

## Proposed implementation slices

### 1. Routing domain and policy

Define capability tiers, reasoning tiers, risk and confidence scales, routing outcomes, assessment revision semantics, policy normalization, invalidation, override rules, and authority boundaries.

### 2. Deterministic eligibility and task-quality checks

Classify obvious tasks and reject ineligible work using task state, dependency readiness, acceptance criteria, scope signals, sensitive areas, project rules, and current runner capabilities. Obvious mechanical work should not require a routing-model call.

### 3. Model-assisted recommendation

Add a read-only router adapter that receives the complete task, concise dependency summaries, selected wiki excerpts, shallow repository structure, nearby history when useful, and deterministic constraints. Require schema-validated output and bounded context expansion.

### 4. Review and launch integration

Present recommendation, rationale, risk, confidence, context used, runtime requirements, validation policy, stale state, and human override controls. Approved recommendations resolve to the existing immutable run specification. Split and specification suggestions become reviewed proposals rather than direct mutations.

### 5. Failure classification and rerouting

Classify environment, validation, implementation, specification, context, task-scope, policy, cancellation, and stalled-agent failures. Define bounded retry and escalation rules based on cause. Repeated failure and unrelated changes require review rather than unlimited escalation.

### 6. Outcome history and routing evaluation

Record recommendation, chosen override, concrete execution profile, duration, usage, retries, validation evidence, diff scope, human follow-up, and final disposition. Begin with reports and representative evaluation sets; do not allow history to silently rewrite policy.

### 7. Dependency and capacity-aware execution waves

Plan approved tasks around dependency readiness, explicit human gates, runner concurrency, installed profiles, CPU, memory, and other capacity. Milestone-wide automatic execution remains a separately enabled operation and is not part of the first routing release.

## Evaluation strategy

Build a representative routing corpus from real PM work rather than generic examples:

- small documentation and formatting changes;
- localized Angular UI work;
- ordinary API and application-service features;
- cross-cutting protocol and persistence changes;
- authentication and security-sensitive changes;
- exploratory bugs with unclear causes;
- oversized or underspecified tasks;
- tasks blocked by dependencies or missing design decisions.

Evaluate correctness of eligibility, policy normalization, recommendation stability, stale-assessment detection, cost/capability choices, failure classification, and human override behavior. Do not assume maximum reasoning is always best; compare supported configurations against representative outcomes.

## Questions to resolve

- Which routing policy is public project state and which preferences remain user-local?
- Should durable assessments live in PM project files, a local cache, or both?
- What capability-tier vocabulary remains useful across provider and model changes?
- What minimal repository and wiki context produces reliable routing without deeply solving each task?
- Which task characteristics are deterministic policy inputs versus model-assessed signals?
- How are security-sensitive tracks, paths, labels, or task classes configured without brittle heuristics?
- When may high-confidence low-risk work auto-queue, if ever?
- How should assessment history be retained, published, redacted, and pruned?
- How should planning proposals, the future conversational planner, and routing assessments share context without coupling their implementations?
- Does execution-wave scheduling belong in the control plane, runner fleet coordinator, or a later dedicated scheduler service?

## Deliverables

- A routing architecture and authority-boundary document grounded in the existing PM and runner implementation.
- Candidate persistence and API models without prematurely adopting externally proposed JSON shapes.
- A deterministic policy matrix and representative evaluation corpus.
- CLI, MCP, Angular, and runner interaction flows.
- Threat analysis for prompt injection, policy bypass, stale recommendations, unsafe validation, secret exposure, and runaway retry cost.
- A sequenced agent-routing milestone created only after v1 runner experience validates the assumptions.

## Acceptance criteria

- The proposal selects sufficient capability rather than maximum capability by default.
- Hard policy cannot be weakened by a model recommendation or repository prompt content.
- Runtime and validation options remain constrained to installed trusted profiles.
- Split and specification recommendations do not mutate tasks without explicit application.
- Failed infrastructure is not treated as a reasoning-capability problem.
- Existing manually launched immutable runs remain supported when routing is disabled.
- The design uses measured v1 outcomes before enabling automatic routing or execution waves.