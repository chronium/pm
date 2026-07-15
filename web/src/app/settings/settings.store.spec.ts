import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import type { SettingsResponse, ValidationResponse } from './settings-api.service';
import { SettingsStore } from './settings.store';

const initial: SettingsResponse = { projectName: 'Atlas', statuses: [{ key: 'todo', name: 'To do' }], tracks: [{ key: 'PM', name: 'Product' }], milestones: [{ key: 'm1', title: 'First', priority: 'none' }], priorityOptions: ['none', 'high'], revision: 'r1' };
const validation: ValidationResponse = { valid: true, issues: [] };

describe('SettingsStore', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [SettingsStore, provideHttpClient(), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function load() {
    const store = TestBed.inject(SettingsStore);
    TestBed.flushEffects();
    expect(store.loading()).toBe(true);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/settings').flush(initial);
    http.expectOne('/api/v1/validation').flush(validation);
    TestBed.flushEffects();
    await vi.waitFor(() => expect(store.settings()).toEqual(initial));
    return { store, http };
  }

  it('loads both resources and retains settings while a refresh is pending', async () => {
    const { store, http } = await load();
    expect(store.settings()).toEqual(initial);
    store.reloadLatest();
    TestBed.flushEffects();
    expect(store.settings()).toEqual(initial);
    expect(store.refreshing()).toBe(true);
    http.expectOne('/api/v1/settings').flush({ ...initial, revision: 'r2' });
    TestBed.flushEffects();
    await vi.waitFor(() => expect(store.settings()?.revision).toBe('r2'));
  });

  it('serializes writes, adopts aggregate responses, and refreshes validation without a settings GET', async () => {
    const { store, http } = await load();
    const first = store.renameStatus('todo', { name: 'Ready' });
    const second = store.renameTrack('PM', { name: 'Planning' });
    await Promise.resolve();
    const status = http.expectOne('/api/v1/settings/statuses/todo');
    expect(status.request.headers.get('If-Match')).toBe('"r1"');
    status.flush({ ...initial, statuses: [{ key: 'todo', name: 'Ready' }], revision: 'r2' });
    await first;
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    await Promise.resolve();
    const track = http.expectOne('/api/v1/settings/tracks/PM');
    expect(track.request.headers.get('If-Match')).toBe('"r2"');
    track.flush({ ...initial, statuses: [{ key: 'todo', name: 'Ready' }], tracks: [{ key: 'PM', name: 'Planning' }], revision: 'r3' });
    expect(await second).toBe(true);
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    expect(store.settings()?.revision).toBe('r3');
    http.expectNone('/api/v1/settings');
  });

  it('keeps restriction errors on the triggering row and disables mutations after a stale response', async () => {
    const { store, http } = await load();
    const blocked = store.removeStatus('todo');
    await Promise.resolve();
    http.expectOne('/api/v1/settings/statuses/todo').flush(
      { title: 'In use', detail: 'Status todo is used by tasks.', errorCode: 'status_in_use' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(await blocked).toBe(false);
    expect(store.errorFor('status', 'todo')).toBe('Status todo is used by tasks.');

    const stale = store.renameStatus('todo', { name: 'Ready' });
    await Promise.resolve();
    http.expectOne('/api/v1/settings/statuses/todo').flush(
      { title: 'Stale', detail: 'Project settings changed.', errorCode: 'precondition_failed' },
      { status: 412, statusText: 'Precondition Failed' },
    );
    expect(await stale).toBe(false);
    expect(store.stale()).toBe(true);
    expect(await store.removeTrack('PM')).toBe(false);
    http.expectNone('/api/v1/settings/tracks/PM');

    store.reloadLatest();
    TestBed.flushEffects();
    http.expectOne('/api/v1/settings').flush({ ...initial, revision: 'r4' });
    TestBed.flushEffects();
    await vi.waitFor(() => expect(store.stale()).toBe(false));
    expect(store.operationError()).toBeNull();
    expect(store.reloadGeneration()).toBe(1);
  });

  it('isolates validation failures and supports a local retry', async () => {
    const store = TestBed.inject(SettingsStore);
    TestBed.flushEffects();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/settings').flush(initial);
    http.expectOne('/api/v1/validation').flush({ title: 'Unavailable' }, { status: 503, statusText: 'Unavailable' });
    TestBed.flushEffects();
    await vi.waitFor(() => expect(store.settings()).toEqual(initial));
    expect(store.validationError()).toContain('Project health');
    store.reloadValidation();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
  });
});
