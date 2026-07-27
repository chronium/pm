import type { RunSpecification } from './protocol/types.js';

export interface RuntimeHandle {
  readonly runtimeId: string;
}

export interface RuntimeDriver {
  create(specification: RunSpecification, signal: AbortSignal): Promise<RuntimeHandle>;
  destroy(handle: RuntimeHandle, reason: 'completed' | 'failed' | 'cancelled'): Promise<void>;
}

export interface AgentDriverEvent {
  readonly type: string;
  readonly summary: string;
  readonly data?: unknown;
}

export interface AgentDriver {
  execute(
    specification: RunSpecification,
    runtime: RuntimeHandle,
    signal: AbortSignal,
  ): AsyncIterable<AgentDriverEvent>;
}
