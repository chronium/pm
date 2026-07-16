import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';

import type { BoardStateGroup, BoardTask } from '../tasks-board.store';
import { TaskStatusGroup } from './task-status-group';

const task: BoardTask = {
  id: 'PM-0055',
  title: 'Decompose the Angular task board',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  state: 'todo',
  dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
  descriptionPreview: 'A focused component story.',
  modifiedAt: '2026-07-15T07:48:04Z',
};
const state: BoardStateGroup = { key: 'todo', name: 'To do', tasks: [task] };
const meta = {
  title: 'Tasks/Status group',
  component: TaskStatusGroup,
  decorators: [
    applicationConfig({ providers: [provideRouter([], withDisabledInitialNavigation())] }),
  ],
  parameters: { layout: 'fullscreen' },
} satisfies Meta<TaskStatusGroup>;
export default meta;
type Story = StoryObj<typeof meta>;
export const Open: Story = { args: { state, selectedTaskId: null, open: true } };
export const Closed: Story = { args: { state, selectedTaskId: null, open: false } };
export const SelectedTask: Story = { args: { state, selectedTaskId: 'PM-0055', open: true } };
