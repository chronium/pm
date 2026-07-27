import { HttpBackend, HttpRequest, HttpResponse, provideHttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';
import { of } from 'rxjs';

import { ProjectMembers } from './project-members';

const identity = {
  userId: 'usr_story_admin',
  displayName: 'Alex Morgan',
  publicKey: 'story-public-key',
  fingerprint: '3f'.repeat(32),
};

@Injectable()
class MembershipStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/project/identity')
      return of(new HttpResponse({ status: 200, body: identity }));
    if (request.url === '/api/v1/project/members')
      return of(
        new HttpResponse({
          status: 200,
          body: {
            projectId: 'prj_story',
            currentUserId: identity.userId,
            currentRole: 'admin',
            authenticated: true,
            members: [
              { ...identity, role: 'admin', isLocal: true },
              {
                userId: 'usr_linux',
                displayName: 'Linux workstation',
                publicKey: 'linux-public-key',
                fingerprint: '8c'.repeat(32),
                role: 'user',
                isLocal: false,
              },
            ],
          },
        }),
      );
    if (request.method === 'GET' && request.url === '/api/v1/project/invitations')
      return of(
        new HttpResponse({
          status: 200,
          body: {
            invitations: [
              {
                invitationId: 'pminv_pending',
                role: 'user',
                createdByUserId: identity.userId,
                createdAt: '2026-07-27T08:00:00Z',
                expiresAt: '2026-07-28T08:00:00Z',
              },
            ],
          },
        }),
      );
    if (request.method === 'POST' && request.url === '/api/v1/project/invitations')
      return of(
        new HttpResponse({
          status: 200,
          body: {
            invitation: {
              invitationId: 'pminv_new',
              role: 'user',
              createdByUserId: identity.userId,
              createdAt: '2026-07-27T08:00:00Z',
              expiresAt: '2026-07-28T08:00:00Z',
            },
            token: 'pmi_example_one_time_secret',
          },
        }),
      );
    return of(new HttpResponse({ status: 204 }));
  }
}

const meta = {
  title: 'Settings/Project members',
  component: ProjectMembers,
  decorators: [
    applicationConfig({
      providers: [provideHttpClient(), { provide: HttpBackend, useClass: MembershipStoryBackend }],
    }),
  ],
} satisfies Meta<ProjectMembers>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Members: Story = {};

export const InvitationResult: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Invite member' }));
    const dialog = await canvas.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create invitation' }));
    await expect(dialog).toHaveAttribute('open');
    await expect(within(dialog).getByText('pmi_example_one_time_secret')).toBeInTheDocument();
  },
};
