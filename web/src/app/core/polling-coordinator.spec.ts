import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PollingCoordinator } from './polling-coordinator';

describe('PollingCoordinator', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [PollingCoordinator, provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('waits five seconds, sends the exact strong ETag, handles 304, and never overlaps', () => {
    const accepted = vi.fn();
    const session = TestBed.inject(PollingCoordinator).create({
      target: () => ({ url: '/resource', etag: '"r1"' }),
      accept: accepted,
    });
    const http = TestBed.inject(HttpTestingController);

    session.start();
    vi.advanceTimersByTime(4999);
    http.expectNone('/resource');
    vi.advanceTimersByTime(1);
    const first = http.expectOne('/resource');
    expect(first.request.headers.get('If-None-Match')).toBe('"r1"');
    vi.advanceTimersByTime(10_000);
    http.expectNone('/resource');
    first.flush(null, { status: 304, statusText: 'Not Modified' });
    expect(session.state()).toBe('online');
    expect(accepted).not.toHaveBeenCalled();
    vi.advanceTimersByTime(5000);
    http.expectOne('/resource').flush({ value: 2 }, { headers: { ETag: '"r2"' } });
    expect(accepted).toHaveBeenCalledOnce();
    session.stop();
  });

  it('cancels obsolete requests and retries transient failures without dropping the session', () => {
    let url = '/first';
    const session = TestBed.inject(PollingCoordinator).create({
      target: () => ({ url, etag: '"r1"' }),
      accept: vi.fn(),
    });
    const http = TestBed.inject(HttpTestingController);
    session.start(true);
    vi.advanceTimersByTime(0);
    const obsolete = http.expectOne('/first');
    url = '/second';
    session.restart(true);
    expect(obsolete.cancelled).toBe(true);
    vi.advanceTimersByTime(0);
    http.expectOne('/second').error(new ProgressEvent('network'));
    expect(session.state()).toBe('retrying');
    vi.advanceTimersByTime(5000);
    http.expectOne('/second').flush(null, { status: 304, statusText: 'Not Modified' });
    expect(session.state()).toBe('online');
    session.stop();
  });

  it('suspends hidden tabs and checks immediately when visibility returns', () => {
    let visibility: DocumentVisibilityState = 'hidden';
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
    const session = TestBed.inject(PollingCoordinator).create({
      target: () => ({ url: '/visible', etag: '"r1"' }),
      accept: vi.fn(),
    });
    const http = TestBed.inject(HttpTestingController);

    session.start();
    vi.advanceTimersByTime(10_000);
    http.expectNone('/visible');
    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange'));
    vi.advanceTimersByTime(0);
    http.expectOne('/visible').flush(null, { status: 304, statusText: 'Not Modified' });
    session.stop();
  });

  it('stops scheduling and cancels work during teardown', () => {
    const session = TestBed.inject(PollingCoordinator).create({
      target: () => ({ url: '/teardown', etag: '"r1"' }),
      accept: vi.fn(),
    });
    const http = TestBed.inject(HttpTestingController);
    session.start(true);
    vi.advanceTimersByTime(0);
    const request = http.expectOne('/teardown');
    session.stop();
    expect(request.cancelled).toBe(true);
    vi.advanceTimersByTime(10_000);
    http.expectNone('/teardown');
  });
});
