---
title: Milestone Deliverables and Activation Triggers
createdAt: 2026-08-06T05:36:38.7041500Z
modifiedAt: 2026-08-07T08:52:57.5942640Z
---

Milestones are first-class deliverables rather than organizational buckets. A milestone describes an outcome, the scope that belongs to that outcome, the activation gates that permit work to begin, and the explicit delivery decision that accepts the result.

Tracks remain the organizational dimension for kinds or streams of work. Task status remains the workflow dimension. Task dependencies remain readiness hints. Milestone activation is a separate eligibility layer.

## Decision summary

The activation model has a deliberately simple Boolean structure:

- Requirements within one activation trigger use AND.
- Activation triggers required by one milestone use AND.
- A manual override applies to one precise trigger.
- Activation is a persisted, latched state transition, not a continuously derived Boolean.
- Milestone delivery is explicit. Completing all assigned tasks only makes a milestone ready to deliver.

This design allows a downstream deliverable to begin when a selected subset of capabilities exists. It does not require the entire source milestone to be delivered, a synthetic entry task, or an unstable percentage rule.

## Domain model

### Milestone

A milestone is a deliverable with:

- A stable key.
- A display title.
- A Markdown description explaining outcome, scope, exclusions, and expected evidence.
- An optional inherited task priority.
- Zero or more required activation trigger keys.
- An optional delivery record.

A milestone with no required activation triggers is active by default. This preserves existing project behavior.

Descriptions should explain what the milestone delivers. They should not duplicate its task list. Tasks describe the work expected to produce the deliverable.

### Activation trigger

An activation trigger is a reusable, latched gate with:

- A stable key.
- A display title.
- Zero or more task or milestone requirements.
- An optional activation record.

Requirements are facts that may cause an activation transition. They are not continuing invariants after activation.

A trigger with no requirements is manual-only. A trigger with requirements activates automatically when every requirement is satisfied, but it can also be activated early through a manual override.

### Requirement

A requirement has one kind and one stable source:

- A task requirement is satisfied when the referenced task is currently in the done state.
- A milestone requirement is satisfied when the referenced milestone has a delivery record.

Requirements within a trigger are combined using AND. Duplicate requirements are invalid.

The first version supports local project references only. Linked-project requirements, OR groups, thresholds, percentages, and count-based rules are outside the initial scope.

## Consolidated storage shape

The target configuration consolidates milestone title, description, priority, activation policy, and delivery state into structured definitions:

~~~yaml
milestones:
  public-beta:
    title: Public beta
    description: |
      Deliver an installable beta covering the complete local workflow.

      Include upgrade and recovery guidance. Hosted collaboration is outside
      this deliverable.
    priority: high
    requiredActivationTriggers:
      - beta-entry
    delivery: null

activationTriggers:
  beta-entry:
    title: Beta entry criteria
    requirements:
      - kind: task
        source: FOUNDATION-0001
      - kind: task
        source: FOUNDATION-0002
      - kind: task
        source: FOUNDATION-0003
      - kind: milestone
        source: architecture-approved
    activation: null

  launch-authorized:
    title: Launch authorized
    requirements: []
    activation: null
~~~

The top-level activationTriggers name identifies trigger definitions. The milestone-side requiredActivationTriggers name makes the direction of the relationship explicit: the milestone consumes those gates rather than emitting them.

## Trigger evaluation and transitions

Requirement evaluation is non-vacuous:

~~~text
requirementsSatisfied =
  requirements is non-empty
  AND every requirement is currently satisfied

triggerOn =
  an activation record exists
~~~

The transition rule is:

~~~text
nextTriggerOn =
  currentTriggerOn
  OR requirementsSatisfied
  OR manualActivationRequested
~~~

More precisely:

- When a trigger is off and requirementsSatisfied becomes true, PM creates an automatic activation record.
- When a manual-only trigger is off and an activation is requested, PM creates a manual activation record.
- When a trigger with unmet requirements is off and activation is requested, PM creates an override activation record.
- Reopening a source task changes current requirement satisfaction but does not remove an activation record.
- Resetting an eligible trigger removes its activation record.
- Reconciliation may create missing automatic activation records, but never removes activation records.

The activation record is authoritative. Satisfied requirements without an activation record are a recoverable inconsistency, not an implicitly active trigger.

## Activation provenance

Activation is one optional structured object rather than several nullable fields. This prevents invalid combinations and records activation mode without persisting actor identity.

An automatic activation is:

~~~yaml
activation:
  at: 2026-08-06T08:15:00Z
  mode: automatic
~~~

A manual-only activation is:

~~~yaml
activation:
  at: 2026-08-06T08:15:00Z
  mode: manual
~~~

An override captures the requirements waived at the time of activation:

~~~yaml
activation:
  at: 2026-08-06T08:15:00Z
  mode: override
  reason: Architecture approval will be completed during beta hardening.
  waivedRequirements:
    - kind: milestone
      source: architecture-approved
~~~

waivedRequirements is a snapshot of requirements that were unsatisfied when the override occurred. It allows PM to report both historical and current truth:

- Active by override: 1 of 4 requirements were satisfied at activation.
- Currently 3 of 4 requirements are satisfied.

The activation object records current activation provenance, not a complete append-only audit trail. Reset removes it from current project state. Git history provides the longer audit trail once changes are committed.

PM does not persist an activation actor in v1. Git commit history provides authorship without copying OS usernames, machine names, certificate material, private identity identifiers, or credentials into public project metadata.

## Manual activation and overrides

Every trigger supports an explicit activation operation, but the operation is contextual:

- An inactive trigger with no requirements offers Activate.
- An inactive trigger with unmet requirements offers Override.
- An inactive trigger whose requirements are already satisfied requires reconciliation rather than a manual activation.
- An active trigger offers no activation action.

An override requires a non-empty reason and a waived requirement snapshot that exactly matches the unsatisfied requirements at activation.

Manual means an explicit control-plane mutation. It does not imply that a human performed it. A trusted CLI, web client, API caller, or MCP client may request it when authorized.

## Reset semantics

Reset is allowed only when:

- The trigger is active.
- Its requirements are not currently satisfied.

Because an empty requirement list is explicitly not satisfied, manual-only triggers may be reset.

An automatic or overridden trigger whose requirements are currently all satisfied cannot be reset. This avoids a pointless reset and immediate automatic reactivation, and it preserves truthful provenance when an override is later justified by completed requirements.

A persistent forced-off state is not part of this design.

## Redefining an active trigger

Presentation metadata such as title may be edited normally. Requirements of an active trigger may not be changed through an ordinary update.

Requirement changes use an explicit redefine operation. A redefine must:

1. Preview affected milestones and currently eligible tasks.
2. Replace the requirement definition.
3. Clear the previous activation record.
4. Evaluate the new requirements.
5. Create a new automatic activation record when the new requirements are already satisfied.
6. Otherwise leave the trigger pending.
7. Require confirmation when currently eligible milestones would become inactive.

This makes a definition change a deliberate re-evaluation instead of an accidental violation of latching semantics.

Changing which milestones consume a trigger is a separate operation. Attaching an already active reusable trigger to another milestone is legal and immediately satisfies that gate for the new consumer. The mutation should still preview its impact.

## Milestone lifecycle

Milestone lifecycle uses this precedence:

~~~text
if a delivery record exists:
    Delivered
else if any required activation trigger is off:
    Inactive
else if the milestone has at least one assigned task
        and every assigned task is done:
    Ready to deliver
else:
    Active
~~~

Consequences:

- Delivery dominates current trigger state.
- Resetting a trigger does not undeliver an accepted milestone.
- Reopening a delivered milestone removes its delivery record, re-evaluates its triggers, and may make it inactive.
- An empty milestone is active rather than vacuously ready to deliver.
- A delivered milestone does not make remaining assigned tasks recommendation-eligible.

### Delivery provenance

Ordinary delivery records the delivery time.

Exceptional delivery with unfinished tasks requires an explicit reason and a snapshot of the unfinished tasks accepted at delivery:

~~~yaml
delivery:
  at: 2026-08-06T10:30:00Z
  mode: exceptional
  reason: Remaining documentation work will ship as post-beta hardening.
  acceptedTaskIds:
    - FOUNDATION-0006
~~~

Delivery with unfinished tasks must require explicit confirmation. The stored snapshot distinguishes an intentional exception from later project corruption or edits.

PM does not persist a delivery actor in v1. A delivery record contains only the delivery time, mode, exceptional reason, and accepted task snapshot. Git commit history provides authorship without copying local identity or machine metadata into the public project configuration.

## Partial-milestone activation

Suppose foundation contains six tasks:

~~~text
FOUNDATION-0001  Core storage
FOUNDATION-0002  Domain model
FOUNDATION-0003  Import path
FOUNDATION-0004  Export path
FOUNDATION-0005  Recovery tools
FOUNDATION-0006  Documentation
~~~

A beta-entry trigger may require only FOUNDATION-0001 through FOUNDATION-0003. When those three selected capabilities are done:

- PM creates an automatic activation record for beta-entry.
- public-beta becomes active.
- FOUNDATION-0004 through FOUNDATION-0006 remain actionable.
- Tasks from both milestones can participate in next-task ranking.

The trigger references explicit task IDs. It does not mean any three tasks, half of the current task count, or fifty percent of a changing milestone. Threshold semantics may be introduced later as a distinct requirement kind.

## Activation graph and cycle validation

Activation cycles are hard errors because they create work that cannot become eligible through the normal workflow.

Build one directed graph containing milestone and trigger nodes:

~~~text
milestone M -> trigger G
    when M requires G

trigger G -> milestone M
    when G requires milestone M

trigger G -> milestone M
    when G requires task T and T is assigned to M
~~~

A task requirement whose task is unassigned creates no activation edge because unassigned tasks remain activation-eligible.

Any directed cycle is invalid, including:

~~~text
public-beta
  -> beta-entry
  -> public-beta
~~~

and indirect cycles:

~~~text
public-beta
  -> beta-entry
  -> foundation
  -> architecture-ready
  -> public-beta
~~~

Task dependency edges do not belong in this graph. They govern readiness rather than activation eligibility. Mixed activation and task-dependency deadlocks may be diagnosed as warnings later without weakening the hard activation-cycle invariant.

### Placement mutation preflight

Task placement contributes activation edges through task requirements. The following operations must validate the complete prospective graph before their first write:

- Assigning a task to a milestone.
- Moving a task between milestones.
- Bulk milestone assignment.
- Removing a task from a milestone.

Bulk operations must evaluate the full prospective batch rather than validating and writing tasks one at a time.

## Validation

PM rejects:

- Missing task, milestone, or trigger references.
- Duplicate requirements within one trigger.
- Duplicate trigger references within one milestone.
- Empty or invalid sources on typed requirements.
- Activation cycles.
- Removing a referenced trigger, task, or milestone.
- Invalid activation field combinations.
- Manual mode on a trigger with requirements.
- Override mode without a non-empty reason.
- Override mode without an exact waived requirement snapshot.
- Automatic mode on a manual-only trigger.
- Ordinary requirement edits while a trigger is active.
- Reset while automatic requirements are currently satisfied.
- Invalid delivery field combinations.
- Ordinary delivery with unfinished tasks.

PM reports non-blocking warnings or informational findings for:

- A trigger consumed by no milestones.
- An active automatic trigger whose requirements are no longer all satisfied.
- An overridden trigger whose requirements subsequently became satisfied.
- An inactive trigger whose requirements are satisfied but whose automatic activation record is missing.
- A milestone with no assigned tasks.

## Reconciliation and failure recovery

Normal lifecycle mutations evaluate affected triggers:

1. A task moves to done.
2. PM evaluates every inactive trigger referencing that task.
3. Newly satisfied triggers receive automatic activation records.
4. Milestone eligibility changes in the same authoritative mutation workflow.

Milestone delivery similarly evaluates triggers that require the delivered milestone.

Repository state may also change through direct edits, merges, reverts, conflict resolution, older PM versions, or partial filesystem failures. PM therefore provides:

~~~text
pm trigger reconcile
pm trigger reconcile --dry-run
~~~

Reconciliation only creates missing automatic activation records for currently satisfied inactive triggers. It never deactivates or rewrites existing activation provenance.

pm doctor reports requirements-satisfied-but-not-latched as a recoverable inconsistency. The dry-run command previews the activation records that reconciliation would create.

Automatic activation requires a lifecycle mutation layer around task state changes and milestone delivery. Updating recommendation code alone is insufficient. State and activation persistence must use bounded rollback or equivalent failure handling so partial writes are reported and recoverable.

## Recommendation eligibility

Activation eligibility is resolved before task dependency readiness and ranking:

~~~text
eligible =
  task is not done
  AND (
    task has no milestone
    OR milestone is Active
    OR milestone is Ready to deliver
  )
~~~

Tasks assigned to Inactive or Delivered milestones are excluded from next-task recommendations.

After this eligibility filter, PM applies its existing dependency readiness, priority, state order, milestone order, explicit task order, modification time, and deterministic ID ranking.

Consequences:

- Include-blocked may return a dependency-blocked task, but never an activation-ineligible task.
- Filtering explicitly to an inactive milestone returns no recommendation and explains the unmet triggers.
- Boards, search, and direct task reads continue to show inactive work with its activation explanation.
- Unassigned tasks remain activation-eligible.
- Linked-family recommendation evaluates each candidate using the owning project's local activation model before federated ranking.

## Control-plane authorization

The following are control-plane operations:

- Activate a manual-only trigger.
- Override a trigger.
- Reset a trigger.
- Deliver a milestone.
- Reopen a milestone.
- Redefine trigger requirements.
- Reconcile automatic activations.

They may be exposed through the CLI, hosted web application, API, and trusted MCP capability profile. They must not be exposed to isolated run workers.

Task completion remains an authoritative PM mutation and may automatically create activation records as part of its lifecycle processing.

## Read model

Trigger activation state and current requirement state remain separate. A resolved trigger exposes enough information for consumers to present both:

~~~csharp
public sealed record ResolvedActivationTrigger(
    string Key,
    string Title,
    bool IsActive,
    ActivationMode? ActivationMode,
    DateTimeOffset? ActivatedAt,
    string? OverrideReason,
    int SatisfiedRequirementCount,
    int RequirementCount,
    bool RequirementsSatisfied,
    bool IsLatchedDespiteUnmetRequirements,
    IReadOnlyList<ResolvedActivationRequirement> Requirements,
    IReadOnlyList<string> ConsumingMilestones);
~~~

Typical presentations are:

| Activation | Current requirements | Presentation |
| --- | --- | --- |
| None | 0 / 0 | Manual activation required |
| None | 3 / 4 | Pending — 3 / 4 |
| Automatic | 4 / 4 | Active automatically |
| Automatic | 3 / 4 | Active automatically — latched |
| Manual | 0 / 0 | Active manually |
| Override | 3 / 4 | Active by override — 3 / 4 |
| Override | 4 / 4 | Active by override — requirements now satisfied |

The activation switchboard shows:

- Trigger title and activation provenance.
- Current satisfied and total requirement counts.
- Expandable requirement details with source links and states.
- Consuming milestones.
- Contextual Activate, Override, Reset, Redefine, and Reconcile actions.
- Impact previews before eligibility-changing mutations.

Milestone presentation shows its title, deliverable description, lifecycle state, required triggers, unmet gates, and delivery provenance. The description should be expandable from the board without turning each milestone into decorative card chrome.

## Compatibility and migration

Existing projects may store milestone titles and priorities in separate scalar maps. This is a demonstrated persisted-data compatibility need, so migration converts each entry into one structured definition:

- The existing key and title remain stable.
- Existing milestone priority is folded into `priority`.
- `description` starts empty.
- `requiredActivationTriggers` starts empty, keeping the milestone active.
- `delivery` starts null.

`pm doctor` reports the legacy schema without writing, while `pm doctor --fix` performs the explicit idempotent migration. Until migration succeeds, project-config mutations fail with `milestone_schema_migration_required`; an unrelated setting change cannot silently rewrite the schema.

The compatibility path is owned by `ProjectConfigService` and its milestone-schema migration. Its removal condition is an explicit incompatible project-format boundary at which pre-structured projects are no longer supported and users have been given a supported migration/export path. Until that condition is met, the reader serves real on-disk data. This exception does not justify obsolete constructors, duplicate application workflows, or internal adapter shims; those callers must move to the approved services and the old code must be removed.

## Deferred extensions

The first version intentionally excludes:

- OR requirement groups.
- Any N of M thresholds.
- Percentage-based milestone progress gates.
- Linked-project activation requirements.
- Persistent forced-off overrides.
- Append-only activation/reset event history.
- Mixing ordinary task dependencies into the hard activation graph.

The functional web switchboard and deliverable editor intentionally shipped before their dedicated visual refinement passes; PM-0095 and PM-0096 own those refinements. PM-0099 owns guarded milestone delivery and reopening controls in Angular. CLI, trusted MCP, and the revisioned API remain the supported delivery surfaces until that task is complete.

These extensions can be added explicitly without changing the core separation:

- Requirements are factual conditions used to produce activation events.
- Triggers are reusable, latched gates with explicit activation provenance.
- Milestones are deliverables requiring all referenced triggers.