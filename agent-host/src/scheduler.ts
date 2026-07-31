import { isTerminal } from './lifecycle.js';
import type { StoredRun, RunStore } from './persistence/run-store.js';
import type { AgentDriver, RuntimeDriver, RuntimeHandle } from './drivers.js';
import type { RuntimeUsage } from './drivers.js';
import type { GitWorkspaceService } from './execution/workspace.js';
import type { PreparedLinkedContext } from './execution/workspace.js';
import { ValidationRunner, type ValidationResult } from './execution/validation.js';
import type { ArtifactCollector } from './execution/artifacts.js';
import type { RunFailure, RunFailureStage } from './protocol/types.js';
import { classifyRunFailure, RunFailureError, runFailure } from './run-failure.js';

export interface RunLifecycleServices {
  workspace: GitWorkspaceService;
  validation: ValidationRunner;
  artifacts: ArtifactCollector;
}

export interface RunProcessor {
  execute(run: StoredRun, signal: AbortSignal): Promise<void>;
}

export class DriverRunProcessor implements RunProcessor {
  constructor(
    private readonly store: RunStore,
    private readonly runtimeDriver: RuntimeDriver,
    private readonly agentDriver: AgentDriver,
    private readonly lifecycle?: RunLifecycleServices,
  ) {}

  async execute(run: StoredRun, signal: AbortSignal): Promise<void> {
    if (this.lifecycle === undefined) return await this.executeLegacy(run, signal);
    const startedAt = new Date().toISOString();
    let runtime: RuntimeHandle | undefined;
    let mirror: string | undefined;
    let agentResponse: string | null = null;
    let executionFailure: RunFailure | null = null;
    let executionStatus: 'succeeded' | 'failed' | 'cancelled' = 'succeeded';
    let stage: RunFailureStage = 'workspace';
    let validation: ValidationResult = ValidationRunner.skipped(
      run.specification.runtime.profile.validation,
    );
    let cleaned = false;
    let agentUsage: RuntimeUsage | null = null;
    let validationUsage: RuntimeUsage | null = null;
    let linkedContexts: PreparedLinkedContext[] = [];
    try {
      const prepared = await this.lifecycle.workspace.prepare(run.specification, signal);
      mirror = prepared.mirror;
      linkedContexts = prepared.linkedContexts;
      for (const context of linkedContexts)
        this.store.appendEvent(run.runId, {
          type: `mcp.linked_context_${context.status}`,
          state: 'preparing_workspace',
          summary: `${context.alias}: ${context.summary}`,
          data: {
            projectId: context.projectId,
            alias: context.alias,
            revision: context.revision,
            requirement: context.requirement,
            status: context.status,
          },
        });
      stage = 'runtime';
      this.store.transition(run.runId, 'starting_runtime', 'Starting runtime');
      runtime = await this.runtimeDriver.create(run.specification, signal);
      stage = 'agent';
      this.store.transition(run.runId, 'starting_agent', 'Starting agent');
      this.store.transition(run.runId, 'running', 'Agent running');
      let agentProducedEvent = false;
      try {
        for await (const event of this.agentDriver.execute(
          { specificationHash: run.specificationHash, specification: run.specification },
          runtime,
          signal,
        )) {
          agentProducedEvent = true;
          if (event.agentThreadId !== undefined)
            this.store.recordAgentThreadId(run.runId, event.agentThreadId);
          if (event.type === 'agent.message')
            agentResponse = responseText(event.data) ?? agentResponse;
          this.store.appendEvent(run.runId, {
            type: event.type,
            state: 'running',
            summary: event.summary,
            data: event.data,
          });
        }
      } catch (error) {
        executionStatus =
          signal.aborted || this.store.isCancellationRequested(run.runId) ? 'cancelled' : 'failed';
        executionFailure = classifyRunFailure(
          error,
          'agent',
          executionStatus === 'cancelled',
          signal.reason,
        );
        if (!agentProducedEvent && executionFailure.code === 'agent_execution_failed')
          executionFailure = runFailure('agent_start_failed');
      } finally {
        if (runtime !== undefined) {
          try {
            agentUsage = await measureUsage(this.runtimeDriver, runtime);
            await this.runtimeDriver.destroy(
              runtime,
              executionStatus === 'succeeded' ? 'completed' : executionStatus,
            );
          } catch {
            executionStatus = executionStatus === 'cancelled' ? 'cancelled' : 'failed';
            executionFailure ??= runFailure('runtime_cleanup_failed');
          }
          runtime = undefined;
        }
      }
      await this.lifecycle.workspace.resetCodexHome(run.runId);
      stage = 'validation';
      this.store.transition(run.runId, 'validating', 'Validating run');
      if (executionStatus === 'succeeded') {
        try {
          runtime = await this.runtimeDriver.create(run.specification, signal);
          validation = await this.lifecycle.validation.execute(
            runtime,
            run.specification.runtime.profile.validation,
            signal,
          );
          for (const step of validation.steps)
            this.store.appendEvent(run.runId, {
              type: `validation.${step.status}`,
              state: 'validating',
              summary: `${step.displayName}: ${step.status}`,
              data: {
                stepId: step.stepId,
                exitCode: step.exitCode,
                timedOut: step.timedOut,
                durationMilliseconds: step.durationMilliseconds,
                outputTruncated: step.outputTruncated,
              },
            });
          if (validation.status === 'failed') {
            executionStatus = 'failed';
            executionFailure = validation.steps.some((step) => step.timedOut)
              ? runFailure('validation_timeout')
              : runFailure('validation_failed');
          }
        } catch (error) {
          executionStatus =
            signal.aborted || this.store.isCancellationRequested(run.runId)
              ? 'cancelled'
              : 'failed';
          executionFailure = classifyRunFailure(
            error,
            'validation',
            executionStatus === 'cancelled',
            signal.reason,
          );
        } finally {
          if (runtime !== undefined) {
            try {
              validationUsage = await measureUsage(this.runtimeDriver, runtime);
              await this.runtimeDriver.destroy(
                runtime,
                executionStatus === 'succeeded' ? 'completed' : executionStatus,
              );
            } catch {
              executionStatus = executionStatus === 'cancelled' ? 'cancelled' : 'failed';
              executionFailure ??= runFailure('runtime_cleanup_failed');
            }
            runtime = undefined;
          }
        }
      }
      stage = 'artifacts';
      this.store.transition(run.runId, 'collecting_artifacts', 'Collecting artifacts');
      const collectionController = new AbortController();
      const collectionTimeout = setTimeout(
        () => collectionController.abort('collection_timeout'),
        120_000,
      );
      try {
        try {
          await this.lifecycle.artifacts.collect(
            {
              run,
              mirror,
              validation,
              agentResponse,
              executionStatus,
              executionFailure,
              startedAt,
              resourceUsage: { agent: agentUsage, validation: validationUsage },
              linkedContexts,
            },
            collectionController.signal,
          );
        } catch (error) {
          throw new RunFailureError(
            classifyRunFailure(error, 'artifacts', false, collectionController.signal.reason),
          );
        }
      } finally {
        clearTimeout(collectionTimeout);
      }
      await this.lifecycle.workspace.cleanup(run.runId);
      cleaned = true;
      const cancelled =
        executionStatus === 'cancelled' || this.store.isCancellationRequested(run.runId);
      this.store.transition(
        run.runId,
        cancelled ? 'cancelled' : executionStatus === 'failed' ? 'failed' : 'completed',
        cancelled
          ? runFailure('run_cancelled').summary
          : (executionFailure?.summary ?? 'Run completed'),
        { validationStatus: validation.status, failure: executionFailure },
      );
    } catch (error) {
      throw new RunFailureError(
        classifyRunFailure(
          error,
          stage,
          signal.aborted || this.store.isCancellationRequested(run.runId),
          signal.reason,
        ),
      );
    } finally {
      try {
        if (runtime !== undefined) {
          const state = this.store.getRun(run.runId)?.state;
          const reason =
            state === 'completed'
              ? 'completed'
              : state === 'cancelled' || signal.reason === 'client_requested'
                ? 'cancelled'
                : 'failed';
          await this.runtimeDriver.destroy(runtime, reason);
        }
      } finally {
        if (!cleaned) {
          try {
            await this.lifecycle.workspace.cleanup(run.runId);
          } catch {
            // The scheduler records the primary bounded failure; private logs retain cleanup detail.
          }
        }
      }
    }
  }

  private async executeLegacy(run: StoredRun, signal: AbortSignal): Promise<void> {
    let runtime: RuntimeHandle | undefined;
    try {
      this.store.transition(run.runId, 'starting_runtime', 'Starting runtime');
      runtime = await this.runtimeDriver.create(run.specification, signal);
      this.store.transition(run.runId, 'starting_agent', 'Starting agent');
      this.store.transition(run.runId, 'running', 'Agent running');
      for await (const event of this.agentDriver.execute(
        { specificationHash: run.specificationHash, specification: run.specification },
        runtime,
        signal,
      )) {
        if (event.agentThreadId !== undefined)
          this.store.recordAgentThreadId(run.runId, event.agentThreadId);
        this.store.appendEvent(run.runId, {
          type: event.type,
          state: 'running',
          summary: event.summary,
          data: event.data,
        });
      }
      this.store.transition(run.runId, 'validating', 'Validating run');
      this.store.transition(run.runId, 'collecting_artifacts', 'Collecting artifacts');
      this.store.transition(run.runId, 'completed', 'Run completed');
    } finally {
      if (runtime !== undefined) await this.runtimeDriver.destroy(runtime, 'completed');
    }
  }
}

function responseText(data: unknown): string | null {
  if (data === null || typeof data !== 'object' || Array.isArray(data)) return null;
  const text = (data as Record<string, unknown>)['text'];
  return typeof text === 'string' ? text : null;
}

async function measureUsage(
  driver: RuntimeDriver,
  runtime: RuntimeHandle,
): Promise<RuntimeUsage | null> {
  try {
    return (await driver.measure?.(runtime)) ?? null;
  } catch {
    return null;
  }
}

export class RunScheduler {
  private readonly active = new Map<
    string,
    { controller: AbortController; promise: Promise<void> }
  >();
  private started = false;
  private stopping = false;
  private pumpScheduled = false;

  constructor(
    private readonly store: RunStore,
    private readonly processor: RunProcessor,
    private readonly maxConcurrency: number,
  ) {
    if (!Number.isSafeInteger(maxConcurrency) || maxConcurrency <= 0)
      throw new Error('Scheduler concurrency must be a positive integer.');
  }

  get activeCount(): number {
    return this.active.size;
  }

  start(): void {
    if (this.started) return;
    this.started = true;
    this.schedulePump();
  }

  notify(): void {
    if (this.started && !this.stopping) this.schedulePump();
  }

  async stop(): Promise<void> {
    this.stopping = true;
    for (const active of this.active.values()) active.controller.abort('runner_stopping');
    await Promise.allSettled([...this.active.values()].map((active) => active.promise));
  }

  cancel(runId: string): boolean {
    const active = this.active.get(runId);
    if (active === undefined) return false;
    active.controller.abort('client_requested');
    return true;
  }

  async waitForIdle(): Promise<void> {
    while (this.active.size > 0 || (this.started && this.store.queueDepth() > 0)) {
      const pending = [...this.active.values()].map((active) => active.promise);
      if (pending.length === 0) {
        await new Promise<void>((resolve) => setImmediate(resolve));
      } else {
        await Promise.race(pending);
      }
    }
  }

  private schedulePump(): void {
    if (this.pumpScheduled) return;
    this.pumpScheduled = true;
    setImmediate(() => {
      this.pumpScheduled = false;
      this.pump();
    });
  }

  private pump(): void {
    while (!this.stopping && this.active.size < this.maxConcurrency) {
      const run = this.store.claimNextRun();
      if (run === undefined) return;
      const controller = new AbortController();
      const promise = this.process(run, controller.signal).finally(() => {
        this.active.delete(run.runId);
        this.schedulePump();
      });
      this.active.set(run.runId, { controller, promise });
    }
  }

  private async process(run: StoredRun, signal: AbortSignal): Promise<void> {
    try {
      await this.processor.execute(run, signal);
      const current = this.store.getRun(run.runId);
      if (current !== undefined && !isTerminal(current.state)) {
        const cancelled = this.store.isCancellationRequested(run.runId);
        const failure = cancelled ? runFailure('run_cancelled') : runFailure('internal_failure');
        this.store.transition(run.runId, cancelled ? 'cancelled' : 'failed', failure.summary, {
          previousState: current.state,
          nextState: cancelled ? 'cancelled' : 'failed',
          failure,
        });
      }
    } catch (error) {
      const current = this.store.getRun(run.runId);
      if (current !== undefined && !isTerminal(current.state)) {
        const cancelled = this.store.isCancellationRequested(run.runId);
        const failure = classifyRunFailure(
          error,
          'system',
          cancelled || signal.aborted,
          signal.reason,
        );
        this.store.transition(run.runId, cancelled ? 'cancelled' : 'failed', failure.summary, {
          previousState: current.state,
          nextState: cancelled ? 'cancelled' : 'failed',
          failure,
        });
      }
    }
  }
}
