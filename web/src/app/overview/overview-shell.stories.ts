import { Component, input } from '@angular/core';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import { OverviewHero } from './overview-hero';
import { OverviewShell } from './overview-shell';

@Component({
  selector: 'pm-overview-story-frame',
  imports: [OverviewHero, OverviewShell],
  template: `
    <div class="story-app">
      <header class="story-topbar">
        <strong>{{ projectName() }}</strong>
        <nav aria-label="Workspace preview">
          <a href="/overview" aria-current="page">Overview</a>
          <a href="/tasks">Tasks</a>
          <a href="/wiki">Wiki</a>
        </nav>
      </header>
      <div class="story-route">
        <pm-overview-shell>
          <pm-overview-hero
            [projectName]="projectName()"
            [title]="title()"
            [description]="description()"
            tasksUrl="/tasks"
            wikiUrl="/wiki"
          />
        </pm-overview-shell>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-width: 320px;
      height: 100dvh;
      color: var(--pm-text-primary);
    }

    .story-app {
      display: grid;
      grid-template-rows: var(--pm-topbar-height) minmax(0, 1fr);
      height: 100%;
      background: var(--pm-surface-canvas);
    }

    .story-topbar {
      display: flex;
      align-items: center;
      gap: var(--pm-space-5);
      min-width: 0;
      padding: 0 var(--pm-space-3);
      background: var(--pm-satin-color);
    }

    .story-topbar strong {
      max-width: var(--pm-sidebar-width);
      overflow: hidden;
      color: var(--pm-text-primary);
      font-size: var(--pm-font-size-md);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .story-topbar nav {
      display: flex;
      align-self: stretch;
    }

    .story-topbar a {
      display: flex;
      align-items: center;
      min-width: 64px;
      padding: 0 var(--pm-space-3);
      border-bottom: 2px solid transparent;
      color: var(--pm-text-muted);
      font-size: var(--pm-font-size-sm);
      font-weight: 550;
      justify-content: center;
      text-decoration: none;
    }

    .story-topbar a:hover {
      background: var(--pm-surface-hover);
      color: var(--pm-text-primary);
    }

    .story-topbar a[aria-current='page'] {
      border-bottom-color: var(--pm-accent);
      color: var(--pm-text-primary);
    }

    .story-route {
      min-width: 0;
      min-height: 0;
      overflow: hidden;
      background: var(--pm-surface-canvas);
    }

    @media (max-width: 520px) {
      .story-topbar {
        gap: var(--pm-space-1);
        padding-inline: var(--pm-space-2);
      }

      .story-topbar strong {
        display: none;
      }

      .story-topbar nav {
        width: 100%;
      }

      .story-topbar a {
        flex: 1;
        min-width: 0;
        padding-inline: var(--pm-space-2);
      }
    }
  `,
})
class OverviewStoryFrame {
  readonly projectName = input.required<string>();
  readonly title = input.required<string>();
  readonly description = input<string | null>(null);
}

@Component({ template: '' })
class StoryRoute {}

const configured = {
  projectName: 'Project Model',
  title: 'PM',
  description: 'Local project management built for software projects and agents.',
};

const meta = {
  title: 'Overview/Shell and hero',
  component: OverviewStoryFrame,
  decorators: [
    applicationConfig({
      providers: [
        provideRouter(
          [
            { path: 'tasks', component: StoryRoute },
            { path: 'wiki', component: StoryRoute },
          ],
          withDisabledInitialNavigation(),
        ),
      ],
    }),
  ],
  parameters: { layout: 'fullscreen' },
  args: configured,
} satisfies Meta<OverviewStoryFrame>;

export default meta;
type Story = StoryObj<typeof meta>;

const verifyConfigured: NonNullable<Story['play']> = async ({ canvasElement }) => {
  const canvas = within(canvasElement);
  const tasks = canvas.getByRole<HTMLAnchorElement>('link', { name: 'View tasks' });
  const wiki = canvas.getByRole<HTMLAnchorElement>('link', { name: 'Read documentation' });
  await expect(canvas.getByRole('heading', { level: 1, name: 'PM' })).toBeVisible();
  await expect(
    canvas.getByText('Project Model', { selector: '.overview-project-context' }),
  ).toBeVisible();
  expect(new URL(tasks.href).pathname).toBe('/tasks');
  expect(new URL(wiki.href).pathname).toBe('/wiki');
  expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
};

export const Configured: Story = { play: verifyConfigured };

export const FallbackIdentity: Story = {
  args: {
    projectName: 'Project Model',
    title: 'Project Model',
    description: null,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', { level: 1, name: 'Project Model' })).toBeVisible();
    expect(canvasElement.querySelector('.overview-project-context')).toBeNull();
    expect(canvasElement.querySelector('.overview-description')).toBeNull();
  },
};

export const LongContent: Story = {
  args: {
    projectName: 'Distributed Build and Deployment Infrastructure',
    title: 'Portable release automation for complex linked software projects',
    description:
      'A deliberately long project description that explains the outcome clearly while testing readable line length, balanced wrapping, and a dense repository-style presentation.',
  },
  play: async ({ canvasElement }) => {
    await expect(within(canvasElement).getByRole('heading', { level: 1 })).toBeVisible();
    expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  },
};

export const DarkMode: Story = {
  globals: { theme: 'dark' },
  play: verifyConfigured,
};

export const Mobile: Story = {
  globals: { viewport: 'mobile' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('link', { name: 'View tasks' })).toBeVisible();
    await expect(canvas.getByRole('link', { name: 'Read documentation' })).toBeVisible();
    expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  },
};

export const KeyboardFocus: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const destinations = within(canvas.getByRole('navigation', { name: 'Project destinations' }));
    const tasks = destinations.getByRole('link', { name: 'View tasks' });
    const wiki = destinations.getByRole('link', { name: 'Read documentation' });

    tasks.focus();
    await expect(tasks).toHaveFocus();
    await userEvent.tab();
    await expect(wiki).toHaveFocus();
  },
};
