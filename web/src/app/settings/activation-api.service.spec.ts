import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ActivationApiService } from './activation-api.service';

describe('ActivationApiService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('uses encoded routes and exact strong revisions for every lifecycle mutation', () => {
    const api = TestBed.inject(ActivationApiService);
    api.read().subscribe();
    api.activate('beta/entry', 'r1').subscribe();
    api.override('beta/entry', 'Accept the risk.', 'r1').subscribe();
    api.reset('beta/entry', 'r1').subscribe();
    api.previewRedefinition('beta/entry', [{ kind: 'task', source: 'PM-1' }], 'r1').subscribe();
    api.redefine('beta/entry', [], 'preview-r1', true, 'r1').subscribe();
    api.reconcile(true, 'r1').subscribe();
    api.previewMilestoneRequiredTriggers('m/one', ['beta entry'], 'r1').subscribe();
    api.setMilestoneRequiredTriggers('m/one', ['beta entry'], 'preview-r1', true, 'r1').subscribe();

    const http = TestBed.inject(HttpTestingController);
    const read = http.expectOne('/api/v1/activation');
    expect(read.request.headers.has('If-Match')).toBe(false);
    read.flush({ activationTriggers: [], milestones: [], issues: [], revision: 'r1' });

    const expected = [
      ['POST', '/api/v1/activation/triggers/beta%2Fentry/activate', {}],
      ['POST', '/api/v1/activation/triggers/beta%2Fentry/override', { reason: 'Accept the risk.' }],
      ['DELETE', '/api/v1/activation/triggers/beta%2Fentry/activation', null],
      [
        'POST',
        '/api/v1/activation/triggers/beta%2Fentry/redefinition-preview',
        { requirements: [{ kind: 'task', source: 'PM-1' }] },
      ],
      [
        'PUT',
        '/api/v1/activation/triggers/beta%2Fentry/redefinition',
        { requirements: [], previewRevision: 'preview-r1', allowDeactivation: true },
      ],
      ['POST', '/api/v1/activation/reconcile', { dryRun: true }],
      [
        'POST',
        '/api/v1/activation/milestones/m%2Fone/required-triggers-preview',
        { triggerKeys: ['beta entry'] },
      ],
      [
        'PUT',
        '/api/v1/activation/milestones/m%2Fone/required-triggers',
        { triggerKeys: ['beta entry'], previewRevision: 'preview-r1', allowDeactivation: true },
      ],
    ] as const;
    for (const [method, url, body] of expected) {
      const request = http.expectOne(url);
      expect(request.request.method).toBe(method);
      expect(request.request.body).toEqual(body);
      expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
      expect(request.request.headers.get('If-Match')).toBe('"r1"');
      request.flush({});
    }
  });

  it('maps structured failures and stale conflicts', () => {
    const api = TestBed.inject(ActivationApiService);
    expect(
      api.error(
        new HttpErrorResponse({
          status: 409,
          error: {
            title: 'Blocked',
            detail: 'Requirements remain satisfied.',
            errorCode: 'activation_trigger_reset_blocked',
          },
        }),
        'Fallback',
      ),
    ).toEqual({
      status: 409,
      message: 'Requirements remain satisfied.',
      conflict: false,
      code: 'activation_trigger_reset_blocked',
    });
    expect(
      api.error(
        new HttpErrorResponse({
          status: 412,
          error: { title: 'Stale', errorCode: 'precondition_failed' },
        }),
        'Fallback',
      ).conflict,
    ).toBe(true);
  });
});
