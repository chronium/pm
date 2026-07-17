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
  milestones: [
    { key: 'angular', name: 'Angular replacement client', remainingCount: 12 },
    {
      key: 'release',
      name: 'Release readiness and embedded production verification',
      remainingCount: 0,
    },
    ...Array.from({ length: 10 }, (_, index) => ({
      key: `milestone-${index}`,
      name: `Milestone ${index + 1}`,
      remainingCount: index + 1,
    })),
  ],
  tracks: [
    { key: 'PM', name: 'Product management', remainingCount: 18 },
    { key: 'BUILD', name: 'Build and release infrastructure', remainingCount: 0 },
    { key: 'DOCS', name: 'Documentation', remainingCount: 4 },
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
          useValue: storyStore({ ...realistic, remainingCount: 0, tracks: [], milestones: [] }),
        },
      ],
    }),
  ],
};
