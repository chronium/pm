import { Component, input } from '@angular/core';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import type { BoardTask } from '../tasks/tasks-board.store';
import { OverviewHero } from './overview-hero';
import { OverviewMarkdown } from './overview-markdown';
import { OverviewMilestone, type OverviewMilestoneData } from './overview-milestone';
import { OverviewShell } from './overview-shell';
import { OverviewTasks } from './overview-tasks';
import { OverviewWiki, type OverviewWikiPage } from './overview-wiki';

type ArchetypeSection =
  | {
      type: 'hero';
      projectName: string;
      title: string;
      description: string | null;
    }
  | {
      type: 'markdown';
      title: string;
      sourcePath: string;
      body: string;
    }
  | {
      type: 'milestone';
      title: string;
      milestone: OverviewMilestoneData | null;
    }
  | {
      type: 'tasks';
      title: string;
      tasks: readonly BoardTask[];
    }
  | {
      type: 'wiki';
      title: string;
      pages: readonly OverviewWikiPage[];
    };

const task = (
  id: string,
  title: string,
  state: string,
  track: string,
  milestone: string | null,
  overrides: Partial<BoardTask> = {},
): BoardTask => ({
  id,
  title,
  track,
  milestone,
  priority: 'medium',
  prioritySource: 'task',
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
    milestoneLifecycle: milestone ? 'active' : null,
    requiredActivationTriggers: [],
    unmetActivationTriggers: [],
    summary: milestone
      ? `Eligible: milestone ${milestone} is active.`
      : 'Eligible: unassigned task.',
  },
  descriptionPreview: 'A representative task selected for this project Overview.',
  modifiedAt: '2026-08-09T10:00:00Z',
  ...overrides,
});

const page = (path: string, title: string, day: string): OverviewWikiPage => ({
  path,
  title,
  modifiedAt: `2026-08-${day}T10:00:00Z`,
});

const softwareProductSections = [
  {
    type: 'hero',
    projectName: 'Northstar workspace',
    title: 'Northstar',
    description: 'A focused planning workspace for teams shipping complex software together.',
  },
  {
    type: 'markdown',
    title: 'Introduction',
    sourcePath: 'overview',
    body: `Northstar keeps delivery decisions close to the work while giving contributors a clear route from an idea to a releasable outcome.

### What the project is proving

The current prototype connects project planning, review, and publishing without requiring a hosted control plane.`,
  },
  {
    type: 'milestone',
    title: 'Current delivery',
    milestone: {
      key: 'public-beta',
      title: 'Public beta',
      description:
        'Deliver an installable beta with the complete local workflow and a useful published project site.',
      priority: 'urgent',
      lifecycle: 'active',
      assignedTaskCount: 14,
      doneTaskCount: 9,
      requiredActivationTriggers: ['beta-entry'],
      unmetActivationTriggers: [],
    },
  },
  {
    type: 'tasks',
    title: "What's being worked on",
    tasks: [
      task(
        'NORTH-0142',
        'Publish the first project Overview',
        'in-progress',
        'SITE',
        'public-beta',
        {
          priority: 'urgent',
        },
      ),
      task(
        'NORTH-0138',
        'Finish the signed desktop release flow',
        'review',
        'RELEASE',
        'public-beta',
        {
          dependencies: {
            ready: false,
            dependsOn: ['NORTH-0137'],
            waitingOn: ['NORTH-0137'],
            missing: [],
            summary: 'waiting on NORTH-0137',
          },
        },
      ),
      task('NORTH-0129', 'Explain linked project navigation', 'todo', 'DOCS', 'public-beta'),
      task(
        'NORTH-0122',
        'Reconcile activation after repository edits',
        'todo',
        'PM',
        'public-beta',
        {
          priority: 'high',
        },
      ),
    ],
  },
  {
    type: 'wiki',
    title: 'Documentation',
    pages: [
      page('getting-started', 'Getting started', '09'),
      page('architecture', 'Architecture', '08'),
      page('publishing/static-site', 'Static publishing', '07'),
    ],
  },
] as const satisfies readonly ArchetypeSection[];

const librarySections = [
  {
    type: 'hero',
    projectName: 'Quarry contributors',
    title: 'Quarry',
    description: 'A compact, typed parser toolkit for configuration-heavy applications.',
  },
  {
    type: 'markdown',
    title: 'Use Quarry',
    sourcePath: 'readme',
    body: `Quarry turns a small grammar into a predictable parser with typed diagnostics.

\`\`\`ts
const document = quarry.parse(source, projectGrammar);
\`\`\`

Start with the guide, then use the API reference when integrating custom nodes.`,
  },
  {
    type: 'wiki',
    title: 'Guides and reference',
    pages: [
      page('guides/first-parser', 'Build your first parser', '09'),
      page('reference/api', 'API reference', '09'),
      page('reference/diagnostics', 'Diagnostic catalogue', '06'),
      page('contributing', 'Contributing', '03'),
    ],
  },
  {
    type: 'milestone',
    title: 'Next release',
    milestone: {
      key: 'v2-1',
      title: 'Quarry 2.1',
      description:
        'Stabilize streaming input and ship migration guidance for existing grammar packages.',
      priority: 'high',
      lifecycle: 'ready_to_deliver',
      assignedTaskCount: 8,
      doneTaskCount: 8,
      requiredActivationTriggers: [],
      unmetActivationTriggers: [],
    },
  },
  {
    type: 'tasks',
    title: 'Release work',
    tasks: [
      task('PARSER-0081', 'Document streaming parser backpressure', 'review', 'DOCS', 'v2-1'),
      task('PARSER-0078', 'Verify grammar package compatibility', 'in-progress', 'CORE', 'v2-1', {
        priority: 'high',
      }),
      task('PARSER-0074', 'Publish the 2.1 migration examples', 'todo', 'SAMPLES', 'v2-1'),
    ],
  },
  {
    type: 'markdown',
    title: 'Compatibility promise',
    sourcePath: 'reference/compatibility',
    body: 'Quarry supports the current and previous two Node LTS releases. Minor releases preserve the documented grammar and diagnostic contracts.',
  },
] as const satisfies readonly ArchetypeSection[];

const infrastructureSections = [
  {
    type: 'hero',
    projectName: 'Platform operations',
    title: 'Harbor',
    description: 'Regional deployment and recovery automation for the shared application platform.',
  },
  {
    type: 'milestone',
    title: 'Current change window',
    milestone: {
      key: 'region-failover',
      title: 'Automated region failover',
      description:
        'Demonstrate a reversible production failover with audited decisions and no manual data repair.',
      priority: 'urgent',
      lifecycle: 'inactive',
      assignedTaskCount: 11,
      doneTaskCount: 6,
      requiredActivationTriggers: ['replication-proven', 'change-authorized'],
      unmetActivationTriggers: ['change-authorized'],
    },
  },
  {
    type: 'tasks',
    title: 'Operational work',
    tasks: [
      task(
        'OPS-0214',
        'Rehearse database promotion in staging',
        'in-progress',
        'DATA',
        'region-failover',
        {
          priority: 'urgent',
        },
      ),
      task(
        'OPS-0211',
        'Approve the production change window',
        'review',
        'CHANGE',
        'region-failover',
        {
          activation: {
            isEligible: false,
            milestoneLifecycle: 'inactive',
            requiredActivationTriggers: ['change-authorized'],
            unmetActivationTriggers: ['change-authorized'],
            summary: 'Ineligible: milestone region-failover is inactive.',
          },
        },
      ),
      task(
        'OPS-0208',
        'Measure cross-region replication lag',
        'in-progress',
        'OBS',
        'region-failover',
      ),
      task('OPS-0204', 'Verify customer traffic drain', 'todo', 'EDGE', 'region-failover'),
      task('OPS-0199', 'Publish operator rollback checklist', 'todo', 'DOCS', 'region-failover'),
      task('OPS-0196', 'Exercise alert routing during failover', 'todo', 'OBS', 'region-failover'),
    ],
  },
  {
    type: 'markdown',
    title: 'Operator notes',
    sourcePath: 'operations/current-window',
    body: `The failover remains gated until the change owner records authorization.

> Do not normalize an override into an automatic activation after the remaining requirements become satisfied.

The rollback checkpoint is fifteen minutes after traffic moves.`,
  },
  {
    type: 'wiki',
    title: 'Runbooks',
    pages: [
      page('runbooks/failover', 'Regional failover', '09'),
      page('runbooks/rollback', 'Rollback and recovery', '08'),
      page('operations/escalation', 'Escalation policy', '05'),
    ],
  },
] as const satisfies readonly ArchetypeSection[];

const personalProjectSections = [
  {
    type: 'hero',
    projectName: 'Mara’s projects',
    title: 'Pocket greenhouse',
    description: null,
  },
  {
    type: 'milestone',
    title: 'Current milestone',
    milestone: null,
  },
  {
    type: 'tasks',
    title: 'Current work',
    tasks: [
      task('GROW-0004', 'Choose the first sensor board', 'in-progress', 'GROW', null, {
        priority: 'low',
      }),
      task('GROW-0003', 'Sketch the enclosure around a small herb pot', 'todo', 'GROW', null, {
        priority: 'none',
      }),
    ],
  },
  {
    type: 'wiki',
    title: 'Documentation',
    pages: [],
  },
] as const satisfies readonly ArchetypeSection[];

@Component({
  selector: 'pm-overview-archetype-story',
  imports: [
    OverviewHero,
    OverviewMarkdown,
    OverviewMilestone,
    OverviewShell,
    OverviewTasks,
    OverviewWiki,
  ],
  template: `
    <div class="story-route">
      <pm-overview-shell>
        @for (section of sections(); track $index) {
          @switch (section.type) {
            @case ('hero') {
              <pm-overview-hero
                [projectName]="section.projectName"
                [title]="section.title"
                [description]="section.description"
                tasksUrl="/tasks"
                wikiUrl="/wiki"
              />
            }
            @case ('markdown') {
              <pm-overview-markdown
                [headingId]="headingId(section.type, $index)"
                [title]="section.title"
                [sourcePath]="section.sourcePath"
                [body]="section.body"
              />
            }
            @case ('milestone') {
              <pm-overview-milestone
                [headingId]="headingId(section.type, $index)"
                [title]="section.title"
                [milestone]="section.milestone"
              />
            }
            @case ('tasks') {
              <pm-overview-tasks
                [headingId]="headingId(section.type, $index)"
                [title]="section.title"
                [tasks]="section.tasks"
              />
            }
            @case ('wiki') {
              <pm-overview-wiki
                [headingId]="headingId(section.type, $index)"
                [title]="section.title"
                [pages]="section.pages"
              />
            }
          }
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
class OverviewArchetypeStory {
  readonly sections = input.required<readonly ArchetypeSection[]>();

  protected headingId(type: ArchetypeSection['type'], index: number): string {
    return `archetype-${index}-${type}`;
  }
}

@Component({ template: '' })
class StoryRoute {}

const meta = {
  title: 'Overview/Project archetypes',
  component: OverviewArchetypeStory,
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
  args: { sections: softwareProductSections },
} satisfies Meta<OverviewArchetypeStory>;

export default meta;
type Story = StoryObj<typeof meta>;

const expectedHeadings = (sections: readonly ArchetypeSection[]): string[] =>
  sections.map((section) => section.title);

const verifyComposition =
  (sections: readonly ArchetypeSection[]): NonNullable<Story['play']> =>
  async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const headings = Array.from(
      canvasElement.querySelectorAll<HTMLElement>('main h1, main h2'),
    ).map((heading) => heading.textContent?.trim());
    expect(headings).toEqual(expectedHeadings(sections));
    await expect(canvas.getByRole('link', { name: 'View tasks' })).toBeVisible();
    await expect(canvas.getByRole('link', { name: 'Read documentation' })).toBeVisible();
    expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  };

const verifyMobileComposition =
  (sections: readonly ArchetypeSection[]): NonNullable<Story['play']> =>
  async (context) => {
    await verifyComposition(sections)(context);
    const finalHeading = expectedHeadings(sections).at(-1)!;
    await expect(
      within(context.canvasElement).getByRole('heading', { name: finalHeading }),
    ).toBeVisible();
  };

export const SoftwareProduct: Story = {
  args: { sections: softwareProductSections },
  play: async (context) => {
    await verifyComposition(softwareProductSections)(context);
    const canvas = within(context.canvasElement);
    canvas.getByRole<HTMLAnchorElement>('link', { name: 'View tasks' }).focus();
    await userEvent.tab();
    await expect(canvas.getByRole('link', { name: 'Read documentation' })).toHaveFocus();
    await userEvent.tab();
    await expect(canvas.getByRole('link', { name: 'overview' })).toHaveFocus();
    (document.activeElement as HTMLElement | null)?.blur();
    context.canvasElement.querySelector('main')?.scrollTo({ top: 0 });
  },
};

export const Library: Story = {
  args: { sections: librarySections },
  play: async (context) => {
    await verifyComposition(librarySections)(context);
    await expect(context.canvasElement.querySelectorAll('.overview-markdown-source')).toHaveLength(
      2,
    );
  },
};

export const Infrastructure: Story = {
  args: { sections: infrastructureSections },
  play: async (context) => {
    await verifyComposition(infrastructureSections)(context);
    await expect(
      within(context.canvasElement).getByText('Waiting on: change-authorized'),
    ).toBeVisible();
  },
};

export const PersonalProjectImplicit: Story = {
  args: { sections: personalProjectSections },
  play: async (context) => {
    await verifyComposition(personalProjectSections)(context);
    const canvas = within(context.canvasElement);
    await expect(canvas.getByText('No active milestone is available.')).toBeVisible();
    await expect(canvas.getByText('No documentation pages are available.')).toBeVisible();
    await expect(canvas.getByRole('link', { name: /Choose the first sensor board/ })).toBeVisible();
  },
};

export const SoftwareProductDark: Story = {
  args: { sections: softwareProductSections },
  globals: { theme: 'dark' },
  play: verifyComposition(softwareProductSections),
};

export const LibraryDark: Story = {
  args: { sections: librarySections },
  globals: { theme: 'dark' },
  play: verifyComposition(librarySections),
};

export const InfrastructureDark: Story = {
  args: { sections: infrastructureSections },
  globals: { theme: 'dark' },
  play: verifyComposition(infrastructureSections),
};

export const PersonalProjectDark: Story = {
  args: { sections: personalProjectSections },
  globals: { theme: 'dark' },
  play: verifyComposition(personalProjectSections),
};

export const SoftwareProductMobile: Story = {
  args: { sections: softwareProductSections },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(softwareProductSections),
};

export const LibraryMobile: Story = {
  args: { sections: librarySections },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(librarySections),
};

export const InfrastructureMobile: Story = {
  args: { sections: infrastructureSections },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(infrastructureSections),
};

export const PersonalProjectMobile: Story = {
  args: { sections: personalProjectSections },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(personalProjectSections),
};
