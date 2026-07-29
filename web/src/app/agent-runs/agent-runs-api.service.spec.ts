import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AgentRunsApiService } from './agent-runs-api.service';

describe('AgentRunsApiService', () => {
  let api: AgentRunsApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(AgentRunsApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('uses encoded runner URLs and the Angular mutation client header', () => {
    api.runnerStatus('runner/one').subscribe();
    const status = http.expectOne('/api/v1/runners/runner%2Fone/status');
    expect(status.request.method).toBe('GET');
    status.flush({});

    api
      .pairRunner({
        endpoint: 'https://runner.example:7443',
        runnerId: 'runner-one',
        tlsFingerprint: 'sha256:abc',
        pairingCode: 'secret',
        replaceExisting: false,
      })
      .subscribe();
    const pair = http.expectOne('/api/v1/runners/pair');
    expect(pair.request.headers.get('X-PM-Client')).toBe('angular-web');
    expect(pair.request.body.pairingCode).toBe('secret');
    pair.flush({});

    api.rotateRunner('runner one').subscribe();
    const rotate = http.expectOne('/api/v1/runners/runner%20one/rotate');
    expect(rotate.request.headers.get('X-PM-Client')).toBe('angular-web');
    rotate.flush({});

    api.revokeRunner('runner#one').subscribe();
    const revoke = http.expectOne('/api/v1/runners/runner%23one');
    expect(revoke.request.headers.get('X-PM-Client')).toBe('angular-web');
    revoke.flush(null);
  });

  it('captures preflight ETags and sends the exact value through If-Match', () => {
    api
      .preflight({
        taskId: 'PM-0001',
        runnerId: 'runner-one',
        profileId: 'pm-development',
        providerId: 'codex',
        modelId: 'gpt-5',
        effortId: 'medium',
      })
      .subscribe((response) => {
        expect(api.etag(response)).toBe('"draft-r1"');
      });
    const preflight = http.expectOne('/api/v1/runs/preflight');
    expect(preflight.request.headers.get('X-PM-Client')).toBe('angular-web');
    preflight.flush(
      { ready: false, runId: null, revision: null, request: null, checks: [] },
      {
        headers: { ETag: '"draft-r1"' },
      },
    );

    api.start('run/one', '"draft-r1"').subscribe();
    const start = http.expectOne('/api/v1/runs/run%2Fone/start');
    expect(start.request.headers.get('If-Match')).toBe('"draft-r1"');
    expect(start.request.headers.get('X-PM-Client')).toBe('angular-web');
    expect(start.request.body).toEqual({});
    start.flush({});
  });

  it('maps problem details and stale preconditions without exposing response internals', () => {
    const failure = api.error(
      new HttpErrorResponse({
        status: 412,
        error: { errorCode: 'preflight_stale', detail: 'The draft changed.' },
      }),
      'Fallback',
    );
    expect(failure).toEqual({
      status: 412,
      code: 'preflight_stale',
      message: 'The draft changed.',
      stale: true,
    });
  });
});
