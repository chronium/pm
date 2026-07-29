import type { AgentRunEvent, AgentRunState } from './agent-runs-api.service';

export type AgentRunConnectivity =
  'loading' | 'connecting' | 'live' | 'reconnecting' | 'paused' | 'complete';

export interface AgentRunLogEntry {
  key: string;
  sequence: number;
  continuation: boolean;
  timestamp: string;
  source: string;
  type: string;
  message: string;
}

export type AgentRunCheckpointStatus = 'pending' | 'active' | 'complete' | 'failed' | 'cancelled';

export interface AgentRunCheckpoint {
  id: string;
  label: string;
  states: AgentRunState[];
  status: AgentRunCheckpointStatus;
  summary: string | null;
  failure: AgentRunFailure | null;
}

export interface AgentRunFailure {
  code: string;
  stage: string;
  summary: string;
  recommendedAction: string;
  retryable: boolean;
}

const ansi =
  /[\u001b\u009b][[\]()#;?]*(?:(?:(?:[a-zA-Z\d]*(?:;[-a-zA-Z\d/#&.:=?%@~_]+)*)?\u0007)|(?:(?:\d{1,4}(?:[;:]\d{0,4})*)?[\dA-PR-TZcf-nq-uy=><~]))/g;
const controls = /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/g;

const terminalStates = new Set<AgentRunState>(['completed', 'failed', 'cancelled']);

const checkpointDefinitions: Pick<AgentRunCheckpoint, 'id' | 'label' | 'states'>[] = [
  { id: 'accepted', label: 'Task accepted', states: ['accepted', 'queued'] },
  { id: 'workspace', label: 'Workspace preparation', states: ['preparing_workspace'] },
  { id: 'runtime', label: 'Runtime startup', states: ['starting_runtime', 'starting_agent'] },
  { id: 'agent', label: 'Codex execution', states: ['running'] },
  { id: 'validation', label: 'Validation', states: ['validating'] },
  { id: 'artifacts', label: 'Artifact collection', states: ['collecting_artifacts'] },
  { id: 'outcome', label: 'Run outcome', states: ['completed', 'failed', 'cancelled'] },
];

export function sanitizeRunText(value: string): string {
  return value.replace(ansi, '').replace(controls, '');
}

export function sanitizeRunEvent(event: AgentRunEvent): AgentRunEvent {
  return {
    ...event,
    type: sanitizeRunText(event.type),
    summary: sanitizeRunText(event.summary),
    data: sanitizeValue(event.data) as AgentRunEvent['data'],
  };
}

export function eventSource(type: string): string {
  return type.includes('.') ? type.slice(0, type.indexOf('.')) : type;
}

export function eventLogEntries(event: AgentRunEvent): AgentRunLogEntry[] {
  const source = eventSource(event.type);
  const detail = primaryText(event);
  const failure = runFailureFromEvent(event);
  const lines = failure
    ? [`${failure.summary} (${failure.code})`, `Recommended action: ${failure.recommendedAction}`]
    : (detail || event.summary).split(/\r?\n/);
  return (lines.length ? lines : ['']).map((line, index) => ({
    key: `${event.sequence}-${index}`,
    sequence: Number(event.sequence),
    continuation: index > 0,
    timestamp: event.timestamp,
    source,
    type: event.type,
    message: sanitizeRunText(line || (index === 0 ? event.summary : '')),
  }));
}

export function projectCheckpoints(
  states: ReadonlySet<AgentRunState>,
  currentState: AgentRunState | null,
  lastSummary: string | null,
  failure: AgentRunFailure | null = null,
): AgentRunCheckpoint[] {
  const terminal: 'completed' | 'failed' | 'cancelled' | null =
    currentState === 'completed' || currentState === 'failed' || currentState === 'cancelled'
      ? currentState
      : null;
  const currentIndex = checkpointDefinitions.findIndex((item) =>
    currentState ? item.states.includes(currentState) : false,
  );
  const lastObservedProgressIndex = checkpointDefinitions
    .slice(0, -1)
    .reduce(
      (last, item, index) => (item.states.some((state) => states.has(state)) ? index : last),
      -1,
    );
  return checkpointDefinitions.map((item, index) => {
    let status: AgentRunCheckpointStatus = 'pending';
    if (item.states.some((state) => states.has(state))) status = 'complete';
    if (!terminal && index === currentIndex) status = 'active';
    if (item.id === 'outcome' && terminal)
      status = terminal === 'completed' ? 'complete' : terminal;
    if (
      terminal &&
      terminal !== 'completed' &&
      item.id !== 'outcome' &&
      index === lastObservedProgressIndex
    )
      status = terminal;
    return {
      ...item,
      status,
      summary: item.id === 'outcome' && terminal ? lastSummary : null,
      failure: item.id === 'outcome' && terminal ? failure : null,
    };
  });
}

export function runFailureFromEvent(event: AgentRunEvent): AgentRunFailure | null {
  if (!event.data || typeof event.data !== 'object' || Array.isArray(event.data)) return null;
  const failure = (event.data as Record<string, unknown>)['failure'];
  if (!failure || typeof failure !== 'object' || Array.isArray(failure)) return null;
  const value = failure as Record<string, unknown>;
  if (
    typeof value['code'] !== 'string' ||
    typeof value['stage'] !== 'string' ||
    typeof value['summary'] !== 'string' ||
    typeof value['recommendedAction'] !== 'string' ||
    typeof value['retryable'] !== 'boolean'
  )
    return null;
  return {
    code: sanitizeRunText(value['code']),
    stage: sanitizeRunText(value['stage']),
    summary: sanitizeRunText(value['summary']),
    recommendedAction: sanitizeRunText(value['recommendedAction']),
    retryable: value['retryable'],
  };
}

export function isTerminalRunState(state: AgentRunState): boolean {
  return terminalStates.has(state);
}

function primaryText(event: AgentRunEvent): string | null {
  if (!event.data || typeof event.data !== 'object' || Array.isArray(event.data)) return null;
  const data = event.data as Record<string, unknown>;
  for (const key of ['output', 'text', 'message', 'command', 'query']) {
    if (typeof data[key] === 'string' && data[key].length) {
      const value = sanitizeRunText(data[key]);
      return event.type === 'command.output' || event.type === 'agent.message'
        ? value
        : `${event.summary}: ${value}`;
    }
  }
  return null;
}

function sanitizeValue(value: unknown): unknown {
  if (typeof value === 'string') return sanitizeRunText(value);
  if (Array.isArray(value)) return value.map(sanitizeValue);
  if (value && typeof value === 'object')
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>).map(([key, item]) => [
        sanitizeRunText(key),
        sanitizeValue(item),
      ]),
    );
  return value;
}
