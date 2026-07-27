import type { RunState } from './protocol/types.js';

const forwardTransitions = new Map<RunState, RunState>([
  ['requested', 'accepted'],
  ['accepted', 'queued'],
  ['queued', 'preparing_workspace'],
  ['preparing_workspace', 'starting_runtime'],
  ['starting_runtime', 'starting_agent'],
  ['starting_agent', 'running'],
  ['running', 'validating'],
  ['validating', 'collecting_artifacts'],
  ['collecting_artifacts', 'completed'],
]);

export const activeRunStates: readonly RunState[] = [
  'preparing_workspace',
  'starting_runtime',
  'starting_agent',
  'running',
  'validating',
  'collecting_artifacts',
];

export const terminalRunStates: readonly RunState[] = ['completed', 'failed', 'cancelled'];

export function isTerminal(state: RunState): boolean {
  return terminalRunStates.includes(state);
}

export function canTransition(current: RunState, next: RunState): boolean {
  if (isTerminal(current)) return false;
  if (forwardTransitions.get(current) === next) return true;
  return current !== 'requested' && (next === 'failed' || next === 'cancelled');
}
