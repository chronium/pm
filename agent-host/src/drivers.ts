import type { RunRequest, RunSpecification } from './protocol/types.js';

export interface RuntimeCommand {
  readonly executable: string;
  readonly arguments: readonly string[];
}

export interface AgentRuntimeContext {
  readonly workspaceDirectory: string;
  readonly codexHomeDirectory: string;
  readonly networkAccessEnabled: boolean;
  readonly workerCommand: RuntimeCommand;
  readonly pmMcpCommand: RuntimeCommand;
  readonly environment: Readonly<Record<string, string>>;
}

export interface RuntimeHandle {
  readonly runtimeId: string;
  readonly agentContext: AgentRuntimeContext;
}

export interface RuntimeDriver {
  create(specification: RunSpecification, signal: AbortSignal): Promise<RuntimeHandle>;
  destroy(handle: RuntimeHandle, reason: 'completed' | 'failed' | 'cancelled'): Promise<void>;
}

export interface AgentDriverEvent {
  readonly type: string;
  readonly summary: string;
  readonly data?: unknown;
  readonly agentThreadId?: string;
}

export interface AgentDriver {
  execute(
    request: RunRequest,
    runtime: RuntimeHandle,
    signal: AbortSignal,
  ): AsyncIterable<AgentDriverEvent>;
}

export interface RuntimeProcessRequest {
  readonly command: RuntimeCommand;
  readonly workingDirectory: string;
  readonly environment: Readonly<Record<string, string>>;
  readonly standardInput: string;
}

export type RuntimeProcessEvent =
  | { readonly type: 'stdout'; readonly chunk: string }
  | { readonly type: 'stderr'; readonly chunk: string }
  | { readonly type: 'exit'; readonly exitCode: number | null; readonly signal: string | null };

export interface RuntimeProcessExecutor {
  execute(
    runtime: RuntimeHandle,
    request: RuntimeProcessRequest,
    signal: AbortSignal,
  ): AsyncIterable<RuntimeProcessEvent>;
}
