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

    api.preflightPatchCollection('run/one').subscribe((response) => {
      expect(api.etag(response)).toBe('"patch-r1"');
    });
    const patchPreflight = http.expectOne('/api/v1/runs/run%2Fone/patch-collection/preflight');
    expect(patchPreflight.request.headers.get('X-PM-Client')).toBe('angular-web');
    patchPreflight.flush({}, { headers: { ETag: '"patch-r1"' } });

    api.collectPatch('run/one', 'abc123', '"patch-r1"').subscribe();
    const collection = http.expectOne('/api/v1/runs/run%2Fone/patch-collection/apply');
    expect(collection.request.headers.get('If-Match')).toBe('"patch-r1"');
    expect(collection.request.headers.get('X-PM-Client')).toBe('angular-web');
    expect(collection.request.body).toEqual({ artifactSha256: 'abc123' });
    collection.flush({});
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

  it('uses encoded run URLs for inspection, replay, cancellation, and artifacts', () => {
    api.inspect('run/one').subscribe();
    http.expectOne('/api/v1/runs/run%2Fone').flush({});

    api.events('run/one', 42, 250).subscribe();
    const events = http.expectOne((request) => request.url.includes('/events'));
    expect(events.request.params.get('afterSequence')).toBe('42');
    expect(events.request.params.get('limit')).toBe('250');
    events.flush({ events: [], nextAfterSequence: 42, hasMore: false, terminal: false });

    api.cancel('run/one').subscribe();
    const cancel = http.expectOne('/api/v1/runs/run%2Fone/cancel');
    expect(cancel.request.headers.get('X-PM-Client')).toBe('angular-web');
    cancel.flush({});

    api.artifacts('run/one').subscribe();
    http.expectOne('/api/v1/runs/run%2Fone/artifacts').flush([]);

    api.artifactContent('run/one', 'changes/patch').subscribe();
    const content = http.expectOne('/api/v1/runs/run%2Fone/artifacts/changes%2Fpatch/content');
    expect(content.request.method).toBe('GET');
    expect(content.request.responseType).toBe('arraybuffer');
    content.flush(new ArrayBuffer(0));
  });

  it('downloads the complete event journal through paginated replay', async () => {
    const journal = api.eventJournal('run-one');
    const first = http.expectOne((request) => request.url.includes('/events'));
    expect(first.request.params.get('afterSequence')).toBe('0');
    first.flush({
      events: [
        {
          protocolVersion: '1.0',
          runId: 'run-one',
          sequence: 1,
          timestamp: '2026-07-29T08:00:00Z',
          type: 'command.output',
          state: 'running',
          summary: 'Output',
          data: { output: '\u001b[31mhello\u001b[0m' },
        },
      ],
      nextAfterSequence: 1,
      hasMore: true,
      terminal: false,
    });
    await Promise.resolve();
    const second = http.expectOne((request) => request.url.includes('/events'));
    expect(second.request.params.get('afterSequence')).toBe('1');
    second.flush({ events: [], nextAfterSequence: 1, hasMore: false, terminal: true });
    const text = await (await journal).text();
    expect(text).toContain('hello');
    expect(text).not.toContain('\u001b');
  });
});
