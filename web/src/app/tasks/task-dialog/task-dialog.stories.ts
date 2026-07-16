import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig, moduleMetadata } from '@storybook/angular-vite';

import { MarkdownDisplay } from '../../markdown/markdown-display';
import type { TaskResponse } from '../task-api.service';
import { TaskCreateForm } from './task-create-form';
import { TaskDialogShell } from './task-dialog-shell';
import { TaskEditForm } from './task-edit-form';

const options = [
  { key: 'PM', name: 'Product', priority: 'medium' },
  { key: 'BUILD', name: 'Build', priority: 'high' },
];
const states = [
  { key: 'todo', name: 'To do', priority: 'medium' },
  { key: 'done', name: 'Done', priority: 'low' },
];
const task: TaskResponse = {
  id: 'PM-0050',
  title: 'Implement routed Angular task dialogs',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  prioritySelection: 'inherit',
  state: 'todo',
  dependencies: {
    ready: true,
    dependsOn: ['PM-0049'],
    waitingOn: [],
    missing: [],
    summary: 'ready',
  },
  createdAt: '2026-07-15T00:00:00Z',
  modifiedAt: '2026-07-15T08:00:00Z',
  description: '## Goal\n\nProvide **complete task workflows** while preserving board context.',
  revision: 'r1',
  localMetadata: { filePath: '.pm/tasks/PM-0050.md' },
};

const meta = {
  title: 'Tasks/Task dialogs',
  decorators: [
    moduleMetadata({ imports: [MarkdownDisplay, TaskCreateForm, TaskDialogShell, TaskEditForm] }),
    applicationConfig({ providers: [provideRouter([], withDisabledInitialNavigation())] }),
  ],
  parameters: { layout: 'fullscreen' },
} satisfies Meta;
export default meta;
type Story = StoryObj<typeof meta>;

export const Create: Story = {
  render: () => ({
    props: { options },
    template:
      '<pm-task-dialog-shell title="Create task" eyebrow="New task"><pm-task-create-form [tracks]="options" [milestones]="[]" /></pm-task-dialog-shell>',
  }),
};
export const Read: Story = {
  render: () => ({
    props: { task },
    template:
      '<pm-task-dialog-shell [title]="task.title" [eyebrow]="task.id"><article><pm-markdown-display [markdown]="task.description" /><hr><p>{{ task.track }} · {{ task.milestone }} · {{ task.state }} · {{ task.priority }}</p><p>Dependencies: {{ task.dependencies.summary }}</p></article></pm-task-dialog-shell>',
  }),
};
export const Edit: Story = {
  render: () => ({
    props: { task, states },
    template:
      '<pm-task-dialog-shell [title]="task.title" [eyebrow]="task.id"><pm-task-edit-form [task]="task" [states]="states" /></pm-task-dialog-shell>',
  }),
};
export const EditConflict: Story = {
  render: () => ({
    props: { task, states },
    template:
      '<pm-task-dialog-shell [title]="task.title" [eyebrow]="task.id"><pm-task-edit-form [task]="task" [states]="states" [stale]="true" apiError="This task changed elsewhere. Reload latest before saving again." /></pm-task-dialog-shell>',
  }),
};
export const ReadMobileDark: Story = { ...Read, globals: { viewport: 'mobile', theme: 'dark' } };
