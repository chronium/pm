import { Component, input } from '@angular/core';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, waitFor, within } from 'storybook/test';

import type { BoardTask } from '../tasks/tasks-board.store';
import {
  OverviewComposition,
  type OverviewCompositionData,
  type OverviewSectionData,
} from './overview-composition';

type ArchetypeComposition = OverviewCompositionData;
type ArchetypeSection = OverviewSectionData;
type ArchetypeCopyrightSection = Extract<ArchetypeSection, { type: 'copyright' }>;
type ArchetypeContentSection = Exclude<ArchetypeSection, ArchetypeCopyrightSection>;

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

const page = (path: string, title: string, day: string) => ({
  path,
  title,
  modifiedAt: `2026-08-${day}T10:00:00Z`,
});

const softwareProductSections = [
  {
    type: 'hero',
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
  {
    type: 'copyright',
    notice: '© 2026 Northstar contributors. All rights reserved.',
  },
] satisfies readonly ArchetypeSection[];

const librarySections = [
  {
    type: 'hero',
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
  {
    type: 'copyright',
    notice: '© 2026 Quarry contributors. Released under the MIT License.',
  },
] satisfies readonly ArchetypeSection[];

const infrastructureSections = [
  {
    type: 'hero',
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
] satisfies readonly ArchetypeSection[];

const personalProjectSections = [
  {
    type: 'hero',
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
] satisfies readonly ArchetypeSection[];

const softwareProductComposition = {
  layout: 'single',
  sections: softwareProductSections,
} satisfies ArchetypeComposition;

const libraryComposition = {
  layout: 'single',
  sections: librarySections,
} satisfies ArchetypeComposition;

const infrastructureComposition = {
  layout: 'single',
  sections: infrastructureSections,
} satisfies ArchetypeComposition;

const personalProjectComposition = {
  layout: 'single',
  sections: personalProjectSections,
} satisfies ArchetypeComposition;

const softwareProductSplitComposition = {
  layout: 'split',
  primary: [softwareProductSections[0], softwareProductSections[1]],
  secondary: [softwareProductSections[2], softwareProductSections[3]],
  after: [softwareProductSections[4], softwareProductSections[5]],
} satisfies ArchetypeComposition;

const librarySplitComposition = {
  layout: 'split',
  primary: [librarySections[0], librarySections[1], librarySections[2]],
  secondary: [librarySections[3], librarySections[4]],
  after: [librarySections[5], librarySections[6]],
} satisfies ArchetypeComposition;

const infrastructureSplitComposition = {
  layout: 'split',
  primary: [infrastructureSections[0], infrastructureSections[3], infrastructureSections[4]],
  secondary: [infrastructureSections[1], infrastructureSections[2]],
  after: [],
} satisfies ArchetypeComposition;

const personalProjectSplitComposition = {
  layout: 'split',
  primary: [personalProjectSections[0], personalProjectSections[3]],
  secondary: [personalProjectSections[1], personalProjectSections[2]],
  after: [],
} satisfies ArchetypeComposition;

const longUnevenSplitComposition = {
  layout: 'split',
  primary: [
    softwareProductSections[0],
    {
      type: 'markdown',
      title: 'A longer project introduction',
      sourcePath: 'overview/long-form',
      body: `${softwareProductSections[1].body}\n\n## Why local ownership matters\n\n${'Repository-backed project state keeps decisions reviewable and portable while preserving a fast local workflow. '.repeat(8)}\n\n## What comes next\n\nThe public Overview should remain useful with long-form narrative content without forcing delivery information below an unreasonable wall of text.`,
    },
  ],
  secondary: [infrastructureSections[1], infrastructureSections[2]],
  after: [softwareProductSections[4], softwareProductSections[5]],
} satisfies ArchetypeComposition;

@Component({
  selector: 'pm-overview-archetype-story',
  imports: [OverviewComposition],
  template: `
    <div class="story-route" [style.width.px]="previewWidth()">
      <pm-overview-composition [composition]="composition()" [projectName]="projectName()" />
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-width: 320px;
      height: 100dvh;
    }

    .story-route {
      width: 100%;
      max-width: 100%;
      height: 100%;
      overflow: hidden;
      background: var(--pm-surface-canvas);
    }
  `,
})
class OverviewArchetypeStory {
  readonly composition = input.required<ArchetypeComposition>();
  readonly projectName = input.required<string>();
  readonly previewWidth = input<number | null>(null);
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
  args: {
    composition: softwareProductComposition,
    projectName: 'Northstar workspace',
    previewWidth: null,
  },
} satisfies Meta<OverviewArchetypeStory>;

export default meta;
type Story = StoryObj<typeof meta>;

const sectionsInReadingOrder = (composition: ArchetypeComposition): readonly ArchetypeSection[] =>
  composition.layout === 'single'
    ? composition.sections
    : [...composition.primary, ...composition.secondary, ...(composition.after ?? [])];

const expectedHeadings = (composition: ArchetypeComposition): string[] =>
  sectionsInReadingOrder(composition)
    .filter((section): section is ArchetypeContentSection => section.type !== 'copyright')
    .map((section) => section.title);

const copyrightSections = (
  composition: ArchetypeComposition,
): readonly ArchetypeCopyrightSection[] =>
  sectionsInReadingOrder(composition).filter(
    (section): section is ArchetypeCopyrightSection => section.type === 'copyright',
  );

const verifyComposition =
  (composition: ArchetypeComposition): NonNullable<Story['play']> =>
  async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const headings = Array.from(
      canvasElement.querySelectorAll<HTMLElement>('main h1, main h2[id^="overview-"]'),
    ).map((heading) => heading.textContent?.trim());
    expect(headings).toEqual(expectedHeadings(composition));
    await expect(canvas.getByRole('link', { name: 'View tasks' })).toBeVisible();
    await expect(canvas.getByRole('link', { name: 'Read documentation' })).toBeVisible();
    await expect(canvas.getAllByRole('heading', { level: 1 })).toHaveLength(1);

    const sections = sectionsInReadingOrder(composition);
    expect(sections[0]?.type).toBe('hero');
    expect(sections.filter((section) => section.type === 'hero')).toHaveLength(1);

    const copyrights = copyrightSections(composition);
    const footers = canvasElement.querySelectorAll('main footer.overview-copyright');
    expect(footers).toHaveLength(copyrights.length);
    if (composition.layout === 'single' && copyrights.length) {
      expect(composition.sections.at(-1)?.type).toBe('copyright');
      expect(
        canvasElement
          .querySelector('[data-region="single"]')
          ?.lastElementChild?.querySelector('footer'),
      ).toBe(footers.item(0));
    }
    if (composition.layout === 'split' && copyrights.length) {
      expect(composition.after?.at(-1)?.type).toBe('copyright');
      expect(canvasElement.querySelector('[data-region="primary"] footer')).toBeNull();
      expect(canvasElement.querySelector('[data-region="secondary"] footer')).toBeNull();
      expect(
        canvasElement
          .querySelector('[data-region="after"]')
          ?.lastElementChild?.querySelector('footer'),
      ).toBe(footers.item(footers.length - 1));
    }

    for (const taskRow of canvasElement.querySelectorAll<HTMLElement>('li[pmTaskRow]')) {
      expect(taskRow.scrollWidth).toBeLessThanOrEqual(taskRow.clientWidth);
    }
    expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  };

const verifyMobileComposition =
  (composition: ArchetypeComposition): NonNullable<Story['play']> =>
  async (context) => {
    await verifyComposition(composition)(context);
    const compositionElement =
      context.canvasElement.querySelector<HTMLElement>('.overview-composition');
    expect(getComputedStyle(compositionElement!).display).toBe('block');
    const finalHeading = expectedHeadings(composition).at(-1)!;
    await expect(
      within(context.canvasElement).getByRole('heading', { name: finalHeading }),
    ).toBeVisible();
  };

const verifySplitComposition =
  (composition: Extract<ArchetypeComposition, { layout: 'split' }>): NonNullable<Story['play']> =>
  async (context) => {
    await verifyComposition(composition)(context);
    const compositionElement =
      context.canvasElement.querySelector<HTMLElement>('.overview-composition')!;
    const primary = context.canvasElement.querySelector<HTMLElement>('[data-region="primary"]')!;
    const secondary = context.canvasElement.querySelector<HTMLElement>(
      '[data-region="secondary"]',
    )!;
    await waitFor(() => expect(getComputedStyle(compositionElement).display).toBe('grid'));
    const primarySections = Array.from(
      primary.querySelectorAll<HTMLElement>(':scope > pm-overview-composition-section'),
    );
    const secondarySections = Array.from(
      secondary.querySelectorAll<HTMLElement>(':scope > pm-overview-composition-section'),
    );
    const pairedSectionCount = Math.min(primarySections.length, secondarySections.length);
    for (let index = 0; index < pairedSectionCount; index += 1) {
      const primaryBounds = primarySections[index]!.getBoundingClientRect();
      const secondaryBounds = secondarySections[index]!.getBoundingClientRect();
      expect(Math.abs(primaryBounds.top - secondaryBounds.top)).toBeLessThan(1);
      expect(Math.abs(primaryBounds.height - secondaryBounds.height)).toBeLessThan(1);
    }
    if (composition.after?.length) {
      const after = context.canvasElement.querySelector<HTMLElement>('[data-region="after"]')!;
      expect(getComputedStyle(after).gridColumnStart).toBe('1');
      expect(getComputedStyle(after).gridColumnEnd).toBe('-1');
    }
    for (const contextElement of context.canvasElement.querySelectorAll<HTMLElement>(
      '[data-region="primary"] .task-context, [data-region="secondary"] .task-context',
    )) {
      const taskList = contextElement.closest<HTMLElement>('.overview-task-list')!;
      if (taskList.clientWidth <= 640) {
        expect(getComputedStyle(contextElement).display).toBe('none');
      } else {
        expect(getComputedStyle(contextElement).display).not.toBe('none');
      }
    }
  };

export const SoftwareProduct: Story = {
  args: { composition: softwareProductComposition },
  play: async (context) => {
    await verifyComposition(softwareProductComposition)(context);
    const canvas = within(context.canvasElement);
    canvas.getByRole<HTMLAnchorElement>('link', { name: 'View tasks' }).focus();
    await userEvent.tab();
    await expect(canvas.getByRole('link', { name: 'Read documentation' })).toHaveFocus();
    await userEvent.tab();
    await expect(canvas.getByRole('link', { name: 'overview' })).toHaveFocus();
    await expect(
      canvas.getByText('© 2026 Northstar contributors. All rights reserved.'),
    ).toBeVisible();
    (document.activeElement as HTMLElement | null)?.blur();
    context.canvasElement.querySelector('main')?.scrollTo({ top: 0 });
  },
};

export const Library: Story = {
  args: { composition: libraryComposition, projectName: 'Quarry contributors' },
  play: async (context) => {
    await verifyComposition(libraryComposition)(context);
    await expect(context.canvasElement.querySelectorAll('.overview-markdown-source')).toHaveLength(
      2,
    );
  },
};

export const Infrastructure: Story = {
  args: { composition: infrastructureComposition, projectName: 'Platform operations' },
  play: async (context) => {
    await verifyComposition(infrastructureComposition)(context);
    await expect(
      within(context.canvasElement).getByText('Waiting on: change-authorized'),
    ).toBeVisible();
  },
};

export const PersonalProjectImplicit: Story = {
  args: { composition: personalProjectComposition, projectName: 'Mara’s projects' },
  play: async (context) => {
    await verifyComposition(personalProjectComposition)(context);
    const canvas = within(context.canvasElement);
    await expect(canvas.getByText('No active milestone is available.')).toBeVisible();
    await expect(canvas.getByText('No documentation pages are available.')).toBeVisible();
    await expect(canvas.getByRole('link', { name: /Choose the first sensor board/ })).toBeVisible();
  },
};

export const SoftwareProductDark: Story = {
  args: { composition: softwareProductComposition },
  globals: { theme: 'dark' },
  play: verifyComposition(softwareProductComposition),
};

export const LibraryDark: Story = {
  args: { composition: libraryComposition, projectName: 'Quarry contributors' },
  globals: { theme: 'dark' },
  play: verifyComposition(libraryComposition),
};

export const InfrastructureDark: Story = {
  args: { composition: infrastructureComposition, projectName: 'Platform operations' },
  globals: { theme: 'dark' },
  play: verifyComposition(infrastructureComposition),
};

export const PersonalProjectDark: Story = {
  args: { composition: personalProjectComposition, projectName: 'Mara’s projects' },
  globals: { theme: 'dark' },
  play: verifyComposition(personalProjectComposition),
};

export const SoftwareProductMobile: Story = {
  args: { composition: softwareProductComposition, previewWidth: 390 },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(softwareProductComposition),
};

export const LibraryMobile: Story = {
  args: {
    composition: libraryComposition,
    projectName: 'Quarry contributors',
    previewWidth: 390,
  },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(libraryComposition),
};

export const InfrastructureMobile: Story = {
  args: {
    composition: infrastructureComposition,
    projectName: 'Platform operations',
    previewWidth: 390,
  },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(infrastructureComposition),
};

export const PersonalProjectMobile: Story = {
  args: {
    composition: personalProjectComposition,
    projectName: 'Mara’s projects',
    previewWidth: 390,
  },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(personalProjectComposition),
};

export const SoftwareProductSplit: Story = {
  args: { composition: softwareProductSplitComposition },
  globals: { viewport: 'desktop' },
  play: async (context) => {
    await verifySplitComposition(softwareProductSplitComposition)(context);
    const canvas = within(context.canvasElement);
    canvas.getByRole<HTMLAnchorElement>('link', { name: 'View tasks' }).focus();
    await userEvent.tab();
    await expect(canvas.getByRole('link', { name: 'Read documentation' })).toHaveFocus();
    await userEvent.tab();
    await expect(canvas.getByRole('link', { name: 'overview' })).toHaveFocus();
    await userEvent.tab();
    await expect(
      canvas.getByRole('link', { name: /Publish the first project Overview/ }),
    ).toHaveFocus();
    (document.activeElement as HTMLElement | null)?.blur();
    context.canvasElement.querySelector('main')?.scrollTo({ top: 0 });
  },
};

export const LibrarySplit: Story = {
  args: { composition: librarySplitComposition, projectName: 'Quarry contributors' },
  globals: { viewport: 'desktop' },
  play: verifySplitComposition(librarySplitComposition),
};

export const InfrastructureSplit: Story = {
  args: { composition: infrastructureSplitComposition, projectName: 'Platform operations' },
  globals: { viewport: 'desktop' },
  play: verifySplitComposition(infrastructureSplitComposition),
};

export const PersonalProjectSplit: Story = {
  args: { composition: personalProjectSplitComposition, projectName: 'Mara’s projects' },
  globals: { viewport: 'desktop' },
  play: verifySplitComposition(personalProjectSplitComposition),
};

export const SoftwareProductSplitDark: Story = {
  args: { composition: softwareProductSplitComposition },
  globals: { theme: 'dark', viewport: 'desktop' },
  play: verifySplitComposition(softwareProductSplitComposition),
};

export const LibrarySplitDark: Story = {
  args: { composition: librarySplitComposition, projectName: 'Quarry contributors' },
  globals: { theme: 'dark', viewport: 'desktop' },
  play: verifySplitComposition(librarySplitComposition),
};

export const InfrastructureSplitDark: Story = {
  args: { composition: infrastructureSplitComposition, projectName: 'Platform operations' },
  globals: { theme: 'dark', viewport: 'desktop' },
  play: verifySplitComposition(infrastructureSplitComposition),
};

export const PersonalProjectSplitDark: Story = {
  args: { composition: personalProjectSplitComposition, projectName: 'Mara’s projects' },
  globals: { theme: 'dark', viewport: 'desktop' },
  play: verifySplitComposition(personalProjectSplitComposition),
};

export const SoftwareProductSplitMobile: Story = {
  args: { composition: softwareProductSplitComposition, previewWidth: 390 },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(softwareProductSplitComposition),
};

export const LibrarySplitMobile: Story = {
  args: {
    composition: librarySplitComposition,
    projectName: 'Quarry contributors',
    previewWidth: 390,
  },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(librarySplitComposition),
};

export const InfrastructureSplitMobile: Story = {
  args: {
    composition: infrastructureSplitComposition,
    projectName: 'Platform operations',
    previewWidth: 390,
  },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(infrastructureSplitComposition),
};

export const PersonalProjectSplitMobile: Story = {
  args: {
    composition: personalProjectSplitComposition,
    projectName: 'Mara’s projects',
    previewWidth: 390,
  },
  globals: { viewport: 'mobile' },
  play: verifyMobileComposition(personalProjectSplitComposition),
};

export const LongUnevenSplit: Story = {
  args: { composition: longUnevenSplitComposition },
  globals: { viewport: 'desktop' },
  play: verifySplitComposition(longUnevenSplitComposition),
};
