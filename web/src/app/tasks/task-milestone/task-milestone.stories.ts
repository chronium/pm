import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent } from 'storybook/test';

import type { BoardMilestoneGroup, BoardTask } from '../tasks-board.store';
import { TaskMilestone } from './task-milestone';

@Component({ template: '' })
class StoryRoute {}

const inactiveTask: BoardTask = {
  id: 'PM-0093',
  title: 'Present milestone lifecycle and activation eligibility on the board',
  track: 'PM',
  milestone: 'milestone-activation',
  priority: 'high',
  prioritySource: 'milestone',
  state: 'todo',
  dependencies: {
    ready: true,
    dependsOn: ['PM-0092'],
    waitingOn: [],
    missing: [],
    summary: 'all dependencies complete',
  },
  activation: {
    isEligible: false,
    milestoneLifecycle: 'inactive',
    requiredActivationTriggers: ['activation-api', 'board-ready'],
    unmetActivationTriggers: ['board-ready'],
    summary:
      'Ineligible: milestone milestone-activation is inactive; unmet activation triggers: board-ready.',
  },
  descriptionPreview: 'Expose activation without hiding work that is not yet eligible.',
  modifiedAt: '2026-08-07T06:30:00Z',
};

const inactive: BoardMilestoneGroup = {
  key: 'milestone-activation',
  name: 'Milestone Deliverables and Activation Triggers',
  description:
    'Deliver **latched activation gates** across the CLI, API, and board.\n\n' +
    '- Preserve inactive work for planning and review.\n' +
    '- Exclude it from next-task recommendations until its gates open.',
  lifecycle: 'inactive',
  requiredActivationTriggers: ['activation-api', 'board-ready'],
  unmetActivationTriggers: ['board-ready'],
  states: [{ key: 'todo', name: 'To do', tasks: [inactiveTask] }],
};

const delivered: BoardMilestoneGroup = {
  ...inactive,
  key: 'public-beta',
  name: 'Public beta',
  lifecycle: 'delivered',
  unmetActivationTriggers: [],
  states: [
    {
      key: 'todo',
      name: 'To do',
      tasks: [
        {
          ...inactiveTask,
          id: 'PM-0101',
          title: 'Deferred beta hardening accepted at delivery',
          milestone: 'public-beta',
          activation: {
            isEligible: false,
            milestoneLifecycle: 'delivered',
            requiredActivationTriggers: ['activation-api'],
            unmetActivationTriggers: [],
            summary: 'Ineligible: milestone public-beta is delivered.',
          },
        },
      ],
    },
  ],
};

const meta = {
  title: 'Tasks/Milestone',
  component: TaskMilestone,
  decorators: [
    applicationConfig({
      providers: [provideRouter([{ path: 'tasks/dialog/:id', component: StoryRoute }])],
    }),
  ],
  parameters: { layout: 'fullscreen' },
  render: (args) => ({
    props: args,
    template:
      '<section class="pm-board-surface">' +
      '<details pmTaskMilestone [milestone]="milestone" [headingId]="headingId" ' +
      '[selectedTaskId]="selectedTaskId" [openStates]="openStates" ' +
      '[milestoneOpen]="milestoneOpen"></details>' +
      '</section>',
  }),
} satisfies Meta<TaskMilestone>;

export default meta;
type Story = StoryObj<typeof meta>;

export const InactiveWithDeliverable: Story = {
  args: {
    milestone: inactive,
    headingId: 'milestone-inactive',
    selectedTaskId: null,
    openStates: { todo: true },
    milestoneOpen: true,
  },
};

export const InactiveWithDeliverableDark: Story = {
  ...InactiveWithDeliverable,
  globals: { theme: 'dark' },
};

const expandDeliverable: NonNullable<Story['play']> = async ({ canvasElement }) => {
  const details = canvasElement.querySelector<HTMLDetailsElement>('.deliverable-description');
  const statusGroups = canvasElement.querySelector<HTMLElement>('.status-groups');
  const summary = details?.querySelector<HTMLElement>('summary');
  const caret = details?.querySelector<HTMLElement>('.deliverable-disclosure');
  const milestoneCaret = canvasElement.querySelector<HTMLElement>('.milestone-disclosure');
  const statusCaret = canvasElement.querySelector<HTMLElement>('.disclosure-icon');
  const deliverableTitle = details?.querySelector<HTMLElement>('.deliverable-title');
  const milestoneTitle = canvasElement.querySelector<HTMLElement>('.milestone-heading');
  const milestoneSummary = canvasElement.querySelector<HTMLElement>('.milestone-summary');
  const statusTitle = canvasElement.querySelector<HTMLElement>('.status-name');
  const statusSummary = canvasElement.querySelector<HTMLElement>('.status-group > summary');
  expect(details).not.toBeNull();
  expect(statusGroups).not.toBeNull();
  expect(summary).not.toBeNull();
  expect(caret).not.toBeNull();
  expect(milestoneCaret).not.toBeNull();
  expect(statusCaret).not.toBeNull();
  expect(deliverableTitle).not.toBeNull();
  expect(milestoneTitle).not.toBeNull();
  expect(milestoneSummary).not.toBeNull();
  expect(statusTitle).not.toBeNull();
  expect(statusSummary).not.toBeNull();
  expect(caret!.getBoundingClientRect().left).toBe(milestoneCaret!.getBoundingClientRect().left);
  expect(caret!.getBoundingClientRect().left).toBe(statusCaret!.getBoundingClientRect().left);
  expect(deliverableTitle!.getBoundingClientRect().left).toBe(
    milestoneTitle!.getBoundingClientRect().left,
  );
  expect(deliverableTitle!.getBoundingClientRect().left).toBe(
    statusTitle!.getBoundingClientRect().left,
  );
  expect(getComputedStyle(details!).backgroundColor).toBe('rgba(0, 0, 0, 0)');
  expect(getComputedStyle(details!).backgroundColor).toBe(
    getComputedStyle(statusSummary!).backgroundColor,
  );
  expect(getComputedStyle(details!).borderBottomWidth).toBe(
    getComputedStyle(milestoneSummary!).borderBottomWidth,
  );
  expect(getComputedStyle(details!).borderBottomColor).toBe(
    getComputedStyle(milestoneSummary!).borderBottomColor,
  );
  expect(getComputedStyle(summary!).borderBottomWidth).toBe('0px');
  expect(getComputedStyle(details!).borderRadius).toBe('0px');
  expect(details!.open).toBe(false);
  await userEvent.click(summary!);
  expect(details!.open).toBe(true);
  await userEvent.click(summary!);
  expect(details!.open).toBe(false);
  await userEvent.click(summary!);
  expect(details!.open).toBe(true);
  expect(getComputedStyle(caret!).transform).not.toBe('none');
  expect(statusGroups!.getBoundingClientRect().top).toBe(details!.getBoundingClientRect().bottom);
};

export const ExpandedDeliverable: Story = {
  ...InactiveWithDeliverable,
  play: expandDeliverable,
};

export const ExpandedDeliverableDark: Story = {
  ...InactiveWithDeliverable,
  globals: { theme: 'dark' },
  play: expandDeliverable,
};

export const DeliveredWithAcceptedWork: Story = {
  args: {
    milestone: delivered,
    headingId: 'milestone-delivered',
    selectedTaskId: null,
    openStates: { todo: true },
    milestoneOpen: true,
  },
};

export const InactiveMobile: Story = {
  ...InactiveWithDeliverable,
  globals: { viewport: 'mobile' },
};
