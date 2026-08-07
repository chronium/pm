import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';

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
      '<details pmTaskMilestone [milestone]="milestone" [headingId]="headingId" ' +
      '[selectedTaskId]="selectedTaskId" [openStates]="openStates" ' +
      '[milestoneOpen]="milestoneOpen"></details>',
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
