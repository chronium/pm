import type { CapabilityService, RunCapabilityResult } from './capabilities.js';
import type {
  AcceptRunResult,
  CancellationResult,
  RunStore,
  StoredRun,
} from './persistence/run-store.js';
import type { RunRequest } from './protocol/types.js';
import { fixedTimeHashEquals } from './protocol/canonical-json.js';

export interface RunExecutionController {
  notify(): void;
  cancel(runId: string): boolean;
}

export class QueueOnlyExecutionController implements RunExecutionController {
  notify(): void {}
  cancel(_runId: string): boolean {
    return false;
  }
}

export type StartRunResult =
  | { disposition: 'invalid_capability'; validation: Exclude<RunCapabilityResult, { valid: true }> }
  | AcceptRunResult;

export interface ActiveRunSummary {
  runId: string;
  taskId: string;
  taskTitle: string;
  state: StoredRun['state'];
  lastEventSequence: number;
  acceptedAt: string;
  updatedAt: string;
  cancellationRequestedAt: string | null;
}

export class RunCoordinator {
  constructor(
    private readonly store: RunStore,
    private readonly capabilities: CapabilityService,
    private readonly queueCapacity: number,
    private readonly execution: RunExecutionController,
  ) {}

  start(request: RunRequest): StartRunResult {
    const existing = this.store.getRun(request.specification.runId);
    if (existing !== undefined)
      return fixedTimeHashEquals(existing.specificationHash, request.specificationHash)
        ? { disposition: 'existing', run: existing }
        : { disposition: 'conflict', code: 'run_id_conflict' };
    const validation = this.capabilities.validateRun(request);
    if (!validation.valid) return { disposition: 'invalid_capability', validation };
    const result = this.store.acceptRun(request, this.queueCapacity);
    if (result.disposition === 'new') this.execution.notify();
    return result;
  }

  cancel(runId: string): CancellationResult {
    const result = this.store.requestCancellation(runId);
    if (result.disposition === 'requested') this.execution.cancel(runId);
    return result;
  }

  static summary(run: StoredRun): ActiveRunSummary {
    return {
      runId: run.runId,
      taskId: run.specification.task.taskId,
      taskTitle: run.specification.task.title,
      state: run.state,
      lastEventSequence: run.lastEventSequence,
      acceptedAt: run.acceptedAt,
      updatedAt: run.updatedAt,
      cancellationRequestedAt: run.cancellationRequestedAt,
    };
  }
}
