import { readFileSync, rmSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { computeSpecificationHash } from '../src/protocol/canonical-json.js';
import { parseRunRequest } from '../src/protocol/validation.js';
import type { RunRequest, RunState } from '../src/protocol/types.js';
import type { RunStore } from '../src/persistence/run-store.js';

const fixture = JSON.parse(
  readFileSync(join(process.cwd(), '..', 'contracts/agent-runs/v1/run-request.json'), 'utf8'),
) as unknown;

export function createRequest(runId: string): RunRequest {
  const request = structuredClone(parseRunRequest(fixture));
  request.specification.runId = runId;
  request.specificationHash = computeSpecificationHash(request.specification);
  return request;
}

export function createTempDirectory(): { path: string; dispose: () => void } {
  const path = mkdtempSync(join(tmpdir(), 'pm-agent-host-'));
  return { path, dispose: () => rmSync(path, { recursive: true, force: true }) };
}

export function completeRun(store: RunStore, runId: string): void {
  const transitions: RunState[] = [
    'starting_runtime',
    'starting_agent',
    'running',
    'validating',
    'collecting_artifacts',
    'completed',
  ];
  for (const state of transitions)
    store.transition(runId, state, `Transitioned to ${state}`, { nextState: state });
}

export async function waitUntil(
  predicate: () => boolean,
  timeoutMilliseconds = 2000,
): Promise<void> {
  const deadline = Date.now() + timeoutMilliseconds;
  while (!predicate()) {
    if (Date.now() >= deadline) throw new Error('Timed out waiting for test condition.');
    await new Promise<void>((resolve) => setTimeout(resolve, 5));
  }
}
