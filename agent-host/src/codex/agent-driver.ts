import type {
  AgentDriver,
  AgentDriverEvent,
  RuntimeHandle,
  RuntimeProcessExecutor,
} from '../drivers.js';
import type { RunRequest } from '../protocol/types.js';
import { sanitizeEventDraft } from '../protocol/event-sanitizer.js';
import {
  codexWorkerProtocolVersion,
  parseCodexWorkerMessage,
  type CodexWorkerRequest,
} from './protocol.js';

const maximumLineBytes = 1_048_576;
const maximumStdoutBytes = 67_108_864;
const maximumStderrBytes = 16_384;
const maximumMessages = 100_000;

export class CodexAgentDriver implements AgentDriver {
  constructor(private readonly executor: RuntimeProcessExecutor) {}

  async *execute(
    request: RunRequest,
    runtime: RuntimeHandle,
    signal: AbortSignal,
  ): AsyncIterable<AgentDriverEvent> {
    const context = runtime.agentContext;
    const workerRequest: CodexWorkerRequest = {
      protocolVersion: codexWorkerProtocolVersion,
      runRequest: request,
      workspaceDirectory: context.workspaceDirectory,
      codexHomeDirectory: context.codexHomeDirectory,
      networkAccessEnabled: context.networkAccessEnabled,
      pmMcpCommand: context.pmMcpCommand,
      environmentNames: Object.keys(context.environment).sort(),
    };
    const decoder = new WorkerOutputDecoder();
    let stderr = '';
    let exitCode: number | null | undefined;
    let exitSignal: string | null = null;
    let completed = false;
    let failure: { errorCode: string; message: string } | undefined;
    let stdoutBytes = 0;
    let messageCount = 0;

    for await (const processEvent of this.executor.execute(
      runtime,
      {
        command: context.workerCommand,
        workingDirectory: context.workspaceDirectory,
        environment: context.environment,
        standardInput: JSON.stringify(workerRequest),
      },
      signal,
    )) {
      if (processEvent.type === 'stderr') {
        if (stderr.length < maximumStderrBytes)
          stderr += processEvent.chunk.slice(0, maximumStderrBytes - stderr.length);
        continue;
      }
      if (processEvent.type === 'exit') {
        exitCode = processEvent.exitCode;
        exitSignal = processEvent.signal;
        continue;
      }
      stdoutBytes += Buffer.byteLength(processEvent.chunk);
      if (stdoutBytes > maximumStdoutBytes) throw new Error('Codex worker output is too large.');
      for (const line of decoder.append(processEvent.chunk)) {
        messageCount += 1;
        if (messageCount > maximumMessages)
          throw new Error('Codex worker emitted too many events.');
        const message = parseLine(line);
        if (message.type === 'event') yield message.event;
        else if (message.type === 'completed') completed = true;
        else failure = { errorCode: message.errorCode, message: message.message };
      }
    }

    for (const line of decoder.finish()) {
      messageCount += 1;
      if (messageCount > maximumMessages) throw new Error('Codex worker emitted too many events.');
      const message = parseLine(line);
      if (message.type === 'event') yield message.event;
      else if (message.type === 'completed') completed = true;
      else failure = { errorCode: message.errorCode, message: message.message };
    }

    if (signal.aborted) throw abortError();
    if (failure !== undefined) {
      const sanitized = sanitizeEventDraft('agent.worker_failed', failure.message, {
        errorCode: failure.errorCode,
      });
      yield { type: sanitized.type, summary: sanitized.summary, data: sanitized.data };
      throw new Error('Codex worker reported failure.');
    }
    if (exitCode !== 0 || exitSignal !== null || !completed) {
      const sanitizedStderr = sanitizeEventDraft('agent.worker_failed', 'Codex worker failed', {
        exitCode: exitCode ?? null,
        signal: exitSignal,
        stderr,
      });
      yield {
        type: sanitizedStderr.type,
        summary: sanitizedStderr.summary,
        data: sanitizedStderr.data,
      };
      throw new Error('Codex worker exited without completing.');
    }
  }
}

class WorkerOutputDecoder {
  private pending = '';

  append(chunk: string): string[] {
    this.pending += chunk;
    if (Buffer.byteLength(this.pending) > maximumLineBytes && !this.pending.includes('\n'))
      throw new Error('Codex worker output line is too large.');
    const lines = this.pending.split('\n');
    this.pending = lines.pop() ?? '';
    for (const line of lines)
      if (Buffer.byteLength(line) > maximumLineBytes)
        throw new Error('Codex worker output line is too large.');
    return lines.filter((line) => line.length > 0);
  }

  finish(): string[] {
    if (this.pending.length === 0) return [];
    if (Buffer.byteLength(this.pending) > maximumLineBytes)
      throw new Error('Codex worker output line is too large.');
    const line = this.pending;
    this.pending = '';
    return [line];
  }
}

function parseLine(line: string) {
  try {
    return parseCodexWorkerMessage(JSON.parse(line) as unknown);
  } catch {
    throw new Error('Codex worker emitted invalid JSONL.');
  }
}

function abortError(): Error {
  const error = new Error('Codex worker execution was cancelled.');
  error.name = 'AbortError';
  return error;
}
