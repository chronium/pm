import { isTerminal } from './lifecycle.js';
import type { StoredRun, RunStore } from './persistence/run-store.js';
import type { AgentDriver, RuntimeDriver, RuntimeHandle } from './drivers.js';

export interface RunProcessor {
  execute(run: StoredRun, signal: AbortSignal): Promise<void>;
}

export class DriverRunProcessor implements RunProcessor {
  constructor(
    private readonly store: RunStore,
    private readonly runtimeDriver: RuntimeDriver,
    private readonly agentDriver: AgentDriver,
  ) {}

  async execute(run: StoredRun, signal: AbortSignal): Promise<void> {
    let runtime: RuntimeHandle | undefined;
    try {
      this.store.transition(run.runId, 'starting_runtime', 'Starting runtime');
      runtime = await this.runtimeDriver.create(run.specification, signal);
      this.store.transition(run.runId, 'starting_agent', 'Starting agent');
      this.store.transition(run.runId, 'running', 'Agent running');

      for await (const event of this.agentDriver.execute(run.specification, runtime, signal))
        this.store.appendEvent(run.runId, {
          type: event.type,
          state: 'running',
          summary: event.summary,
          data: event.data,
        });

      this.store.transition(run.runId, 'validating', 'Validating run');
      this.store.transition(run.runId, 'collecting_artifacts', 'Collecting artifacts');
      this.store.transition(run.runId, 'completed', 'Run completed');
    } finally {
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
    }
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
