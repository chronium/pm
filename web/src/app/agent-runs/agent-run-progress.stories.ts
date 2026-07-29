import type { Meta, StoryObj } from '@storybook/angular-vite';

import { projectCheckpoints } from './agent-run-events';
import { AgentRunProgress } from './agent-run-progress';
import { runArtifacts, runInspection } from './agent-runs.fixtures';
import type { AgentRunState } from './agent-runs-api.service';

function inspection(state: AgentRunState) {
  return {
    ...runInspection,
    run: {
      ...runInspection.run,
      state,
      terminalAt:
        state === 'completed' || state === 'failed' || state === 'cancelled'
          ? '2026-07-29T08:10:00.000Z'
          : null,
    },
  };
}

function statesThrough(state: AgentRunState): ReadonlySet<AgentRunState> {
  const ordered: AgentRunState[] = [
    'accepted',
    'queued',
    'preparing_workspace',
    'starting_runtime',
    'starting_agent',
    'running',
    'validating',
    'collecting_artifacts',
  ];
  const index = ordered.indexOf(state);
  return new Set(index >= 0 ? ordered.slice(0, index + 1) : [...ordered, state]);
}

const meta = {
  title: 'Agent runs/Progress',
  component: AgentRunProgress,
  args: {
    inspection: inspection('running'),
    checkpoints: projectCheckpoints(statesThrough('running'), 'running', null),
    artifacts: [],
  },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<AgentRunProgress>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Queued: Story = {
  args: {
    inspection: inspection('queued'),
    checkpoints: projectCheckpoints(statesThrough('queued'), 'queued', null),
  },
};
export const Running: Story = {};
export const Failed: Story = {
  args: {
    inspection: inspection('failed'),
    checkpoints: projectCheckpoints(statesThrough('failed'), 'failed', 'Run validation failed.', {
      code: 'validation_failed',
      stage: 'validation',
      summary: 'Run validation failed.',
      recommendedAction:
        'Review the failed validation step and collected patch before deciding whether to retry.',
      retryable: false,
    }),
  },
};
export const Cancelled: Story = {
  args: {
    inspection: inspection('cancelled'),
    checkpoints: projectCheckpoints(statesThrough('cancelled'), 'cancelled', 'Cancelled by user.'),
  },
};
export const Completed: Story = {
  args: {
    inspection: inspection('completed'),
    checkpoints: projectCheckpoints(statesThrough('completed'), 'completed', 'Run completed.'),
    artifacts: runArtifacts,
  },
};
export const ArtifactDownloading: Story = {
  args: {
    inspection: inspection('completed'),
    checkpoints: projectCheckpoints(statesThrough('completed'), 'completed', 'Run completed.'),
    artifacts: runArtifacts,
    artifactDownloads: {
      'changes-patch': { status: 'downloading', message: null },
    },
  },
};
export const ArtifactDownloadFailed: Story = {
  args: {
    inspection: inspection('completed'),
    checkpoints: projectCheckpoints(statesThrough('completed'), 'completed', 'Run completed.'),
    artifacts: runArtifacts,
    artifactDownloads: {
      'changes-patch': {
        status: 'error',
        message: 'Artifact integrity verification failed.',
      },
    },
  },
};
export const TaskDrift: Story = {
  args: {
    inspection: {
      ...inspection('running'),
      taskChanged: true,
      currentTaskRevision: 'task-r3',
    },
  },
};
