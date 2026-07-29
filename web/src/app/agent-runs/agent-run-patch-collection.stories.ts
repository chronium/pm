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
import { of } from 'rxjs';

import { AgentRunPatchCollection } from './agent-run-patch-collection';

@Injectable()
class PatchCollectionStoryBackend extends HttpBackend {
  handle(_request: HttpRequest<unknown>) {
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
          paths: [
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
          ],
          statistics: { filesChanged: 2, insertions: 40, deletions: 4, binaryFiles: 0 },
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

export const Ready: Story = {};
export const Mobile: Story = { globals: { viewport: 'mobile' } };
