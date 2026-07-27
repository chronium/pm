import { once } from 'node:events';
import type { IncomingMessage, ServerResponse } from 'node:http';
import { isTerminal } from './lifecycle.js';
import type { RunStore } from './persistence/run-store.js';

const replayPageSize = 100;

export class EventStreamManager {
  private readonly active = new Set<AbortController>();
  private readonly revisions = new Map<string, number>();
  private readonly waiters = new Map<string, Set<() => void>>();
  private readonly unsubscribe: () => void;

  constructor(
    private readonly store: RunStore,
    private readonly maximumStreams = 64,
    private readonly heartbeatMilliseconds = 15_000,
    private readonly backpressureTimeoutMilliseconds = 30_000,
  ) {
    this.unsubscribe = store.subscribe((events) => {
      for (const event of events) this.notify(event.runId);
    });
  }

  get activeCount(): number {
    return this.active.size;
  }

  canOpen(): boolean {
    return this.active.size < this.maximumStreams;
  }

  async stream(
    runId: string,
    afterSequence: number,
    request: IncomingMessage,
    response: ServerResponse,
  ): Promise<void> {
    if (!this.canOpen()) throw new StreamCapacityError();
    const controller = new AbortController();
    const abort = (): void => controller.abort('client_disconnected');
    request.once('aborted', abort);
    response.once('close', abort);
    this.active.add(controller);
    response.statusCode = 200;
    response.setHeader('Content-Type', 'text/event-stream; charset=utf-8');
    response.setHeader('Connection', 'keep-alive');
    response.setHeader('X-Accel-Buffering', 'no');
    response.flushHeaders();
    response.write('retry: 2000\n\n');

    let cursor = afterSequence;
    try {
      while (!controller.signal.aborted) {
        const observedRevision = this.revisions.get(runId) ?? 0;
        const page = this.store.eventPage(runId, cursor, replayPageSize);
        for (const event of page.events) {
          await this.write(
            response,
            `id: ${event.sequence}\nevent: run-event\ndata: ${JSON.stringify(event)}\n\n`,
            controller.signal,
          );
          cursor = event.sequence;
        }
        if (page.hasMore) continue;

        const run = this.store.getRun(runId);
        if (run === undefined) return;
        if (isTerminal(run.state) && cursor >= run.lastEventSequence) {
          await this.write(
            response,
            `event: stream-end\ndata: ${JSON.stringify({ state: run.state, lastSequence: run.lastEventSequence })}\n\n`,
            controller.signal,
          );
          response.end();
          return;
        }

        const wake = await this.waitForChange(runId, observedRevision, controller.signal);
        if (wake === 'heartbeat') await this.write(response, ': heartbeat\n\n', controller.signal);
      }
    } catch (error) {
      if (!controller.signal.aborted && !(error instanceof StreamClosedError)) response.destroy();
    } finally {
      request.off('aborted', abort);
      response.off('close', abort);
      this.active.delete(controller);
      if (!response.writableEnded && !response.destroyed) response.end();
    }
  }

  close(): void {
    this.unsubscribe();
    for (const controller of this.active) controller.abort('runner_stopping');
    this.active.clear();
    for (const callbacks of this.waiters.values()) for (const callback of callbacks) callback();
    this.waiters.clear();
  }

  private notify(runId: string): void {
    this.revisions.set(runId, (this.revisions.get(runId) ?? 0) + 1);
    const callbacks = this.waiters.get(runId);
    if (callbacks === undefined) return;
    this.waiters.delete(runId);
    for (const callback of callbacks) callback();
  }

  private waitForChange(
    runId: string,
    observedRevision: number,
    signal: AbortSignal,
  ): Promise<'event' | 'heartbeat'> {
    if ((this.revisions.get(runId) ?? 0) !== observedRevision) return Promise.resolve('event');
    return new Promise((resolve) => {
      let finished = false;
      const complete = (result: 'event' | 'heartbeat'): void => {
        if (finished) return;
        finished = true;
        clearTimeout(timer);
        signal.removeEventListener('abort', aborted);
        const callbacks = this.waiters.get(runId);
        callbacks?.delete(changed);
        if (callbacks?.size === 0) this.waiters.delete(runId);
        resolve(result);
      };
      const changed = (): void => complete('event');
      const aborted = (): void => complete('event');
      const timer = setTimeout(() => complete('heartbeat'), this.heartbeatMilliseconds);
      const callbacks = this.waiters.get(runId) ?? new Set<() => void>();
      callbacks.add(changed);
      this.waiters.set(runId, callbacks);
      signal.addEventListener('abort', aborted, { once: true });
      if ((this.revisions.get(runId) ?? 0) !== observedRevision) changed();
    });
  }

  private async write(response: ServerResponse, data: string, signal: AbortSignal): Promise<void> {
    if (signal.aborted || response.destroyed) throw new StreamClosedError();
    if (response.write(data)) return;
    const timeout = setTimeout(
      () => response.destroy(new Error('SSE client remained backpressured.')),
      this.backpressureTimeoutMilliseconds,
    );
    try {
      await Promise.race([
        once(response, 'drain'),
        once(response, 'close').then(() => {
          throw new StreamClosedError();
        }),
      ]);
    } finally {
      clearTimeout(timeout);
    }
  }
}

export class StreamCapacityError extends Error {}
class StreamClosedError extends Error {}
