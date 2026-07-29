import { isTerminal } from './lifecycle.js';
import type { StoredRun, RunStore } from './persistence/run-store.js';
import type { AgentDriver, RuntimeDriver, RuntimeHandle } from './drivers.js';
import type { RuntimeUsage } from './drivers.js';
import type { GitWorkspaceService } from './execution/workspace.js';
import { ValidationRunner, type ValidationResult } from './execution/validation.js';
import type { ArtifactCollector } from './execution/artifacts.js';

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
    let executionError: string | null = null;
    let executionStatus: 'succeeded' | 'failed' | 'cancelled' = 'succeeded';
    let validation: ValidationResult = ValidationRunner.skipped(
      run.specification.runtime.profile.validation,
    );
    let cleaned = false;
    let agentUsage: RuntimeUsage | null = null;
    let validationUsage: RuntimeUsage | null = null;
    try {
      const prepared = await this.lifecycle.workspace.prepare(run.specification, signal);
      mirror = prepared.mirror;
      this.store.transition(run.runId, 'starting_runtime', 'Starting runtime');
      runtime = await this.runtimeDriver.create(run.specification, signal);
      this.store.transition(run.runId, 'starting_agent', 'Starting agent');
      this.store.transition(run.runId, 'running', 'Agent running');
      try {
        for await (const event of this.agentDriver.execute(
          { specificationHash: run.specificationHash, specification: run.specification },
          runtime,
          signal,
        )) {
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
        executionError = safeError(error);
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
            executionError = 'runtime_cleanup_failed';
          }
          runtime = undefined;
        }
      }
      await this.lifecycle.workspace.resetCodexHome(run.runId);
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
        } catch (error) {
          executionStatus =
            signal.aborted || this.store.isCancellationRequested(run.runId)
              ? 'cancelled'
              : 'failed';
          executionError = safeError(error);
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
              executionError = 'runtime_cleanup_failed';
            }
            runtime = undefined;
          }
        }
      }
      this.store.transition(run.runId, 'collecting_artifacts', 'Collecting artifacts');
      const collectionController = new AbortController();
      const collectionTimeout = setTimeout(
        () => collectionController.abort('collection_timeout'),
        120_000,
      );
      try {
        await this.lifecycle.artifacts.collect(
          {
            run,
            mirror,
            validation,
            agentResponse,
            executionStatus,
            executionError,
            startedAt,
            resourceUsage: { agent: agentUsage, validation: validationUsage },
          },
          collectionController.signal,
        );
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
        cancelled ? 'Run cancelled' : executionStatus === 'failed' ? 'Run failed' : 'Run completed',
        { validationStatus: validation.status, reason: executionError },
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
        if (!cleaned) await this.lifecycle.workspace.cleanup(run.runId);
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

function safeError(error: unknown): string {
  if (error instanceof Error && error.name === 'AbortError') return 'operation_cancelled';
  return 'run_phase_failed';
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
        this.store.transition(
          run.runId,
          cancelled ? 'cancelled' : 'failed',
          cancelled ? 'Run cancelled' : 'Run processor ended before completion',
          {
            previousState: current.state,
            nextState: cancelled ? 'cancelled' : 'failed',
            reason: cancelled ? 'client_requested' : 'run_processor_incomplete',
          },
        );
      }
    } catch {
      const current = this.store.getRun(run.runId);
      if (current !== undefined && !isTerminal(current.state)) {
        const cancelled = this.store.isCancellationRequested(run.runId);
        this.store.transition(
          run.runId,
          cancelled ? 'cancelled' : 'failed',
          cancelled ? 'Run cancelled' : 'Run processor failed',
          {
            previousState: current.state,
            nextState: cancelled ? 'cancelled' : 'failed',
            reason: cancelled ? 'client_requested' : 'run_processor_failed',
          },
        );
      }
    }
  }
}
