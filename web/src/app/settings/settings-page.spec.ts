import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import type { SettingsResponse, ValidationResponse } from './settings-api.service';
import { SettingsPage } from './settings-page';

const settings: SettingsResponse = {
  projectName: 'Atlas',
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
    },
  ],
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
    },
  ],
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
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  it('renders dense read-first sections, structured health context, and long values', async () => {
    const { element } = await render();
    expect(
      [...element.querySelectorAll('section h2')].map((heading) => heading.textContent),
    ).toEqual(['Project health', 'Statuses', 'Milestones', 'Tracks']);
    expect(element.textContent).toContain('task_state_missing');
    expect(element.textContent).toContain('.pm/tasks/PM-0001.md');
    expect(element.textContent).toContain('A very long milestone title');
    expect(element.querySelectorAll('.settings-row input')).toHaveLength(0);
    expect(
      element.querySelector('button[aria-label="Edit milestone title"]')?.getAttribute('title'),
    ).toBe('Edit milestone title');
  });

  it('validates create forms, cancels without mutation, and creates a status with retained input on failure', async () => {
    const { fixture, element, http } = await render();
    (element.querySelector('.settings-section button') as HTMLButtonElement).click();
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
    expect(element.querySelector('dialog')?.textContent).toContain('todo');
    const confirm = element.querySelector(
      'pm-confirm-dialog .pm-button--danger',
    ) as HTMLButtonElement;
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

  it('locks stale mutations and clears editors only after Reload latest succeeds', async () => {
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
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.textContent).toContain('Reload latest');
    expect((element.querySelector('.settings-row input') as HTMLInputElement).value).toBe('Ready');
    (element.querySelector('.stale-banner button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(element.querySelector('.settings-row input')).toBeTruthy();
    http.expectOne('/api/v1/settings').flush({
      ...settings,
      statuses: [{ key: 'todo', name: 'Server value' }, settings.statuses[1]!],
      revision: 'r9',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('.settings-row input')).toBeNull();
    expect(element.textContent).toContain('Server value');
  });
});
