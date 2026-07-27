import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ProjectMembers } from './project-members';

const identity = {
  userId: 'usr_local',
  displayName: '<Local admin>',
  publicKey: 'public-key',
  fingerprint: 'ab'.repeat(32),
};
const adminMembership = {
  projectId: 'prj_test',
  currentUserId: identity.userId,
  currentRole: 'admin',
  authenticated: true,
  members: [
    { ...identity, role: 'admin', isLocal: true },
    {
      userId: 'usr_user',
      displayName: '<script>alert(1)</script>',
      publicKey: 'other-key',
      fingerprint: 'cd'.repeat(32),
      role: 'user',
      isLocal: false,
    },
  ],
};

describe('ProjectMembers', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [ProjectMembers],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents(),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  function start(membership = adminMembership) {
    const fixture = TestBed.createComponent(ProjectMembers);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/project/identity').flush(identity);
    http.expectOne('/api/v1/project/members').flush(membership);
    if (membership.currentRole === 'admin')
      http.expectOne('/api/v1/project/invitations').flush({ invitations: [] });
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  it('renders local identity, authentication health, members, and escaped display names', () => {
    const { element } = start();
    expect(element.textContent).toContain('<Local admin>');
    expect(element.textContent).toContain('<script>alert(1)</script>');
    expect(element.querySelector('script')).toBeNull();
    expect(element.textContent).toContain('Authenticated with the project service');
    expect(element.textContent).toContain('This device');
  });

  it('hides all membership administration from normal users', () => {
    const { element } = start({ ...adminMembership, currentRole: 'user' });
    expect(element.querySelector('button')?.textContent).toContain('Join with invitation');
    expect(element.textContent).not.toContain('Invite member');
    expect(element.textContent).not.toContain('Pending invitations');
    expect(element.querySelector('.member-actions')).toBeNull();
  });

  it('creates a user invitation by default and removes the secret after acknowledgement', async () => {
    const { fixture, element, http } = start();
    [...element.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('Invite member'))!
      .click();
    fixture.detectChanges();
    const dialog = element.querySelector('.membership-dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(true);
    (dialog.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/v1/project/invitations');
    expect(request.request.body).toEqual({ role: 'user' });
    expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
    request.flush({
      invitation: {
        invitationId: 'pminv_1',
        role: 'user',
        createdByUserId: identity.userId,
        createdAt: '2026-07-27T00:00:00Z',
        expiresAt: '2026-07-28T00:00:00Z',
      },
      token: 'pmi_one-time-secret',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(dialog.textContent).toContain('pmi_one-time-secret');
    [...dialog.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('I have saved it'))!
      .click();
    fixture.detectChanges();
    expect(element.textContent).not.toContain('pmi_one-time-secret');
  });

  it('shows an offline authentication state while retaining local identity', () => {
    const fixture = TestBed.createComponent(ProjectMembers);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/project/identity').flush(identity);
    http.expectOne('/api/v1/project/members').error(new ProgressEvent('offline'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('membership service is offline');
    expect(fixture.nativeElement.textContent).toContain(identity.userId);
  });
});
