import { HttpBackend, HttpRequest, HttpResponse, provideHttpClient } from '@angular/common/http';
import { Component, Injectable } from '@angular/core';
import { provideRouter } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';
import { of } from 'rxjs';

import { PollingCoordinator } from '../core/polling-coordinator';
import type { ActivationSwitchboardResponse } from './activation-api.service';
import { ActivationSwitchboard } from './activation-switchboard';

@Component({ template: '' })
class ActivationStoryRoute {}

const switchboard: ActivationSwitchboardResponse = {
  revision: 'story-r1',
  issues: [
    {
      severity: 'warning',
      code: 'activation_reconciliation_required',
      message: 'architecture-ready has satisfied requirements but no activation record.',
    },
  ],
  milestones: [
    {
      key: 'public-beta',
      title: 'Public beta',
      description: 'Ship the complete local workflow to beta users.',
      priority: 'high',
      lifecycle: 'inactive',
      assignedTaskCount: 8,
      doneTaskCount: 3,
      requiredActivationTriggers: ['beta-entry'],
      unmetActivationTriggers: ['beta-entry'],
      delivery: null,
    },
  ],
  activationTriggers: [
    {
      key: 'beta-entry',
      title: 'Beta entry criteria',
      isActive: false,
      activation: null,
      satisfiedRequirementCount: 2,
      requirementCount: 3,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: false,
      requirements: [
        {
          kind: 'task',
          source: 'FOUNDATION-0001',
          isSatisfied: true,
          wasWaivedAtActivation: false,
        },
        {
          kind: 'task',
          source: 'FOUNDATION-0002',
          isSatisfied: true,
          wasWaivedAtActivation: false,
        },
        {
          kind: 'milestone',
          source: 'architecture-approved',
          isSatisfied: false,
          wasWaivedAtActivation: false,
        },
      ],
      consumingMilestones: ['public-beta'],
    },
    {
      key: 'launch-authorized',
      title: 'Launch authorized',
      isActive: true,
      activation: {
        at: '2026-08-06T19:15:00Z',
        mode: 'manual',
        reason: null,
        waivedRequirements: [],
      },
      satisfiedRequirementCount: 0,
      requirementCount: 0,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: false,
      requirements: [],
      consumingMilestones: ['launch'],
    },
    {
      key: 'foundation-ready',
      title: 'Foundation ready',
      isActive: true,
      activation: {
        at: '2026-08-05T10:00:00Z',
        mode: 'automatic',
        reason: null,
        waivedRequirements: [],
      },
      satisfiedRequirementCount: 2,
      requirementCount: 3,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: true,
      requirements: [
        {
          kind: 'task',
          source: 'FOUNDATION-0001',
          isSatisfied: true,
          wasWaivedAtActivation: false,
        },
        {
          kind: 'task',
          source: 'FOUNDATION-0002',
          isSatisfied: true,
          wasWaivedAtActivation: false,
        },
        {
          kind: 'task',
          source: 'FOUNDATION-0003',
          isSatisfied: false,
          wasWaivedAtActivation: false,
        },
      ],
      consumingMilestones: ['public-beta', 'import-preview'],
    },
  ],
};

@Injectable()
class ActivationStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/activation' && request.method === 'GET')
      return of(new HttpResponse({ status: 200, body: switchboard }));
    return of(
      new HttpResponse({
        status: 200,
        body: { changed: false, switchboard, impact: null },
      }),
    );
  }
}

const meta = {
  title: 'Settings/Activation switchboard',
  component: ActivationSwitchboard,
  decorators: [
    applicationConfig({
      providers: [
        PollingCoordinator,
        provideHttpClient(),
        provideRouter([{ path: '**', component: ActivationStoryRoute }]),
        { provide: HttpBackend, useClass: ActivationStoryBackend },
      ],
    }),
  ],
  args: { readOnly: false },
  parameters: { layout: 'padded' },
} satisfies Meta<ActivationSwitchboard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const MixedStates: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(await canvas.findByText('Pending — 2 / 3')).toBeVisible();
    await userEvent.click(canvas.getByText('Beta entry criteria'));
    await expect(canvas.getByRole('button', { name: 'Override…' })).toBeVisible();
    await expect(canvas.getByText('architecture-approved')).toBeVisible();
  },
};

export const ReadOnly: Story = {
  args: { readOnly: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(await canvas.findByText(/Controls are hidden/)).toBeVisible();
    await expect(canvas.queryByRole('button', { name: 'Override…' })).not.toBeInTheDocument();
  },
};

export const CreateManualOnly: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Add trigger' }));
    const dialog = canvas.getByRole('dialog', { name: 'Create trigger' });
    await expect(within(dialog).getByText(/Manual-only trigger/)).toBeInTheDocument();
    const inputs = within(dialog).getAllByRole('textbox');
    await userEvent.type(inputs[0]!, 'launch-authorized');
    await userEvent.type(inputs[1]!, 'Launch authorized');
    await expect(
      within(dialog).getByRole('button', { name: 'Create manual-only trigger' }),
    ).toBeEnabled();
  },
};

export const DefinitionActions: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const title = await canvas.findByText('Beta entry criteria');
    await userEvent.click(title.closest('summary')!);
    const row = within(title.closest('details')!);
    await expect(row.getByRole('button', { name: 'Rename…' })).toBeVisible();
    await expect(row.getByRole('button', { name: 'Edit requirements…' })).toBeVisible();
    await expect(row.getByRole('button', { name: 'Remove…' })).toBeDisabled();
    await expect(row.getByText(/Detach from public-beta before removing/)).toBeVisible();
    await userEvent.click(row.getByRole('button', { name: 'Rename…' }));
    const dialog = canvas.getByRole('dialog', { name: 'Rename trigger' });
    await expect(within(dialog).getByText('beta-entry')).toBeInTheDocument();
    await expect(within(dialog).getByRole('button', { name: 'Save title' })).toBeDisabled();
  },
};

export const Mobile: Story = {
  globals: { viewport: 'mobile' },
  play: MixedStates.play,
};
