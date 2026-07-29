import type { Meta, StoryObj } from '@storybook/angular-vite';

import { eventLogEntries, type AgentRunLogEntry } from './agent-run-events';
import { AgentRunOutput } from './agent-run-output';
import { runEvents } from './agent-runs.fixtures';

const entries = runEvents.flatMap(eventLogEntries);
const longEntries: AgentRunLogEntry[] = Array.from({ length: 20_000 }, (_, index) => ({
  key: `${index + 1}-0`,
  sequence: index + 1,
  continuation: false,
  timestamp: '2026-07-29T08:03:08.000Z',
  source: index % 3 === 0 ? 'command' : index % 3 === 1 ? 'agent' : 'validation',
  type: 'command.output',
  message: `Structured output line ${index + 1}`,
}));

const meta = {
  title: 'Agent runs/Output',
  component: AgentRunOutput,
  args: {
    entries,
    connectivity: 'live',
    paused: false,
    droppedEntries: 0,
    downloading: false,
  },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<AgentRunOutput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Live: Story = {};
export const Reconnecting: Story = { args: { connectivity: 'reconnecting' } };
export const LongLog: Story = { args: { entries: longEntries, droppedEntries: 12_438 } };
export const Mobile: Story = { globals: { viewport: 'mobile' } };
