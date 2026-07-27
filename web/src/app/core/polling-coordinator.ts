import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpParams, HttpResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { StaticModeService } from '../static/static-mode.service';

export type PollingState = 'idle' | 'online' | 'retrying';

export interface PollingTarget {
  url: string;
  etag: string;
  params?: HttpParams | Record<string, string>;
}

export interface PollingOptions<T> {
  target: () => PollingTarget | null;
  accept: (response: HttpResponse<T>) => void;
  missing?: () => void;
  intervalMs?: number;
}

export class PollingSession {
  readonly state = signal<PollingState>('idle');
  private active = false;
  private request: { unsubscribe(): void } | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private readonly http: HttpClient,
    private readonly document: Document,
    private readonly options: PollingOptions<unknown>,
    private readonly disabled = false,
  ) {}

  start(immediate = false): void {
    if (this.disabled) return;
    const wasActive = this.active;
    this.active = true;
    if (this.document.visibilityState === 'hidden') return;
    if (immediate || !wasActive) this.schedule(immediate ? 0 : (this.options.intervalMs ?? 5000));
  }

  stop(): void {
    this.active = false;
    this.clearTimer();
    this.request?.unsubscribe();
    this.request = null;
    this.state.set('idle');
  }

  restart(immediate = true): void {
    this.request?.unsubscribe();
    this.request = null;
    this.clearTimer();
    if (this.active && this.document.visibilityState !== 'hidden')
      this.schedule(immediate ? 0 : 5000);
  }

  visibilityChanged(): void {
    if (!this.active) return;
    if (this.document.visibilityState === 'hidden') {
      this.clearTimer();
      this.request?.unsubscribe();
      this.request = null;
      return;
    }
    this.restart(true);
  }

  private schedule(delay: number): void {
    this.clearTimer();
    this.timer = setTimeout(() => this.poll(), delay);
  }

  private poll(): void {
    this.timer = null;
    if (!this.active || this.request || this.document.visibilityState === 'hidden') return;
    const target = this.options.target();
    if (!target?.etag) {
      this.schedule(this.options.intervalMs ?? 5000);
      return;
    }
    this.request = this.http
      .get(target.url, {
        observe: 'response',
        responseType: 'json',
        params: target.params,
        headers: { 'If-None-Match': target.etag },
      })
      .subscribe({
        next: (response) => {
          this.request = null;
          this.state.set('online');
          if (response.status !== 304) this.options.accept(response);
          this.schedule(this.options.intervalMs ?? 5000);
        },
        error: (error: unknown) => {
          this.request = null;
          if (error instanceof HttpErrorResponse && error.status === 304) {
            this.state.set('online');
          } else if (error instanceof HttpErrorResponse && error.status === 404) {
            this.state.set('online');
            this.options.missing?.();
          } else {
            this.state.set('retrying');
          }
          this.schedule(this.options.intervalMs ?? 5000);
        },
      });
  }

  private clearTimer(): void {
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = null;
  }
}

@Injectable()
export class PollingCoordinator {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT, { optional: true }) ?? globalThis.document;
  private readonly destroyRef = inject(DestroyRef);
  private readonly staticMode = inject(StaticModeService);
  private readonly sessions = new Set<PollingSession>();

  constructor() {
    this.document.addEventListener('visibilitychange', this.onVisibilityChange);
    this.destroyRef.onDestroy(() => {
      this.document.removeEventListener('visibilitychange', this.onVisibilityChange);
      for (const session of this.sessions) session.stop();
    });
  }

  create<T>(options: PollingOptions<T>): PollingSession {
    const session = new PollingSession(
      this.http,
      this.document,
      options as PollingOptions<unknown>,
      this.staticMode.enabled,
    );
    this.sessions.add(session);
    return session;
  }

  private readonly onVisibilityChange = () => {
    for (const session of this.sessions) session.visibilityChanged();
  };
}
