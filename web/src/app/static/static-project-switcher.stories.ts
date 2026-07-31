import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular-vite';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import { of } from 'rxjs';

import { StaticSnapshotStore, type StaticSnapshot } from './static-snapshot.interceptor';
import { StaticProjectSwitcher } from './static-project-switcher';

const snapshot = {
  project: {
    projectId: 'games',
    name: 'Games',
    accent: 'teal',
    relationship: 'current',
    readOnly: true,
    revision: 'static-snapshot',
  },
  linkedProjects: [
    {
      projectId: 'royale',
      name: 'Royale',
      alias: 'royale',
      relationship: 'child',
      publicSiteUrl: 'https://example.test/royale/',
    },
    {
      projectId: 'starfall',
      name: 'Starfall',
      alias: 'starfall',
      relationship: 'child',
      publicSiteUrl: null,
    },
  ],
} as StaticSnapshot;

const meta: Meta<StaticProjectSwitcher> = {
  title: 'Static/Project switcher',
  component: StaticProjectSwitcher,
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([], withDisabledInitialNavigation()),
        { provide: StaticSnapshotStore, useValue: { snapshot: of(snapshot) } },
      ],
    }),
  ],
};

export default meta;
type Story = StoryObj<StaticProjectSwitcher>;

export const Family: Story = {};
