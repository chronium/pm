import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import type { SettingsResponse, ValidationResponse } from './settings-api.service';
import { SettingsPage } from './settings-page';

const settings: SettingsResponse = {
  projectName: 'Atlas',
  accent: 'teal',
  statuses: [
    { key: 'todo', name: 'To do' },
    { key: 'done', name: 'Done' },
  ],
  tracks: [{ key: 'PM', name: 'Product' }],
  milestones: [
    {
      key: 'long/milestone',
      title: 'A very long milestone title that wraps without obscuring its controls',
      priority: 'high',
      description: '',
      requiredActivationTriggers: [],
    },
  ],
  activationTriggers: [],
  priorityOptions: ['none', 'medium', 'high'],
  revision: 'r1',
};
const validation: ValidationResponse = {
  valid: false,
  issues: [
    {
      severity: 'error',
      code: 'task_state_missing',
      message: 'A long validation message that remains fully available.',
      path: '.pm/tasks/PM-0001.md',
      taskId: 'PM-0001',
      wikiPath: null,
      state: 'missing',
      projectId: null,
      projectAlias: null,
    },
  ],
};
const identity = {
  userId: 'usr_local',
  displayName: 'Chronium',
  publicKey: 'public-key',
  fingerprint: 'ab'.repeat(32),
};
const membership = {
  projectId: 'project-1',
  currentUserId: identity.userId,
  currentRole: 'admin',
  authenticated: true,
  members: [{ ...identity, role: 'admin', isLocal: true }],
};

describe('SettingsPage', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [SettingsPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents(),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render() {
    const fixture = TestBed.createComponent(SettingsPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/settings').flush(settings);
    http.expectOne('/api/v1/validation').flush(validation);
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne('/api/v1/project/identity').flush(identity);
    http.expectOne('/api/v1/project/members').flush(membership);
    http.expectOne('/api/v1/project/invitations').flush({ invitations: [] });
    http.expectOne('/api/v1/runners').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  it('renders dense read-first sections, structured health context, and long values', async () => {
    const { element } = await render();
    expect(
      [...element.querySelectorAll('section h2')].map((heading) => heading.textContent),
    ).toEqual([
      'Project health',
      'Appearance',
      'Project members',
      'Agent runners',
      'Statuses',
      'Milestones',
      'Tracks',
    ]);
    expect(element.textContent).toContain('task_state_missing');
    expect(element.textContent).toContain('.pm/tasks/PM-0001.md');
    expect(element.textContent).toContain('A very long milestone title');
    expect(element.querySelector('.settings-navigation')?.textContent).toContain('Linked projects');
    expect(element.querySelectorAll('.settings-row input')).toHaveLength(0);
    expect(
      element.querySelector('button[aria-label="Edit milestone title"]')?.getAttribute('title'),
    ).toBe('Edit milestone title');
  });

  it('saves a project-wide accent from General settings', async () => {
    document.documentElement.dataset['accent'] = 'teal';
    const { fixture, element, http } = await render();
    const purple = [...element.querySelectorAll('pm-accent-picker button')].find(
      (button) => button.textContent?.trim() === 'Purple',
    ) as HTMLButtonElement;

    purple.click();
    fixture.detectChanges();
    await Promise.resolve();
    const request = http.expectOne('/api/v1/settings/accent');
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"r1"');
    request.flush({ ...settings, accent: 'purple', revision: 'r2' });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne('/api/v1/validation').flush(validation);

    expect(document.documentElement.dataset['accent']).toBe('purple');
    expect(purple.getAttribute('aria-pressed')).toBe('true');
  });

  it('validates create forms, cancels without mutation, and creates a status with retained input on failure', async () => {
    const { fixture, element, http } = await render();
    (
      element.querySelector(
        '.settings-section[aria-labelledby="statuses-heading"] button',
      ) as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    const form = element.querySelector('form.create-form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(element.textContent).toContain('Key is required.');
    const inputs = form.querySelectorAll('input');
    inputs[0]!.value = 'blocked';
    inputs[0]!.dispatchEvent(new Event('input'));
    inputs[1]!.value = 'Blocked';
    inputs[1]!.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));
    await Promise.resolve();
    fixture.detectChanges();
    http.expectOne('/api/v1/settings/statuses').flush(
      {
        title: 'Duplicate',
        detail: 'Status blocked already exists.',
        errorCode: 'duplicate_status',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect((element.querySelector('#status-key') as HTMLInputElement).value).toBe('blocked');
    expect(element.textContent).toContain('Status blocked already exists.');
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((form.querySelector('button[type="button"]') as HTMLButtonElement).disabled).toBe(
        false,
      );
    });
    (form.querySelector('button[type="button"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(element.querySelector('form.create-form')).toBeNull();
  });

  it('saves milestone title and priority as separate atomic mutations', async () => {
    const { fixture, element, http } = await render();
    (
      element.querySelector('button[aria-label="Edit milestone title"]') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    const title = element.querySelector('.milestone-row input') as HTMLInputElement;
    title.value = 'Launch';
    title.dispatchEvent(new Event('input'));
    (title.closest('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await Promise.resolve();
    const titleRequest = http.expectOne('/api/v1/settings/milestones/long%2Fmilestone');
    expect(titleRequest.request.body).toEqual({ title: 'Launch' });
    titleRequest.flush({
      ...settings,
      milestones: [{ ...settings.milestones[0]!, title: 'Launch' }],
      revision: 'r2',
    });
    await Promise.resolve();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    await fixture.whenStable();
    fixture.detectChanges();

    (element.querySelector('.priority-action') as HTMLButtonElement).click();
    fixture.detectChanges();
    const select = element.querySelector('.priority-form select') as HTMLSelectElement;
    select.value = 'medium';
    select.dispatchEvent(new Event('input'));
    (select.closest('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await Promise.resolve();
    const priority = http.expectOne('/api/v1/settings/milestones/long%2Fmilestone/priority');
    expect(priority.request.body).toEqual({ priority: 'medium' });
    expect(priority.request.headers.get('If-Match')).toBe('"r2"');
    priority.flush({
      ...settings,
      milestones: [{ ...settings.milestones[0]!, title: 'Launch', priority: 'medium' }],
      revision: 'r3',
    });
    await Promise.resolve();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
  });

  it('confirms removal, shows row restrictions, and disables actions during a pending mutation', async () => {
    const { fixture, element, http } = await render();
    (element.querySelector('button[aria-label="Remove status"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const removalDialog = [...element.querySelectorAll('pm-confirm-dialog')].find((dialog) =>
      dialog.textContent?.includes('todo'),
    )!;
    expect(removalDialog.textContent).toContain('todo');
    const confirm = removalDialog.querySelector('.pm-button--danger') as HTMLButtonElement;
    confirm.click();
    await Promise.resolve();
    fixture.detectChanges();
    expect(
      [...element.querySelectorAll<HTMLButtonElement>('.settings-row button')].every(
        (button) => button.disabled,
      ),
    ).toBe(true);
    http
      .expectOne('/api/v1/settings/statuses/todo')
      .flush(
        { title: 'In use', detail: 'Status todo is used by PM-0001.', errorCode: 'status_in_use' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('.settings-row .row-error')?.textContent).toContain(
      'used by PM-0001',
    );
  });

  it('fetches latest after 412 and supports review then draft restoration', async () => {
    const { fixture, element, http } = await render();
    (element.querySelector('button[aria-label="Edit status name"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const input = element.querySelector('.settings-row input') as HTMLInputElement;
    input.value = 'Ready';
    input.dispatchEvent(new Event('input'));
    (input.closest('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await Promise.resolve();
    http.expectOne('/api/v1/settings/statuses/todo').flush(
      {
        title: 'Stale',
        detail: 'Project settings changed in another client.',
        errorCode: 'precondition_failed',
      },
      { status: 412, statusText: 'Precondition Failed' },
    );
    await Promise.resolve();
    await Promise.resolve();
    TestBed.tick();
    http.expectOne('/api/v1/settings').flush({
      ...settings,
      statuses: [{ key: 'todo', name: 'Server value' }, settings.statuses[1]!],
      revision: 'r9',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect((element.querySelector('.settings-row input') as HTMLInputElement).value).toBe('Ready');
    expect(element.textContent).toContain('Review latest');
    (
      element.querySelector('pm-external-change-banner .pm-button--primary') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    expect(element.querySelector('.settings-row input')).toBeNull();
    expect(element.textContent).toContain('Server value');
    (
      element.querySelector('pm-external-change-banner .pm-button--secondary') as HTMLButtonElement
    ).click();
    fixture.detectChanges();
    expect((element.querySelector('.settings-row input') as HTMLInputElement).value).toBe('Ready');
  });
});
