import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  AgentRunEventStreamService,
  type AgentRunStreamConnection,
  type AgentRunStreamHandlers,
} from './agent-run-event-stream.service';
import { AgentRunSupervisionStore } from './agent-run-supervision.store';
import { runArtifacts, runEvents, runInspection } from './agent-runs.fixtures';

class FakeEventStream {
  handlers: AgentRunStreamHandlers[] = [];
  cursors: number[] = [];
  closes = 0;

  connect(
    _runId: string,
    afterSequence: number,
    handlers: AgentRunStreamHandlers,
  ): AgentRunStreamConnection {
    this.cursors.push(afterSequence);
    this.handlers.push(handlers);
    return { close: () => (this.closes += 1) };
  }
}

describe('AgentRunSupervisionStore', () => {
  let store: AgentRunSupervisionStore;
  let http: HttpTestingController;
  let stream: FakeEventStream;

  beforeEach(() => {
    stream = new FakeEventStream();
    TestBed.configureTestingModule({
      providers: [
        AgentRunSupervisionStore,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AgentRunEventStreamService, useValue: stream },
      ],
    });
    store = TestBed.inject(AgentRunSupervisionStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  async function loadRunningRun() {
    store.load('run-01K123');
    http.expectOne('/api/v1/runs/run-01K123').flush(runInspection);
    await Promise.resolve();
    await Promise.resolve();
    const replay = http.expectOne((request) => request.url.includes('/events'));
    expect(replay.request.params.get('afterSequence')).toBe('0');
    replay.flush({
      events: runEvents.slice(0, 7),
      nextAfterSequence: 7,
      hasMore: false,
      terminal: false,
    });
    await Promise.resolve();
    await Promise.resolve();
  }

  it('replays before streaming, deduplicates sequence numbers, and sanitizes live output', async () => {
    await loadRunningRun();
    expect(stream.cursors).toEqual([7]);
    stream.handlers[0]!.open();
    expect(store.connectivity()).toBe('live');
    stream.handlers[0]!.event(runEvents[6]!);
    stream.handlers[0]!.event(runEvents[7]!);
    expect(store.lastSequence()).toBe(8);
    expect(store.entries().filter((entry) => entry.sequence === 7)).toHaveLength(1);
    expect(store.entries().at(-2)?.message).toBe('npm test');
    expect(store.entries().at(-1)?.message).toBe('133 tests passed');
  });

  it('pauses by closing the stream and resumes through durable replay', async () => {
    await loadRunningRun();
    store.setPaused(true);
    expect(store.connectivity()).toBe('paused');
    expect(stream.closes).toBeGreaterThan(0);
    store.setPaused(false);
    await Promise.resolve();
    const replay = http.expectOne((request) => request.url.includes('/events'));
    expect(replay.request.params.get('afterSequence')).toBe('7');
    replay.flush({ events: [runEvents[7]], nextAfterSequence: 8, hasMore: false, terminal: false });
    await Promise.resolve();
    await Promise.resolve();
    expect(stream.cursors.at(-1)).toBe(8);
  });

  it('keeps runner connectivity separate from authoritative run progress', async () => {
    vi.useFakeTimers();
    await loadRunningRun();
    stream.handlers[0]!.error();
    expect(store.connectivity()).toBe('reconnecting');
    expect(store.run()?.state).toBe('running');
    await vi.advanceTimersByTimeAsync(1000);
    const replay = http.expectOne((request) => request.url.includes('/events'));
    expect(replay.request.params.get('afterSequence')).toBe('7');
    replay.flush({ events: [], nextAfterSequence: 7, hasMore: false, terminal: false });
    await Promise.resolve();
    vi.useRealTimers();
  });

  it('requests cancellation without inventing a terminal state', async () => {
    await loadRunningRun();
    const cancellation = store.cancel();
    const request = http.expectOne('/api/v1/runs/run-01K123/cancel');
    expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
    request.flush({
      disposition: 'requested',
      run: { ...runInspection.run, cancellationRequestedAt: '2026-07-29T08:04:00.000Z' },
    });
    expect(await cancellation).toBe(true);
    expect(store.run()?.state).toBe('running');
    expect(store.canCancel()).toBe(false);
  });

  it('replays an already-completed run before presenting its journal as complete', async () => {
    store.load('run-01K123');
    http.expectOne('/api/v1/runs/run-01K123').flush({
      ...runInspection,
      run: {
        ...runInspection.run,
        state: 'completed',
        terminalAt: '2026-07-29T08:10:00.000Z',
      },
    });
    await Promise.resolve();
    http.expectOne('/api/v1/runs/run-01K123/artifacts').flush(runArtifacts);
    await new Promise((resolve) => setTimeout(resolve, 0));
    http
      .expectOne((request) => request.url.includes('/events'))
      .flush({ events: runEvents, nextAfterSequence: 8, hasMore: false, terminal: false });
    await Promise.resolve();
    await Promise.resolve();

    expect(store.lastSequence()).toBe(8);
    expect(store.entries().at(-1)?.message).toBe('133 tests passed');
    expect(store.artifacts()).toEqual(runArtifacts);
    expect(store.connectivity()).toBe('complete');
    expect(stream.cursors).toEqual([]);
  });

  it('repairs a live sequence gap through replay without duplicating output', async () => {
    vi.useFakeTimers();
    try {
      await loadRunningRun();
      stream.handlers[0]!.event({ ...runEvents[7]!, sequence: 9 });
      await vi.advanceTimersByTimeAsync(0);
      http
        .expectOne((request) => request.url.includes('/events'))
        .flush({
          events: [runEvents[7]!, { ...runEvents[7]!, sequence: 9, data: { output: 'done' } }],
          nextAfterSequence: 9,
          hasMore: false,
          terminal: false,
        });
      await Promise.resolve();
      await Promise.resolve();

      expect(store.lastSequence()).toBe(9);
      expect(store.entries().filter((entry) => entry.sequence === 8)).toHaveLength(2);
      expect(store.entries().filter((entry) => entry.sequence === 9)).toHaveLength(1);
      expect(stream.cursors.at(-1)).toBe(9);
    } finally {
      vi.useRealTimers();
    }
  });

  it('bounds retained browser output while preserving the durable sequence cursor', async () => {
    const events = Array.from({ length: 10_005 }, (_, index) => ({
      ...runEvents[0]!,
      sequence: index + 1,
      summary: `Output ${index + 1}`,
    }));
    store.load('run-01K123');
    http.expectOne('/api/v1/runs/run-01K123').flush(runInspection);
    await Promise.resolve();
    await Promise.resolve();
    http
      .expectOne((request) => request.url.includes('/events'))
      .flush({ events, nextAfterSequence: events.length, hasMore: false, terminal: false });
    await Promise.resolve();
    await Promise.resolve();

    expect(store.lastSequence()).toBe(10_005);
    expect(store.entries()).toHaveLength(10_000);
    expect(store.droppedEntries()).toBe(5);
    expect(stream.cursors.at(-1)).toBe(10_005);
  });
});
