import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import type { SettingsResponse, ValidationResponse } from './settings-api.service';
import { SettingsStore } from './settings.store';
import { PollingCoordinator } from '../core/polling-coordinator';

const initial: SettingsResponse = {
  projectName: 'Atlas',
  accent: 'teal',
  statuses: [{ key: 'todo', name: 'To do' }],
  tracks: [{ key: 'PM', name: 'Product' }],
  milestones: [
    {
      key: 'm1',
      title: 'First',
      priority: 'none',
      description: '',
      requiredActivationTriggers: [],
    },
  ],
  activationTriggers: [],
  priorityOptions: ['none', 'high'],
  revision: 'r1',
};
const validation: ValidationResponse = { valid: true, issues: [] };

describe('SettingsStore', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        SettingsStore,
        PollingCoordinator,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }),
  );
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
    track.flush({
      ...initial,
      statuses: [{ key: 'todo', name: 'Ready' }],
      tracks: [{ key: 'PM', name: 'Planning' }],
      revision: 'r3',
    });
    expect(await second).toBe(true);
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    expect(store.settings()?.revision).toBe('r3');
    http.expectNone('/api/v1/settings');
  });

  it('updates the project accent through the revisioned mutation queue', async () => {
    const { store, http } = await load();

    const result = store.setAccent({ accent: 'purple' });
    await Promise.resolve();
    const request = http.expectOne('/api/v1/settings/accent');
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"r1"');
    request.flush({ ...initial, accent: 'purple', revision: 'r2' });

    expect(await result).toBe(true);
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    expect(store.settings()?.accent).toBe('purple');
  });

  it('updates a milestone description as an independent settings mutation', async () => {
    const { store, http } = await load();

    const result = store.setMilestoneDescription('m1', { description: 'Ship a usable beta.' });
    await Promise.resolve();
    const request = http.expectOne('/api/v1/settings/milestones/m1/description');
    expect(request.request.body).toEqual({ description: 'Ship a usable beta.' });
    expect(request.request.headers.get('If-Match')).toBe('"r1"');
    request.flush({
      ...initial,
      milestones: [{ ...initial.milestones[0]!, description: 'Ship a usable beta.' }],
      revision: 'r2',
    });

    expect(await result).toBe(true);
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    expect(store.settings()?.milestones[0]?.description).toBe('Ship a usable beta.');
  });

  it('previews activation impact, applies the reviewed revision, and refreshes settings', async () => {
    const { store, http } = await load();

    const previewResult = store.previewMilestoneRequiredTriggers('m1', ['beta-entry']);
    await Promise.resolve();
    http.expectOne('/api/v1/activation').flush({
      revision: 'activation-r1',
      activationTriggers: [],
      milestones: [],
      issues: [],
    });
    await Promise.resolve();
    const previewRequest = http.expectOne(
      '/api/v1/activation/milestones/m1/required-triggers-preview',
    );
    expect(previewRequest.request.headers.get('If-Match')).toBe('"activation-r1"');
    previewRequest.flush(
      {
        milestoneKey: 'm1',
        previewRevision: 'preview-r1',
        currentTriggerKeys: [],
        proposedTriggerKeys: ['beta-entry'],
        before: 'active',
        after: 'inactive',
        currentlyEligibleTaskIds: ['PM-0001'],
        taskIdsLosingEligibility: ['PM-0001'],
        requiresConfirmation: true,
      },
      { headers: { ETag: '"activation-r1"' } },
    );
    const preview = await previewResult;
    expect(preview?.impact.taskIdsLosingEligibility).toEqual(['PM-0001']);

    const applyResult = store.applyMilestoneRequiredTriggers('m1', ['beta-entry'], preview!);
    await Promise.resolve();
    const applyRequest = http.expectOne('/api/v1/activation/milestones/m1/required-triggers');
    expect(applyRequest.request.headers.get('If-Match')).toBe('"activation-r1"');
    expect(applyRequest.request.body).toEqual({
      triggerKeys: ['beta-entry'],
      previewRevision: 'preview-r1',
      allowDeactivation: true,
    });
    applyRequest.flush({ revision: 'activation-r2', trigger: null, milestone: null });
    await Promise.resolve();
    const refreshed = {
      ...initial,
      milestones: [{ ...initial.milestones[0]!, requiredActivationTriggers: ['beta-entry'] }],
      revision: 'r2',
    };
    http.expectOne('/api/v1/settings').flush(refreshed);
    expect(await applyResult).toBe(true);
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
    expect(store.settings()).toEqual(refreshed);
  });

  it('keeps a gate preview conflict local so the caller can require a fresh review', async () => {
    const { store, http } = await load();
    const preview = {
      activationRevision: 'activation-r1',
      impact: {
        milestoneKey: 'm1',
        previewRevision: 'preview-r1',
        currentTriggerKeys: [],
        proposedTriggerKeys: ['beta-entry'],
        before: 'active',
        after: 'inactive',
        currentlyEligibleTaskIds: ['PM-0001'],
        taskIdsLosingEligibility: ['PM-0001'],
        requiresConfirmation: true,
      },
    };

    const result = store.applyMilestoneRequiredTriggers('m1', ['beta-entry'], preview);
    await Promise.resolve();
    http.expectOne('/api/v1/activation/milestones/m1/required-triggers').flush(
      {
        title: 'Stale preview',
        detail: 'Review the current impact again.',
        errorCode: 'precondition_failed',
      },
      { status: 412, statusText: 'Precondition Failed' },
    );

    expect(await result).toBe(false);
    expect(store.operationError()?.error.conflict).toBe(true);
    expect(store.stale()).toBe(false);
    http.expectNone('/api/v1/settings');
  });

  it('keeps restriction errors on the triggering row and disables mutations after a stale response', async () => {
    const { store, http } = await load();
    const blocked = store.removeStatus('todo');
    await Promise.resolve();
    http
      .expectOne('/api/v1/settings/statuses/todo')
      .flush(
        { title: 'In use', detail: 'Status todo is used by tasks.', errorCode: 'status_in_use' },
        { status: 409, statusText: 'Conflict' },
      );
    expect(await blocked).toBe(false);
    expect(store.errorFor('status', 'todo')).toBe('Status todo is used by tasks.');

    const stale = store.renameStatus('todo', { name: 'Ready' });
    await Promise.resolve();
    http
      .expectOne('/api/v1/settings/statuses/todo')
      .flush(
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
    http
      .expectOne('/api/v1/validation')
      .flush({ title: 'Unavailable' }, { status: 503, statusText: 'Unavailable' });
    TestBed.flushEffects();
    await vi.waitFor(() => expect(store.settings()).toEqual(initial));
    expect(store.validationError()).toContain('Project health');
    store.reloadValidation();
    TestBed.flushEffects();
    http.expectOne('/api/v1/validation').flush(validation);
  });
});
