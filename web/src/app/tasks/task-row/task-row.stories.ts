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
const contentTask: BoardTask = {
  ...blocked,
  id: 'CONTENT-0018',
  title: 'Freeze initial Draft 0 Fire Arrow inputs',
  track: 'CONTENT',
  milestone: 'M8',
  priority: 'medium',
};
const protocolTask: BoardTask = {
  ...blocked,
  id: 'PROTOCOL-0011',
  title: 'Add Fire Arrow facts and serialization',
  track: 'PROTOCOL',
  milestone: 'M8',
  priority: 'medium',
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
export const BlockedSelected: Story = {
  args: { task: blocked, selected: true },
};
export const LongContentMobile: Story = {
  args: { task: blocked, selected: false },
  globals: { viewport: 'mobile' },
};

const longIdsDesktopRender: NonNullable<Story['render']> = () => ({
  props: { contentTask, protocolTask },
  template: `
    <main style="padding: 24px">
      <ul style="margin: 0; padding: 0; list-style: none">
        <li pmTaskRow [task]="contentTask" [selected]="false"></li>
        <li pmTaskRow [task]="protocolTask" [selected]="true"></li>
      </ul>
    </main>
  `,
});

const verifyLongIdsDesktop: NonNullable<Story['play']> = async ({ canvasElement }) => {
  const rows = [...canvasElement.querySelectorAll<HTMLElement>('li[pmTaskRow] a')];
  expect(rows).toHaveLength(2);
  for (const row of rows) {
    expect(row.getBoundingClientRect().height).toBe(52);
    expect(row.scrollWidth).toBeLessThanOrEqual(row.clientWidth);
  }
  for (const taskId of canvasElement.querySelectorAll<HTMLElement>('.task-id')) {
    expect(taskId.scrollHeight).toBeLessThanOrEqual(taskId.clientHeight);
  }
  for (const row of canvasElement.querySelectorAll<HTMLElement>('li[pmTaskRow]')) {
    const priority = row.querySelector<HTMLElement>('pm-priority-indicator');
    const taskId = row.querySelector<HTMLElement>('.task-id');
    const title = row.querySelector<HTMLElement>('.task-title');
    expect(priority).not.toBeNull();
    expect(taskId).not.toBeNull();
    expect(title).not.toBeNull();
    expect(taskId!.getBoundingClientRect().bottom).toBeLessThanOrEqual(
      title!.getBoundingClientRect().top,
    );
    const priorityBounds = priority!.getBoundingClientRect();
    const taskIdBounds = taskId!.getBoundingClientRect();
    expect(
      Math.abs(
        priorityBounds.top +
          priorityBounds.height / 2 -
          (taskIdBounds.top + taskIdBounds.height / 2),
      ),
    ).toBeLessThanOrEqual(1);
    expect(taskIdBounds.left - priorityBounds.right).toBe(8);
    expect(priorityBounds.left).toBe(title!.getBoundingClientRect().left);
  }
};

export const LongIdsDesktop: Story = {
  args: { task: contentTask, selected: false },
  render: longIdsDesktopRender,
  play: verifyLongIdsDesktop,
};

export const LongIdsDesktopDark: Story = {
  args: { task: contentTask, selected: false },
  globals: { theme: 'dark' },
  render: longIdsDesktopRender,
  play: verifyLongIdsDesktop,
};
