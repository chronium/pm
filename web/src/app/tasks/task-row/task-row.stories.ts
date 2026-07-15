import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';

import type { BoardTask } from '../tasks-board.store';
import { TaskRow } from './task-row';

const ready: BoardTask = { id: 'PM-0055', title: 'Decompose the Angular task board', track: 'PM', milestone: 'angular-web', priority: 'high', prioritySource: 'milestone', state: 'todo', dependencies: { ready: true, dependsOn: ['PM-0049'], waitingOn: [], missing: [], summary: 'ready' }, descriptionPreview: 'Extract meaningful regions without changing the established layout.', modifiedAt: '2026-07-15T07:48:04Z' };
const blocked: BoardTask = { ...ready, id: 'PM-0056', title: 'A long blocked task title that wraps cleanly on narrow screens while preserving all content', dependencies: { ready: false, dependsOn: ['PM-9999'], waitingOn: [], missing: ['PM-9999'], summary: 'missing PM-9999' } };
const meta = {
  title: 'Tasks/Task row',
  component: TaskRow,
  decorators: [applicationConfig({ providers: [provideRouter([], withDisabledInitialNavigation())] })],
  parameters: { layout: 'fullscreen' },
  render: (args) => ({
    props: args,
    template: '<ul style="margin: 0; padding: 0; list-style: none"><li pmTaskRow [task]="task" [selected]="selected"></li></ul>',
  }),
} satisfies Meta<TaskRow>;
export default meta;
type Story = StoryObj<typeof meta>;
export const Ready: Story = { args: { task: ready, selected: false } };
export const BlockedSelected: Story = { args: { task: blocked, selected: true } };
export const LongContentMobile: Story = { args: { task: blocked, selected: false }, globals: { viewport: 'mobile' } };
