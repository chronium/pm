import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PollingCoordinator } from '../core/polling-coordinator';
import type { ActivationSwitchboardResponse } from './activation-api.service';
import { ActivationSwitchboardStore } from './activation-switchboard.store';

const switchboard: ActivationSwitchboardResponse = {
  revision: 'r1',
  issues: [],
  milestones: [],
  activationTriggers: [
    {
      key: 'manual-entry',
      title: 'Manual entry',
      isActive: false,
      activation: null,
      satisfiedRequirementCount: 0,
      requirementCount: 0,
      requirementsSatisfied: false,
      isLatchedDespiteUnmetRequirements: false,
      requirements: [],
      consumingMilestones: ['beta'],
    },
  ],
};

describe('ActivationSwitchboardStore', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        ActivationSwitchboardStore,
        PollingCoordinator,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function load() {
    const store = TestBed.inject(ActivationSwitchboardStore);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/activation').flush(switchboard);
    await vi.waitFor(() => expect(store.switchboard()).toEqual(switchboard));
    return { store, http };
  }

  it('adopts lifecycle mutation responses without a redundant read', async () => {
    const { store, http } = await load();
    const result = store.activate('manual-entry');
    await Promise.resolve();
    const request = http.expectOne('/api/v1/activation/triggers/manual-entry/activate');
    expect(request.request.headers.get('If-Match')).toBe('"r1"');
    request.flush({
      changed: true,
      switchboard: {
        ...switchboard,
        revision: 'r2',
        activationTriggers: [
          {
            ...switchboard.activationTriggers[0]!,
            isActive: true,
            activation: {
              at: '2026-08-07T06:00:00Z',
              mode: 'manual',
              reason: null,
              waivedRequirements: [],
            },
          },
        ],
      },
    });
    expect(await result).toBe(true);
    expect(store.switchboard()?.revision).toBe('r2');
    expect(store.statusMessage()).toBe('Trigger activated.');
    http.expectNone('/api/v1/activation');
  });

  it('creates a definition and adopts the returned switchboard without a redundant read', async () => {
    const { store, http } = await load();
    const request = {
      key: 'architecture-ready',
      title: 'Architecture ready',
      requirements: [{ kind: 'milestone', source: 'current' }],
    };
    const result = store.create(request);
    await Promise.resolve();
    const create = http.expectOne('/api/v1/activation/triggers');
    expect(create.request.body).toEqual(request);
    expect(create.request.headers.get('If-Match')).toBe('"r1"');
    create.flush({
      changed: true,
      switchboard: {
        ...switchboard,
        revision: 'r2',
        activationTriggers: [
          ...switchboard.activationTriggers,
          {
            key: 'architecture-ready',
            title: 'Architecture ready',
            isActive: false,
            activation: null,
            satisfiedRequirementCount: 0,
            requirementCount: 1,
            requirementsSatisfied: false,
            isLatchedDespiteUnmetRequirements: false,
            requirements: [
              {
                kind: 'milestone',
                source: 'current',
                isSatisfied: false,
                wasWaivedAtActivation: false,
              },
            ],
            consumingMilestones: [],
          },
        ],
      },
    });
    expect(await result).toBe(true);
    expect(store.switchboard()?.activationTriggers.at(-1)?.key).toBe('architecture-ready');
    expect(store.statusMessage()).toBe('Activation trigger created.');
    http.expectNone('/api/v1/activation');
  });

  it('reviews a redefinition before applying the preview revision', async () => {
    const { store, http } = await load();
    const requirements = [{ kind: 'task', source: 'PM-0001' }];
    const previewResult = store.previewRedefinition('manual-entry', requirements);
    await Promise.resolve();
    const previewRequest = http.expectOne(
      '/api/v1/activation/triggers/manual-entry/redefinition-preview',
    );
    expect(previewRequest.request.body).toEqual({ requirements });
    previewRequest.flush({
      triggerKey: 'manual-entry',
      previewRevision: 'preview-r1',
      willReactivateAutomatically: false,
      requiresConfirmation: true,
      milestones: [],
      currentlyEligibleTaskIds: ['PM-0002'],
      taskIdsLosingEligibility: ['PM-0002'],
    });
    const preview = await previewResult;
    expect(preview?.requiresConfirmation).toBe(true);

    const applyResult = store.redefine('manual-entry', requirements, preview!);
    await Promise.resolve();
    const apply = http.expectOne('/api/v1/activation/triggers/manual-entry/redefinition');
    expect(apply.request.body).toEqual({
      requirements,
      previewRevision: 'preview-r1',
      allowDeactivation: true,
    });
    apply.flush({ changed: true, switchboard: { ...switchboard, revision: 'r2' } });
    expect(await applyResult).toBe(true);
  });

  it('refreshes latest state after a stale mutation while retaining the conflict', async () => {
    const { store, http } = await load();
    const result = store.activate('manual-entry');
    await Promise.resolve();
    http
      .expectOne('/api/v1/activation/triggers/manual-entry/activate')
      .flush(
        { title: 'Stale', detail: 'Activation state changed.', errorCode: 'precondition_failed' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await Promise.resolve();
    http.expectOne('/api/v1/activation').flush({ ...switchboard, revision: 'r9' });
    expect(await result).toBe(false);
    expect(store.failure()?.error.conflict).toBe(true);
    expect(store.switchboard()?.revision).toBe('r9');
    expect(store.statusMessage()).toContain('changed elsewhere');
  });

  it('dry-runs reconciliation before the confirmed mutation', async () => {
    const { store, http } = await load();
    const previewResult = store.previewReconciliation();
    await Promise.resolve();
    const previewRequest = http.expectOne('/api/v1/activation/reconcile');
    expect(previewRequest.request.body).toEqual({ dryRun: true });
    previewRequest.flush({
      changed: true,
      switchboard,
      impact: {
        affectedMilestones: ['beta'],
        taskIdsLosingEligibility: [],
        automaticallyActivatedTriggers: ['manual-entry'],
      },
    });
    expect(await previewResult).toEqual({
      revision: 'r1',
      triggerKeys: ['manual-entry'],
      milestoneKeys: ['beta'],
    });

    const result = store.reconcile();
    await Promise.resolve();
    const reconcile = http.expectOne('/api/v1/activation/reconcile');
    expect(reconcile.request.body).toEqual({ dryRun: false });
    reconcile.flush({ changed: true, switchboard: { ...switchboard, revision: 'r2' } });
    expect(await result).toBe(true);
  });
});
