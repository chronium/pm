import { HttpBackend, HttpRequest, HttpResponse, provideHttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';
import { of } from 'rxjs';

import { PollingCoordinator } from '../core/polling-coordinator';
import { MilestoneDeliverableEditor } from './milestone-deliverable-editor';
import type { SettingsResponse } from './settings-api.service';
import { SettingsStore } from './settings.store';

const milestone = {
  key: 'public-beta',
  title: 'Public beta',
  priority: 'high',
  description: `## Outcome

Deliver an installable beta covering the complete local workflow.

## Exclusions

Hosted collaboration remains outside this deliverable.`,
  requiredActivationTriggers: [],
};

const triggers = [
  {
    key: 'beta-entry',
    title: 'Beta entry criteria',
    requirements: [
      { kind: 'task', source: 'FOUNDATION-0001' },
      { kind: 'task', source: 'FOUNDATION-0002' },
    ],
  },
  { key: 'launch-authorized', title: 'Launch authorized', requirements: [] },
];

const settings: SettingsResponse = {
  projectName: 'Atlas workspace',
  accent: 'teal',
  statuses: [],
  tracks: [],
  milestones: [milestone],
  activationTriggers: triggers,
  priorityOptions: ['none', 'low', 'medium', 'high', 'urgent'],
  revision: 'settings-r1',
};

@Injectable()
class DeliverableEditorStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/settings')
      return of(new HttpResponse({ status: 200, body: settings }));
    if (request.url === '/api/v1/validation')
      return of(new HttpResponse({ status: 200, body: { valid: true, issues: [] } }));
    if (request.url === '/api/v1/activation')
      return of(
        new HttpResponse({
          status: 200,
          headers: request.headers.set('ETag', '"activation-r1"'),
          body: { revision: 'activation-r1', activationTriggers: [], milestones: [], issues: [] },
        }),
      );
    if (request.url.endsWith('/required-triggers-preview'))
      return of(
        new HttpResponse({
          status: 200,
          headers: request.headers.set('ETag', '"activation-r1"'),
          body: {
            milestoneKey: milestone.key,
            previewRevision: 'preview-r1',
            currentTriggerKeys: [],
            proposedTriggerKeys: ['beta-entry'],
            before: 'active',
            after: 'inactive',
            currentlyEligibleTaskIds: ['PM-0092', 'PM-0093'],
            taskIdsLosingEligibility: ['PM-0092', 'PM-0093'],
            requiresConfirmation: true,
          },
        }),
      );
    return of(new HttpResponse({ status: 200, body: settings }));
  }
}

const meta = {
  title: 'Settings/Milestone deliverable editor',
  component: MilestoneDeliverableEditor,
  decorators: [
    applicationConfig({
      providers: [
        SettingsStore,
        PollingCoordinator,
        provideHttpClient(),
        { provide: HttpBackend, useClass: DeliverableEditorStoryBackend },
      ],
    }),
  ],
  args: {
    open: true,
    milestone,
    activationTriggers: triggers,
    priorityOptions: settings.priorityOptions,
    readOnly: false,
  },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<MilestoneDeliverableEditor>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const EmptyDescription: Story = {
  args: { milestone: { ...milestone, description: '' } },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const guidance = await canvas.findByLabelText('Deliverable description suggestions');
    await expect(guidance).toHaveTextContent('Outcome:');
    await expect(guidance).toHaveTextContent('Evidence:');
  },
};

export const EligibilityImpact: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByLabelText(/Beta entry criteria/));
    await userEvent.click(canvas.getByRole('button', { name: 'Review changes' }));
    await expect(await canvas.findByText(/PM-0092, PM-0093/)).toHaveTextContent('PM-0092, PM-0093');
    await expect(canvas.getByRole('button', { name: 'Apply' })).toBeEnabled();
  },
};

export const ReadOnly: Story = {
  args: { readOnly: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(await canvas.findByRole('status')).toHaveTextContent('read-only');
    await expect(canvas.getByRole('button', { name: 'Edit milestone title' })).toBeDisabled();
    await expect(
      canvas.queryByRole('button', { name: 'Edit deliverable description' }),
    ).not.toBeInTheDocument();
  },
};

export const Mobile: Story = { globals: { viewport: 'mobile' } };
