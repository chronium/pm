import { Component, input } from '@angular/core';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import { ProjectContextService } from '../core/project-context.service';
import { ProjectLinksService } from '../core/project-links.service';
import type { BoardTask } from '../tasks/tasks-board.store';
import { OverviewHero } from './overview-hero';
import { OverviewInvalidState, type OverviewIssue } from './overview-invalid-state';
import { OverviewMarkdown } from './overview-markdown';
import { OverviewMilestone, type OverviewMilestoneData } from './overview-milestone';
import { OverviewShell } from './overview-shell';
import { OverviewTasks } from './overview-tasks';
import { OverviewWiki, type OverviewWikiPage } from './overview-wiki';

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

const documentationPages = [
  {
    path: 'getting-started',
    title: 'Getting started',
    modifiedAt: '2026-08-09T08:00:00Z',
  },
  {
    path: 'architecture',
    title: 'Architecture',
    modifiedAt: '2026-08-08T08:00:00Z',
  },
  {
    path: 'publishing/static-site',
    title: 'Static publishing',
    modifiedAt: '2026-08-07T08:00:00Z',
  },
] as const satisfies readonly OverviewWikiPage[];

const introduction = `PM keeps project work, deliverables, and documentation together in a repository-friendly model.

### Start with the project story

The Overview is composed from resolved PM data. Projects choose the content and its order while PM preserves a consistent layout, navigation model, and accessible rendering.

- Review the current deliverable.
- Follow active work without recreating the full board.
- Continue into the project's documentation when more detail is useful.`;

const invalidContentIssues = [
  {
    code: 'missing_overview_wiki_page',
    message: 'Wiki page publishing/missing was not found.',
    path: 'site.home.sections[4].pages[1]',
  },
  {
    code: 'missing_overview_markdown_source',
    message: 'Markdown source wiki:introduction was not found.',
    path: 'site.home.sections[1].source',
  },
] as const satisfies readonly OverviewIssue[];

@Component({
  selector: 'pm-overview-sections-story',
  imports: [
    OverviewHero,
    OverviewInvalidState,
    OverviewMarkdown,
    OverviewMilestone,
    OverviewShell,
    OverviewTasks,
    OverviewWiki,
  ],
  template: `
    <div class="story-route">
      <pm-overview-shell>
        @if (invalidIssues(); as issues) {
          <pm-overview-invalid-state [issues]="issues" />
        } @else {
          <pm-overview-hero
            projectName="Project Model"
            title="PM"
            description="Local project management built for software projects and agents."
            tasksUrl="/tasks"
            wikiUrl="/wiki"
          />
          <pm-overview-markdown
            headingId="featured-introduction"
            [title]="markdownSectionTitle()"
            [sourcePath]="markdownSourcePath()"
            [body]="markdownBody()"
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
          <pm-overview-wiki
            headingId="featured-documentation"
            [title]="wikiSectionTitle()"
            [pages]="wikiPages()"
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
  `,
})
class OverviewSectionsStory {
  readonly markdownSectionTitle = input('Introduction');
  readonly markdownSourcePath = input('overview');
  readonly markdownBody = input(introduction);
  readonly milestoneSectionTitle = input('Current milestone');
  readonly milestone = input.required<OverviewMilestoneData | null>();
  readonly taskSectionTitle = input('Current work');
  readonly tasks = input<readonly BoardTask[]>([]);
  readonly wikiSectionTitle = input('Documentation');
  readonly wikiPages = input<readonly OverviewWikiPage[]>(documentationPages);
  readonly invalidIssues = input<readonly OverviewIssue[] | null>(null);
}

@Component({ template: '' })
class StoryRoute {}

const meta = {
  title: 'Overview/Resolved sections',
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
    markdownSectionTitle: 'Introduction',
    markdownSourcePath: 'overview',
    markdownBody: introduction,
    wikiSectionTitle: 'Documentation',
    wikiPages: documentationPages,
    invalidIssues: null,
  },
} satisfies Meta<OverviewSectionsStory>;

export default meta;
type Story = StoryObj<typeof meta>;

const verifyResolvedComposition: NonNullable<Story['play']> = async ({ canvasElement }) => {
  const canvas = within(canvasElement);
  await expect(canvas.getByRole('heading', { level: 3, name: 'Public beta' })).toBeVisible();
  await expect(canvas.getByRole('heading', { level: 2, name: 'Introduction' })).toBeVisible();
  await expect(
    canvas.getByRole('heading', { level: 3, name: 'Start with the project story' }),
  ).toBeVisible();
  await expect(canvas.getByText('9 of 14 tasks complete')).toBeVisible();
  expect(
    new URL(canvas.getByRole<HTMLAnchorElement>('link', { name: 'overview' }).href).pathname,
  ).toBe('/wiki/overview');
  const taskLinks = canvas
    .getAllByRole<HTMLAnchorElement>('link')
    .filter((link) => link.getAttribute('href')?.startsWith('/tasks/PM-'));
  expect(taskLinks.map((link) => link.getAttribute('href'))).toEqual([
    '/tasks/PM-0128',
    '/tasks/PM-0114',
    '/tasks/PM-0107',
  ]);
  const wikiLinks = canvas
    .getAllByRole<HTMLAnchorElement>('link')
    .filter((link) => link.querySelector('.wiki-page-title'));
  expect(wikiLinks.map((link) => new URL(link.href).pathname)).toEqual([
    '/wiki/getting-started',
    '/wiki/architecture',
    '/wiki/publishing/static-site',
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
    markdownBody: '',
    wikiPages: [],
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByText('No active milestone is available.')).toBeVisible();
    await expect(canvas.getByText('No tasks match this section.')).toBeVisible();
    await expect(canvas.getByText('This documentation page is empty.')).toBeVisible();
    await expect(canvas.getByText('No documentation pages are available.')).toBeVisible();
  },
};

export const MissingContentSources: Story = {
  args: {
    milestone: null,
    tasks: [],
    invalidIssues: invalidContentIssues,
  },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('alert')).toBeVisible();
    await expect(canvas.getByText('Wiki page publishing/missing was not found.')).toBeVisible();
    await expect(
      canvas.getByText('Markdown source wiki:introduction was not found.'),
    ).toBeVisible();
    expect(canvas.queryByText('Current milestone')).toBeNull();
    expect(canvas.queryByText("What's being worked on")).toBeNull();
    expect(canvas.queryByText('Documentation')).toBeNull();
  },
};

export const LongNarrativeAndPaths: Story = {
  args: {
    markdownBody: `${introduction}\n\n## A deliberately long narrative section\n\n${'This content checks readable measure and vertical rhythm across a substantial project introduction. '.repeat(10)}\n\n\`\`\`yaml\nsite:\n  enabled: true\n  home:\n    sections:\n      - type: markdown\n        source: wiki:overview\n\`\`\``,
    wikiPages: [
      ...documentationPages,
      {
        path: 'architecture/publishing/extremely-long-static-site-deployment-contract',
        title: 'A deliberately long documentation page title for deployment architecture',
        modifiedAt: '2026-08-06T08:00:00Z',
      },
    ],
  },
  play: async ({ canvasElement }) => {
    await expect(
      within(canvasElement).getByText('A deliberately long narrative section'),
    ).toBeVisible();
    expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  },
};

export const LinkedProjectDocumentation: Story = {
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: ProjectContextService,
          useValue: {
            wikiUrl: (path: string) => `/projects/prj_child/wiki/${path}`,
            tasksRoot: () => '/projects/prj_child/tasks',
          },
        },
      ],
    }),
  ],
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    expect(
      new URL(canvas.getByRole<HTMLAnchorElement>('link', { name: 'overview' }).href).pathname,
    ).toBe('/projects/prj_child/wiki/overview');
    expect(
      new URL(canvas.getByRole<HTMLAnchorElement>('link', { name: /Getting started/ }).href)
        .pathname,
    ).toBe('/projects/prj_child/wiki/getting-started');
  },
};

export const CanonicalProjectLink: Story = {
  args: {
    markdownBody:
      'Continue with the [linked architecture](pm://project/prj_child/wiki/architecture/overview).',
  },
  decorators: [
    applicationConfig({
      providers: [
        {
          provide: ProjectLinksService,
          useValue: {
            resolve: (href: string) =>
              href.startsWith('pm://')
                ? {
                    kind: 'available',
                    href: '/projects/prj_child/wiki/architecture/overview',
                    local: true,
                  }
                : { kind: 'not-project-link' },
          },
        },
      ],
    }),
  ],
  play: async ({ canvasElement }) => {
    expect(
      new URL(
        within(canvasElement).getByRole<HTMLAnchorElement>('link', {
          name: 'linked architecture',
        }).href,
      ).pathname,
    ).toBe('/projects/prj_child/wiki/architecture/overview');
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
