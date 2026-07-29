import { eventLogEntries, projectCheckpoints, sanitizeRunEvent } from './agent-run-events';
import { runEvents } from './agent-runs.fixtures';

describe('agent run event projection', () => {
  it('strips ANSI and control sequences before producing fixed-height log lines', () => {
    const event = sanitizeRunEvent(runEvents[7]!);
    const entries = eventLogEntries(event);
    expect(entries.map((entry) => entry.message)).toEqual(['npm test', '133 tests passed']);
    expect(entries[1]).toMatchObject({ sequence: 8, continuation: true, source: 'command' });
  });

  it('projects checkpoints from the same observed run states', () => {
    const states = new Set(
      runEvents.slice(0, 7).flatMap((event) => (event.state ? [event.state] : [])),
    );
    const checkpoints = projectCheckpoints(states, 'running', 'Agent running');
    expect(checkpoints.map((checkpoint) => checkpoint.status)).toEqual([
      'complete',
      'complete',
      'complete',
      'active',
      'pending',
      'pending',
      'pending',
    ]);
  });

  it('keeps terminal failure understandable without requiring raw output', () => {
    const checkpoints = projectCheckpoints(
      new Set(['accepted', 'queued', 'preparing_workspace', 'failed']),
      'failed',
      'Workspace preparation failed',
    );
    expect(checkpoints.at(-1)).toMatchObject({
      status: 'failed',
      summary: 'Workspace preparation failed',
    });
    expect(checkpoints[1]?.status).toBe('failed');
    expect(checkpoints[2]?.status).toBe('pending');
  });
});
