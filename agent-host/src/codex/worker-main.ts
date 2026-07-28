#!/usr/bin/env node
import { sanitizeEventDraft } from '../protocol/event-sanitizer.js';
import { executeCodexWorker, CodexWorkerError } from './worker.js';
import { codexWorkerProtocolVersion, type CodexWorkerMessage } from './protocol.js';

const maximumInputBytes = 1_048_576;

async function main(): Promise<void> {
  const controller = new AbortController();
  const cancel = (): void => controller.abort('runtime_terminated');
  process.once('SIGINT', cancel);
  process.once('SIGTERM', cancel);
  try {
    const input = await readStandardInput();
    for await (const event of executeCodexWorker(input, controller.signal))
      writeMessage({
        protocolVersion: codexWorkerProtocolVersion,
        type: 'event',
        event,
      });
    writeMessage({ protocolVersion: codexWorkerProtocolVersion, type: 'completed' });
  } catch (error) {
    const failure = failureDetails(error, controller.signal.aborted);
    writeMessage({
      protocolVersion: codexWorkerProtocolVersion,
      type: 'failed',
      errorCode: failure.errorCode,
      message: failure.message,
    });
    process.exitCode = controller.signal.aborted ? 130 : 1;
  } finally {
    process.removeListener('SIGINT', cancel);
    process.removeListener('SIGTERM', cancel);
  }
}

async function readStandardInput(): Promise<unknown> {
  const chunks: Buffer[] = [];
  let bytes = 0;
  for await (const chunk of process.stdin) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk as Uint8Array);
    bytes += buffer.byteLength;
    if (bytes > maximumInputBytes) throw new Error('Codex worker request is too large.');
    chunks.push(buffer);
  }
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8')) as unknown;
  } catch {
    throw new Error('Codex worker request is invalid JSON.');
  }
}

function writeMessage(message: CodexWorkerMessage): void {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

function failureDetails(
  error: unknown,
  cancelled: boolean,
): { errorCode: string; message: string } {
  if (cancelled) return { errorCode: 'codex_worker_cancelled', message: 'Codex worker cancelled.' };
  if (error instanceof CodexWorkerError)
    return { errorCode: error.code, message: safeMessage(error.message) };
  return { errorCode: 'codex_worker_failed', message: 'Codex worker failed.' };
}

function safeMessage(message: string): string {
  return sanitizeEventDraft('agent.worker_failed', message, null).summary;
}

void main();
