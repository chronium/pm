import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect } from 'storybook/test';

import type { BoardTask } from '../tasks-board.store';
import { TaskRow } from './task-row';

const ready: BoardTask = {
  id: 'PM-0055',
  title: 'Decompose the Angular task board',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  state: 'todo',
  dependencies: {
    ready: true,
    dependsOn: ['PM-0049'],
    waitingOn: [],
    missing: [],
    summary: 'ready',
  },
  activation: {
    isEligible: true,
    milestoneLifecycle: 'active',
    requiredActivationTriggers: ['entry'],
    unmetActivationTriggers: [],
    summary: 'Eligible: milestone angular-web is active.',
  },
  descriptionPreview: 'Extract meaningful regions without changing the established layout.',
  modifiedAt: '2026-07-15T07:48:04Z',
};
const blocked: BoardTask = {
  ...ready,
  id: 'PM-0056',
  title:
    'A long blocked task title that wraps cleanly on narrow screens while preserving all content',
  dependencies: {
    ready: false,
    dependsOn: ['PM-9999'],
    waitingOn: [],
    missing: ['PM-9999'],
    summary: 'missing PM-9999',
  },
  activation: {
    isEligible: false,
    milestoneLifecycle: 'inactive',
    requiredActivationTriggers: ['entry'],
    unmetActivationTriggers: ['entry'],
    summary: 'Ineligible: milestone angular-web is inactive; unmet activation triggers: entry.',
  },
};
const meta = {
  title: 'Tasks/Task row',
  component: TaskRow,
  decorators: [
    applicationConfig({ providers: [provideRouter([], withDisabledInitialNavigation())] }),
  ],
  parameters: { layout: 'fullscreen' },
  render: (args) => ({
    props: args,
    template:
      '<ul style="margin: 0; padding: 0; list-style: none"><li pmTaskRow [task]="task" [selected]="selected"></li></ul>',
  }),
} satisfies Meta<TaskRow>;
export default meta;
type Story = StoryObj<typeof meta>;
export const Ready: Story = {
  args: { task: ready, selected: false },
  play: async ({ canvasElement }) => {
    const priority = canvasElement.querySelector<HTMLElement>('pm-priority-indicator');
    const statuses = [...canvasElement.querySelectorAll<HTMLElement>('.task-status')];
    expect(priority?.dataset['priority']).toBe('high');
    expect(priority?.getAttribute('aria-label')).toBe('Priority: high');
    expect(statuses).toHaveLength(2);
    expect(statuses.map((status) => status.dataset['icon'])).toEqual([
      'cssUnblock',
      'cssLockUnlock',
    ]);
    for (const status of statuses)
      expect(status.getBoundingClientRect().height).toBeLessThanOrEqual(20);
  },
};
export const BlockedSelected: Story = { args: { task: blocked, selected: true } };
export const LongContentMobile: Story = {
  args: { task: blocked, selected: false },
  globals: { viewport: 'mobile' },
};
