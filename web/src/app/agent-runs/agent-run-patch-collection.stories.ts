import {
  HttpBackend,
  HttpHeaders,
  HttpRequest,
  HttpResponse,
  provideHttpClient,
} from '@angular/common/http';
import { Injectable } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, within } from 'storybook/test';
import { of } from 'rxjs';

import { AgentRunPatchCollection } from './agent-run-patch-collection';

const readyPaths = [
  {
    path: 'PM/TaskService.cs',
    status: 'modified',
    insertions: 12,
    deletions: 4,
    binary: false,
  },
  {
    path: 'PM.Tests/TaskServiceTests.cs',
    status: 'modified',
    insertions: 28,
    deletions: 0,
    binary: false,
  },
];

const longReviewPaths = [
  ...readyPaths,
  ...Array.from({ length: 24 }, (_, index) => ({
    path: `web/src/app/agent-runs/fixtures/patch-review-${String(index + 1).padStart(2, '0')}.ts`,
    status: index % 3 === 0 ? 'added' : 'modified',
    insertions: index + 2,
    deletions: index % 4,
    binary: false,
  })),
];

@Injectable()
class PatchCollectionStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    const paths = request.url.includes('run-long-review') ? longReviewPaths : readyPaths;
    return of(
      new HttpResponse({
        status: 200,
        headers: new HttpHeaders({ ETag: '"patch-r1"' }),
        body: {
          ready: true,
          revision: 'patch-r1',
          artifactId: 'changes-patch',
          artifactSha256: 'ab'.repeat(32),
          baseCommit: '1234567890abcdef1234567890abcdef12345678',
          currentHead: '1234567890abcdef1234567890abcdef12345678',
          taskRevision: 'cd'.repeat(32),
          currentTaskRevision: 'cd'.repeat(32),
          checks: [
            { id: 'base', label: 'Exact base commit', status: 'passed', summary: 'Base matches.' },
            {
              id: 'paths',
              label: 'Patch safety',
              status: 'passed',
              summary: 'Paths stay inside the repository.',
            },
            {
              id: 'apply',
              label: 'Patch application',
              status: 'passed',
              summary: 'Git confirms the patch applies.',
            },
          ],
          warnings: ['Two non-overlapping local paths will be preserved.'],
          paths,
          statistics: {
            filesChanged: paths.length,
            insertions: paths.reduce((total, path) => total + path.insertions, 0),
            deletions: paths.reduce((total, path) => total + path.deletions, 0),
            binaryFiles: 0,
          },
        },
      }),
    );
  }
}

const meta = {
  title: 'Agent runs/Patch collection',
  component: AgentRunPatchCollection,
  args: { open: true, runId: 'run-01K123' },
  decorators: [
    applicationConfig({
      providers: [
        provideHttpClient(),
        { provide: HttpBackend, useClass: PatchCollectionStoryBackend },
      ],
    }),
  ],
  parameters: { layout: 'fullscreen' },
} satisfies Meta<AgentRunPatchCollection>;

export default meta;
type Story = StoryObj<typeof meta>;

async function verifyLongReview(canvasElement: HTMLElement): Promise<void> {
  const canvas = within(canvasElement);
  const finalPath = longReviewPaths.at(-1)!.path;
  await canvas.findByText(finalPath);

  const dialog = canvas.getByRole<HTMLDialogElement>('dialog', {
    name: 'Review patch collection',
  });
  const header = dialog.querySelector<HTMLElement>('.patch-header')!;
  const body = dialog.querySelector<HTMLElement>('.patch-body')!;
  const actions = dialog.querySelector<HTMLElement>('.patch-actions')!;
  const pathRows = [...dialog.querySelectorAll<HTMLElement>('.path-list li')];
  const before = {
    headerTop: header.getBoundingClientRect().top,
    actionsTop: actions.getBoundingClientRect().top,
  };

  expect(body.scrollHeight).toBeGreaterThan(body.clientHeight);
  expect(actions.getBoundingClientRect().bottom).toBeLessThanOrEqual(
    dialog.getBoundingClientRect().bottom + 1,
  );
  expect(body.getBoundingClientRect().bottom).toBeLessThanOrEqual(
    actions.getBoundingClientRect().top + 1,
  );
  const statusLefts = pathRows.map(
    (row) => row.querySelector<HTMLElement>('span')!.getBoundingClientRect().left,
  );
  const statisticsLefts = pathRows.map(
    (row) => row.querySelector<HTMLElement>('span:last-child')!.getBoundingClientRect().left,
  );
  expect(Math.max(...statusLefts) - Math.min(...statusLefts)).toBeLessThan(1);
  expect(Math.max(...statisticsLefts) - Math.min(...statisticsLefts)).toBeLessThan(1);
  body.scrollTop = body.scrollHeight;
  expect(body.scrollTop).toBeGreaterThan(0);
  expect(Math.abs(header.getBoundingClientRect().top - before.headerTop)).toBeLessThan(1);
  expect(Math.abs(actions.getBoundingClientRect().top - before.actionsTop)).toBeLessThan(1);
}

export const Ready: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await canvas.findByText('PM.Tests/TaskServiceTests.cs');
    const dialog = canvas.getByRole<HTMLDialogElement>('dialog', {
      name: 'Review patch collection',
    });
    expect(dialog.getBoundingClientRect().height).toBeLessThan(780);
  },
};
export const Mobile: Story = { globals: { viewport: 'mobile' } };
export const LongReview: Story = {
  args: { runId: 'run-long-review' },
  globals: { viewport: 'desktop' },
  play: async ({ canvasElement }) => verifyLongReview(canvasElement),
};
export const LongReviewMobile: Story = {
  args: { runId: 'run-long-review' },
  globals: { viewport: 'mobile' },
  play: async ({ canvasElement }) => verifyLongReview(canvasElement),
};
