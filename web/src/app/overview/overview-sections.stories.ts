import { Component, input } from '@angular/core';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import type { BoardTask } from '../tasks/tasks-board.store';
import { OverviewHero } from './overview-hero';
import { OverviewMilestone, type OverviewMilestoneData } from './overview-milestone';
import { OverviewShell } from './overview-shell';
import { OverviewTasks } from './overview-tasks';

const task = (
  id: string,
  title: string,
  state: string,
  overrides: Partial<BoardTask> = {},
): BoardTask => ({
  id,
  title,
  track: 'PM',
  milestone: 'public-beta',
  priority: 'high',
  prioritySource: 'milestone',
  state,
  dependencies: {
    ready: true,
    dependsOn: [],
    waitingOn: [],
    missing: [],
    summary: 'ready',
  },
  activation: {
    isEligible: true,
    milestoneLifecycle: 'active',
    requiredActivationTriggers: ['beta-entry'],
    unmetActivationTriggers: [],
    summary: 'Eligible: milestone public-beta is active.',
  },
  descriptionPreview: 'A resolved task selected for the configured Overview section.',
  modifiedAt: '2026-08-09T08:00:00Z',
  ...overrides,
});

const currentTasks = [
  task('PM-0128', 'Publish static project Overview pages', 'in-progress', { priority: 'urgent' }),
  task('PM-0114', 'Support agent execution from task views', 'review', {
    dependencies: {
      ready: false,
      dependsOn: ['PM-0113'],
      waitingOn: ['PM-0113'],
      missing: [],
      summary: 'waiting on PM-0113',
    },
  }),
  task('PM-0107', 'Resolve linked-project presentation data', 'todo', { priority: 'medium' }),
] as const;

const activeMilestone: OverviewMilestoneData = {
  key: 'public-beta',
  title: 'Public beta',
  description:
    'Deliver an installable beta covering the complete local workflow, static publishing, and linked-project navigation.',
  priority: 'urgent',
  lifecycle: 'active',
  assignedTaskCount: 14,
  doneTaskCount: 9,
  requiredActivationTriggers: ['beta-entry'],
  unmetActivationTriggers: [],
};

const readyMilestone: OverviewMilestoneData = {
  ...activeMilestone,
  key: 'first-release',
  title: 'First release',
  lifecycle: 'ready_to_deliver',
  assignedTaskCount: 12,
  doneTaskCount: 12,
};

const inactiveMilestone: OverviewMilestoneData = {
  ...activeMilestone,
  key: 'public-launch',
  title: 'Public launch',
  lifecycle: 'inactive',
  assignedTaskCount: 8,
  doneTaskCount: 2,
  requiredActivationTriggers: ['release-stable', 'launch-authorized'],
  unmetActivationTriggers: ['release-stable', 'launch-authorized'],
};

const deliveredMilestone: OverviewMilestoneData = {
  ...activeMilestone,
  key: 'private-preview',
  title: 'Private preview',
  lifecycle: 'delivered',
  assignedTaskCount: 6,
  doneTaskCount: 6,
};

@Component({
  selector: 'pm-overview-sections-story',
  imports: [OverviewHero, OverviewMilestone, OverviewShell, OverviewTasks],
  template: `
    <div class="story-route">
      <pm-overview-shell>
        @if (invalidIssue(); as issue) {
          <section
            class="overview-diagnostic"
            role="alert"
            aria-labelledby="overview-diagnostic-title"
          >
            <p class="diagnostic-context">Overview configuration</p>
            <h1 id="overview-diagnostic-title">This Overview needs attention</h1>
            <p>{{ issue }}</p>
            <code>site.home.sections[1].milestone</code>
          </section>
        } @else {
          <pm-overview-hero
            projectName="Project Model"
            title="PM"
            description="Local project management built for software projects and agents."
            tasksUrl="/tasks"
            wikiUrl="/wiki"
          />
          <pm-overview-milestone
            headingId="featured-milestone"
            [title]="milestoneSectionTitle()"
            [milestone]="milestone()"
          />
          <pm-overview-tasks
            headingId="featured-tasks"
            [title]="taskSectionTitle()"
            [tasks]="tasks()"
          />
        }
      </pm-overview-shell>
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-width: 320px;
      height: 100dvh;
    }

    .story-route {
      height: 100%;
      overflow: hidden;
      background: var(--pm-surface-canvas);
    }

    .overview-diagnostic {
      max-width: 680px;
      padding: var(--pm-space-6) 0;
    }

    .overview-diagnostic h1,
    .overview-diagnostic p {
      margin: 0;
    }

    .diagnostic-context {
      margin-bottom: var(--pm-space-2) !important;
      color: var(--pm-danger);
      font-size: var(--pm-font-size-xs);
      font-weight: 700;
      letter-spacing: 0.04em;
      text-transform: uppercase;
    }

    .overview-diagnostic h1 {
      color: var(--pm-text-primary);
      font-size: clamp(1.5rem, 4vw, 2rem);
    }

    .overview-diagnostic h1 + p {
      margin-top: var(--pm-space-3);
      color: var(--pm-text-muted);
    }

    .overview-diagnostic code {
      display: inline-block;
      margin-top: var(--pm-space-3);
      color: var(--pm-text-subtle);
      font-family: var(--pm-font-family-mono);
      font-size: var(--pm-font-size-xs);
    }
  `,
})
class OverviewSectionsStory {
  readonly milestoneSectionTitle = input('Current milestone');
  readonly milestone = input.required<OverviewMilestoneData | null>();
  readonly taskSectionTitle = input('Current work');
  readonly tasks = input<readonly BoardTask[]>([]);
  readonly invalidIssue = input<string | null>(null);
}

@Component({ template: '' })
class StoryRoute {}

const meta = {
  title: 'Overview/Featured milestone and tasks',
  component: OverviewSectionsStory,
  decorators: [
    applicationConfig({
      providers: [
        provideRouter(
          [
            { path: 'tasks', component: StoryRoute },
            { path: 'tasks/dialog/:id', component: StoryRoute },
            { path: 'tasks/:id', component: StoryRoute },
            { path: 'wiki', component: StoryRoute },
          ],
          withDisabledInitialNavigation(),
        ),
      ],
    }),
  ],
  parameters: { layout: 'fullscreen' },
  args: {
    milestoneSectionTitle: 'Current milestone',
    milestone: activeMilestone,
    taskSectionTitle: "What's being worked on",
    tasks: currentTasks,
    invalidIssue: null,
  },
} satisfies Meta<OverviewSectionsStory>;

export default meta;
type Story = StoryObj<typeof meta>;

const verifyResolvedComposition: NonNullable<Story['play']> = async ({ canvasElement }) => {
  const canvas = within(canvasElement);
  await expect(canvas.getByRole('heading', { level: 3, name: 'Public beta' })).toBeVisible();
  await expect(canvas.getByText('9 of 14 tasks complete')).toBeVisible();
  const taskLinks = canvas
    .getAllByRole<HTMLAnchorElement>('link')
    .filter((link) => link.getAttribute('href')?.startsWith('/tasks/PM-'));
  expect(taskLinks.map((link) => link.getAttribute('href'))).toEqual([
    '/tasks/PM-0128',
    '/tasks/PM-0114',
    '/tasks/PM-0107',
  ]);
  const sectionHeading = canvas.getByRole('heading', { level: 2, name: "What's being worked on" });
  const taskRows = taskLinks.map((link) => link.closest('li[pmTaskRow]') as HTMLElement);
  const taskIdentities = taskRows.map((row) => row.querySelector<HTMLElement>('.task-identity')!);
  const taskContexts = taskRows.map((row) => row.querySelector<HTMLElement>('.task-context')!);
  expect(
    taskIdentities.every(
      (identity) =>
        identity.getBoundingClientRect().left === sectionHeading.getBoundingClientRect().left,
    ),
  ).toBe(true);
  expect(
    taskContexts.every(
      (context) =>
        context.getBoundingClientRect().left === taskContexts[0]!.getBoundingClientRect().left,
    ),
  ).toBe(true);
  expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
};

export const AutomaticActiveMilestone: Story = { play: verifyResolvedComposition };

export const ExplicitReadyToDeliver: Story = {
  args: {
    milestoneSectionTitle: 'Release candidate',
    milestone: readyMilestone,
  },
};

export const ExplicitInactive: Story = {
  args: {
    milestoneSectionTitle: 'Upcoming delivery',
    milestone: inactiveMilestone,
  },
};

export const ExplicitDelivered: Story = {
  args: {
    milestoneSectionTitle: 'Latest delivery',
    milestone: deliveredMilestone,
  },
};

export const EmptySections: Story = {
  args: {
    milestone: null,
    tasks: [],
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByText('No active milestone is available.')).toBeVisible();
    await expect(canvas.getByText('No tasks match this section.')).toBeVisible();
  },
};

export const InvalidMilestoneReference: Story = {
  args: {
    milestone: null,
    tasks: [],
    invalidIssue: 'Milestone public-beta was not found.',
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('alert')).toBeVisible();
    expect(canvas.queryByText('Current milestone')).toBeNull();
    expect(canvas.queryByText("What's being worked on")).toBeNull();
  },
};

export const DarkMode: Story = {
  globals: { theme: 'dark' },
  play: verifyResolvedComposition,
};

export const Mobile: Story = {
  globals: { viewport: 'mobile' },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const taskLinks = canvas
      .getAllByRole<HTMLAnchorElement>('link')
      .filter((link) => link.getAttribute('href')?.startsWith('/tasks/PM-'));
    await expect(taskLinks[0]).toBeVisible();
    expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  },
};

export const KeyboardTaskNavigation: Story = {
  play: async ({ canvasElement }) => {
    const taskLinks = within(canvasElement)
      .getAllByRole<HTMLAnchorElement>('link')
      .filter((link) => link.getAttribute('href')?.startsWith('/tasks/PM-'));
    taskLinks[0]!.focus();
    await expect(taskLinks[0]).toHaveFocus();
    await userEvent.tab();
    await expect(taskLinks[1]).toHaveFocus();
  },
};
