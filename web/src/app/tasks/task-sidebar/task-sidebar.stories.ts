import { Component, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';

import { TaskSidebar } from './task-sidebar';
import { TaskSidebarStore, type BoardNavigationResponse } from './task-sidebar.store';

@Component({ template: '' })
class StoryRoute {}

const realistic: BoardNavigationResponse = {
  remainingCount: 37,
  activationEligibleCount: 25,
  milestones: [
    {
      key: 'angular',
      name: 'Angular replacement client',
      remainingCount: 12,
      activationEligibleCount: 12,
      lifecycle: 'active',
      unmetActivationTriggers: [],
    },
    {
      key: 'release',
      name: 'Release readiness and embedded production verification',
      remainingCount: 0,
      activationEligibleCount: 0,
      lifecycle: 'delivered',
      unmetActivationTriggers: [],
    },
    ...Array.from({ length: 10 }, (_, index) => ({
      key: `milestone-${index}`,
      name: `Milestone ${index + 1}`,
      remainingCount: index + 1,
      activationEligibleCount: index % 3 === 0 ? 0 : index + 1,
      lifecycle: index % 3 === 0 ? 'inactive' : 'active',
      unmetActivationTriggers: index % 3 === 0 ? ['entry'] : [],
    })),
  ],
  tracks: [
    { key: 'PM', name: 'Product management', remainingCount: 18, activationEligibleCount: 12 },
    {
      key: 'BUILD',
      name: 'Build and release infrastructure',
      remainingCount: 0,
      activationEligibleCount: 0,
    },
    { key: 'DOCS', name: 'Documentation', remainingCount: 4, activationEligibleCount: 3 },
  ],
  revision: 'navigation-story',
};

function storyStore(
  navigation: BoardNavigationResponse | undefined,
  loading = false,
  error: string | null = null,
) {
  return {
    navigation: signal(navigation),
    loading: signal(loading),
    error: signal(error),
    recommendationPending: signal(false),
    recommendationMessage: signal<string | null>(null),
    recommendationError: signal<string | null>(null),
    recommend: async () => null,
    reload: () => true,
  };
}

const meta = {
  title: 'Tasks/Task sidebar',
  component: TaskSidebar,
  parameters: { layout: 'fullscreen' },
  decorators: [
    applicationConfig({ providers: [provideRouter([{ path: 'tasks', component: StoryRoute }])] }),
    (story) => ({
      ...story(),
      styles: [
        'pm-task-sidebar { display: flex; width: 240px; height: 520px; padding: 12px 8px; background: var(--pm-surface-raised); }',
      ],
    }),
  ],
} satisfies Meta<TaskSidebar>;
export default meta;
type Story = StoryObj<typeof meta>;

export const LongListsAndZeroCounts: Story = {
  decorators: [
    applicationConfig({
      providers: [{ provide: TaskSidebarStore, useValue: storyStore(realistic) }],
    }),
  ],
};

export const ActiveMilestone: Story = {
  decorators: [
    applicationConfig({
      providers: [{ provide: TaskSidebarStore, useValue: storyStore(realistic) }],
    }),
  ],
  play: async ({ canvas }) => {
    canvas.getByText('Angular replacement client').click();
  },
};

export const Loading: Story = {
  decorators: [
    applicationConfig({
      providers: [{ provide: TaskSidebarStore, useValue: storyStore(undefined, true) }],
    }),
  ],
};

export const Error: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: TaskSidebarStore,
          useValue: storyStore(undefined, false, 'The navigation API could not be reached.'),
        },
      ],
    }),
  ],
};

export const EmptyCollections: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: TaskSidebarStore,
          useValue: storyStore({
            ...realistic,
            remainingCount: 0,
            activationEligibleCount: 0,
            tracks: [],
            milestones: [],
          }),
        },
      ],
    }),
  ],
};

export const NoReadyRecommendation: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: TaskSidebarStore,
          useValue: {
            ...storyStore(realistic),
            recommendationMessage: signal('No dependency-ready actionable task found.'),
          },
        },
      ],
    }),
  ],
};

export const RecommendationError: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: TaskSidebarStore,
          useValue: {
            ...storyStore(realistic),
            recommendationError: signal('The next task could not be recommended.'),
          },
        },
      ],
    }),
  ],
};
