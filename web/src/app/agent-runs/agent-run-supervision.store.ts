import { DestroyRef, inject, Injectable, signal, computed } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  AgentRunsApiService,
  type AgentRunArtifact,
  type AgentRunEvent,
  type AgentRunInspection,
  type AgentRunState,
} from './agent-runs-api.service';
import {
  AgentRunEventStreamService,
  type AgentRunStreamConnection,
  type AgentRunStreamEnd,
} from './agent-run-event-stream.service';
import {
  eventLogEntries,
  isTerminalRunState,
  projectCheckpoints,
  sanitizeRunEvent,
  type AgentRunConnectivity,
  type AgentRunLogEntry,
} from './agent-run-events';

const replayPageSize = 500;
const maximumRetainedEntries = 10_000;
const maximumRetainedCharacters = 16 * 1024 * 1024;
const inspectionIntervalMilliseconds = 15_000;

@Injectable()
export class AgentRunSupervisionStore {
  private readonly api = inject(AgentRunsApiService);
  private readonly streams = inject(AgentRunEventStreamService);
  private readonly destroyRef = inject(DestroyRef);
  private connection: AgentRunStreamConnection | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private inspectionTimer: ReturnType<typeof setInterval> | null = null;
  private streamGeneration = 0;
  private loadGeneration = 0;
  private reconnectAttempts = 0;
  private retainedCharacters = 0;
  private runId = '';

  readonly inspection = signal<AgentRunInspection | null>(null);
  readonly entries = signal<AgentRunLogEntry[]>([]);
  readonly artifacts = signal<AgentRunArtifact[]>([]);
  readonly seenStates = signal<ReadonlySet<AgentRunState>>(new Set());
  readonly lastSequence = signal(0);
  readonly droppedEntries = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly connectionError = signal<string | null>(null);
  readonly connectivity = signal<AgentRunConnectivity>('loading');
  readonly paused = signal(false);
  readonly cancellationPending = signal(false);
  readonly actionError = signal<string | null>(null);
  readonly downloading = signal(false);
  readonly lastStateSummary = signal<string | null>(null);

  readonly run = computed(() => this.inspection()?.run ?? null);
  readonly terminal = computed(() => !!this.run() && isTerminalRunState(this.run()!.state));
  readonly checkpoints = computed(() =>
    projectCheckpoints(this.seenStates(), this.run()?.state ?? null, this.lastStateSummary()),
  );
  readonly canCancel = computed(
    () =>
      !!this.run() &&
      !this.terminal() &&
      !this.run()!.cancellationRequestedAt &&
      !this.cancellationPending(),
  );

  constructor() {
    this.destroyRef.onDestroy(() => this.dispose());
  }

  load(runId: string): void {
    this.disposeConnections();
    this.runId = runId;
    this.loadGeneration += 1;
    this.inspection.set(null);
    this.entries.set([]);
    this.artifacts.set([]);
    this.seenStates.set(new Set());
    this.lastSequence.set(0);
    this.droppedEntries.set(0);
    this.retainedCharacters = 0;
    this.loading.set(true);
    this.error.set(null);
    this.connectionError.set(null);
    this.connectivity.set('loading');
    this.paused.set(false);
    this.actionError.set(null);
    this.lastStateSummary.set(null);
    void this.initialize(this.loadGeneration);
  }

  retryConnection(): void {
    if (!this.runId || this.paused() || this.terminal()) return;
    this.clearReconnectTimer();
    this.reconnectAttempts = 0;
    this.connectionError.set(null);
    void this.replayAndConnect(this.loadGeneration);
  }

  setPaused(paused: boolean): void {
    if (this.terminal()) return;
    this.paused.set(paused);
    if (paused) {
      this.closeConnection();
      this.clearReconnectTimer();
      this.connectivity.set('paused');
      return;
    }
    this.reconnectAttempts = 0;
    void this.replayAndConnect(this.loadGeneration);
  }

  async cancel(): Promise<boolean> {
    if (!this.canCancel()) return false;
    this.cancellationPending.set(true);
    this.actionError.set(null);
    try {
      const response = await firstValueFrom(this.api.cancel(this.runId));
      if (response.body) this.updateRun(response.body.run);
      return true;
    } catch (error) {
      this.actionError.set(this.api.error(error, 'Cancellation could not be requested.').message);
      return false;
    } finally {
      this.cancellationPending.set(false);
    }
  }

  async eventJournal(): Promise<Blob | null> {
    if (this.downloading()) return null;
    this.downloading.set(true);
    this.actionError.set(null);
    try {
      return await this.api.eventJournal(this.runId);
    } catch (error) {
      this.actionError.set(
        this.api.error(error, 'The complete event journal could not be downloaded.').message,
      );
      return null;
    } finally {
      this.downloading.set(false);
    }
  }

  private async initialize(generation: number): Promise<void> {
    try {
      await this.refreshInspection(generation, true);
      if (generation !== this.loadGeneration) return;
      this.loading.set(false);
      this.startInspectionPolling(generation);
      await this.replayAndConnect(generation);
    } catch (error) {
      if (generation !== this.loadGeneration) return;
      this.loading.set(false);
      this.connectivity.set('reconnecting');
      this.error.set(this.api.error(error, 'The agent run could not be loaded.').message);
    }
  }

  private async refreshInspection(generation: number, required = false): Promise<void> {
    try {
      const response = await firstValueFrom(this.api.inspect(this.runId));
      if (generation !== this.loadGeneration || !response.body) return;
      this.inspection.set(response.body);
      this.recordState(response.body.run.state);
      if (isTerminalRunState(response.body.run.state)) {
        this.stopInspectionPolling();
        if (this.connectivity() !== 'loading') this.connectivity.set('complete');
        await this.loadArtifacts(generation);
      }
    } catch (error) {
      if (required) throw error;
      if (generation === this.loadGeneration)
        this.connectionError.set(
          this.api.error(error, 'Runner inspection is temporarily unavailable.').message,
        );
    }
  }

  private async replayAndConnect(generation: number): Promise<void> {
    if (generation !== this.loadGeneration || this.paused()) return;
    this.closeConnection();
    this.connectivity.set(this.lastSequence() ? 'reconnecting' : 'connecting');
    try {
      let hasMore = true;
      while (hasMore && generation === this.loadGeneration && !this.paused()) {
        const pageStartSequence = this.lastSequence();
        const response = await firstValueFrom(
          this.api.events(this.runId, this.lastSequence(), replayPageSize),
        );
        const page = response.body;
        if (!page) throw new Error('The run event replay returned an empty page.');
        const entries: AgentRunLogEntry[] = [];
        for (const event of page.events) {
          if (!this.append(event, entries))
            throw new Error('The durable event journal contains a sequence gap.');
        }
        this.appendEntries(entries);
        hasMore = page.hasMore;
        if (hasMore && this.lastSequence() <= pageStartSequence)
          throw new Error('The durable event journal did not advance its sequence cursor.');
        if (page.terminal && !hasMore) {
          this.connectivity.set('complete');
          await this.refreshInspection(generation);
          return;
        }
      }
      if (generation !== this.loadGeneration || this.paused()) return;
      if (this.terminal()) {
        this.connectivity.set('complete');
        return;
      }
      this.openStream(generation);
    } catch (error) {
      if (generation !== this.loadGeneration || this.paused()) return;
      this.connectionError.set(this.api.error(error, 'The event stream disconnected.').message);
      if (this.terminal()) {
        this.connectivity.set('complete');
        return;
      }
      this.scheduleReconnect(generation);
    }
  }

  private openStream(generation: number): void {
    const streamGeneration = ++this.streamGeneration;
    this.connection = this.streams.connect(this.runId, this.lastSequence(), {
      open: () => {
        if (!this.currentStream(generation, streamGeneration)) return;
        this.reconnectAttempts = 0;
        this.connectionError.set(null);
        this.connectivity.set('live');
      },
      event: (event) => {
        if (!this.currentStream(generation, streamGeneration)) return;
        if (!this.append(event)) this.scheduleReconnect(generation, true);
      },
      end: (end) => {
        if (!this.currentStream(generation, streamGeneration)) return;
        this.handleStreamEnd(end, generation);
      },
      error: () => {
        if (!this.currentStream(generation, streamGeneration)) return;
        this.connectionError.set('The live event stream disconnected. Replaying durable events…');
        this.scheduleReconnect(generation);
      },
    });
  }

  private handleStreamEnd(end: AgentRunStreamEnd, generation: number): void {
    this.closeConnection();
    if (Number(end.lastSequence) > this.lastSequence()) {
      void this.replayAndConnect(generation);
      return;
    }
    this.connectivity.set('complete');
    this.recordState(end.state);
    this.updateRunState(end.state);
    this.stopInspectionPolling();
    void this.refreshInspection(generation);
  }

  private append(unsafeEvent: AgentRunEvent, entries?: AgentRunLogEntry[]): boolean {
    const event = sanitizeRunEvent(unsafeEvent);
    const sequence = Number(event.sequence);
    if (!Number.isSafeInteger(sequence) || sequence <= 0) return false;
    if (sequence <= this.lastSequence()) return true;
    if (sequence !== this.lastSequence() + 1) return false;
    this.lastSequence.set(sequence);
    if (event.state) {
      this.recordState(event.state);
      if (!this.terminal() || isTerminalRunState(event.state))
        this.updateRunState(event.state, event.timestamp, sequence);
      this.lastStateSummary.set(event.summary);
    }
    const additions = eventLogEntries(event);
    if (entries) entries.push(...additions);
    else this.appendEntries(additions);
    return true;
  }

  private appendEntries(additions: AgentRunLogEntry[]): void {
    let entries = [...this.entries(), ...additions];
    this.retainedCharacters += additions.reduce((total, item) => total + item.message.length, 0);
    let removed = Math.max(0, entries.length - maximumRetainedEntries);
    let removedCharacters = entries
      .slice(0, removed)
      .reduce((total, item) => total + item.message.length, 0);
    while (
      removed < entries.length &&
      this.retainedCharacters - removedCharacters > maximumRetainedCharacters
    ) {
      removedCharacters += entries[removed]!.message.length;
      removed += 1;
    }
    this.retainedCharacters -= removedCharacters;
    if (removed) entries = entries.slice(removed);
    if (removed) this.droppedEntries.update((count) => count + removed);
    this.entries.set(entries);
  }

  private recordState(state: AgentRunState): void {
    this.seenStates.update((states) => new Set([...states, state]));
  }

  private updateRunState(state: AgentRunState, timestamp?: string, sequence?: number): void {
    const inspection = this.inspection();
    if (!inspection) return;
    this.inspection.set({
      ...inspection,
      run: {
        ...inspection.run,
        state,
        updatedAt: timestamp ?? inspection.run.updatedAt,
        lastEventSequence: sequence ?? inspection.run.lastEventSequence,
        terminalAt: isTerminalRunState(state) && timestamp ? timestamp : inspection.run.terminalAt,
      },
    });
  }

  private updateRun(run: AgentRunInspection['run']): void {
    const inspection = this.inspection();
    if (!inspection) return;
    this.inspection.set({ ...inspection, run });
    this.recordState(run.state);
  }

  private async loadArtifacts(generation: number): Promise<void> {
    try {
      const response = await firstValueFrom(this.api.artifacts(this.runId));
      if (generation === this.loadGeneration) this.artifacts.set(response.body ?? []);
    } catch {
      // Artifact metadata is secondary to supervision and can be retried on inspection refresh.
    }
  }

  private startInspectionPolling(generation: number): void {
    this.stopInspectionPolling();
    if (this.terminal()) return;
    this.inspectionTimer = setInterval(
      () => void this.refreshInspection(generation),
      inspectionIntervalMilliseconds,
    );
  }

  private stopInspectionPolling(): void {
    if (this.inspectionTimer) clearInterval(this.inspectionTimer);
    this.inspectionTimer = null;
  }

  private scheduleReconnect(generation: number, immediate = false): void {
    if (generation !== this.loadGeneration || this.paused() || this.terminal()) return;
    this.closeConnection();
    this.clearReconnectTimer();
    this.connectivity.set('reconnecting');
    const delay = immediate ? 0 : Math.min(1000 * 2 ** this.reconnectAttempts++, 15_000);
    this.reconnectTimer = setTimeout(() => void this.replayAndConnect(generation), delay);
  }

  private currentStream(generation: number, streamGeneration: number): boolean {
    return generation === this.loadGeneration && streamGeneration === this.streamGeneration;
  }

  private closeConnection(): void {
    this.streamGeneration += 1;
    this.connection?.close();
    this.connection = null;
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
  }

  private disposeConnections(): void {
    this.closeConnection();
    this.clearReconnectTimer();
    this.stopInspectionPolling();
  }

  private dispose(): void {
    this.loadGeneration += 1;
    this.disposeConnections();
  }
}
