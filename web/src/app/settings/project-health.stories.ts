import type { Meta, StoryObj } from '@storybook/angular-vite';

import { ProjectHealth } from './project-health';

const invalid = {
  valid: false,
  issues: [
    {
      severity: 'error',
      code: 'task_state_missing',
      message:
        'Task PM-0052 references a state that is no longer configured; this deliberately long explanation wraps without hiding context.',
      taskId: 'PM-0052',
      wikiPath: 'architecture/angular/settings-and-validation-with-a-deliberately-long-path.md',
      state: 'archived',
      path: '.pm/tasks/PM-0052-with-a-deliberately-long-file-name.md',
      projectId: null,
      projectAlias: null,
    },
  ],
};

const validWithWarnings = {
  valid: true,
  issues: [
    {
      severity: 'warning',
      code: 'linked_project_missing',
      message: 'Linked project prj_gameplay (gameplay) is missing.',
      taskId: null,
      wikiPath: null,
      state: null,
      path: null,
      projectId: 'prj_gameplay',
      projectAlias: 'gameplay',
    },
  ],
};

const meta = {
  title: 'Settings/Project health',
  component: ProjectHealth,
  parameters: { layout: 'padded' },
} satisfies Meta<ProjectHealth>;
export default meta;
type Story = StoryObj<typeof meta>;

export const Valid: Story = { args: { validation: { valid: true, issues: [] } } };
export const ValidWithWarnings: Story = { args: { validation: validWithWarnings } };
export const Invalid: Story = { args: { validation: invalid } };
export const Loading: Story = { args: { loading: true } };
export const Refreshing: Story = { args: { validation: invalid, refreshing: true } };
export const Error: Story = {
  args: { error: 'Project health could not be loaded. Settings remain available.' },
};
export const LongContentMobile: Story = {
  args: { validation: invalid },
  globals: { viewport: 'mobile' },
};
