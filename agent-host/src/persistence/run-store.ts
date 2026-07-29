import { randomUUID } from 'node:crypto';
import { chmodSync, existsSync, lstatSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { activeRunStates, canTransition, isTerminal } from '../lifecycle.js';
import { canonicalSpecification, fixedTimeHashEquals } from '../protocol/canonical-json.js';
import { sanitizeEventDraft } from '../protocol/event-sanitizer.js';
import { isRunState, parseRunRequest, validateArtifact } from '../protocol/validation.js';
import type {
  RunArtifact,
  RunEvent,
  RunRequest,
  RunSpecification,
  RunState,
} from '../protocol/types.js';
import { applyMigrations } from './migrations.js';

export interface StoredRun {
  runId: string;
  specificationHash: string;
  specification: RunSpecification;
  state: RunState;
  lastEventSequence: number;
  acceptedAt: string;
  updatedAt: string;
  terminalAt: string | null;
  cancellationRequestedAt: string | null;
  agentThreadId: string | null;
}

export type AcceptRunResult =
  | { disposition: 'new'; run: StoredRun }
  | { disposition: 'existing'; run: StoredRun }
  | { disposition: 'conflict'; code: 'run_id_conflict' }
  | { disposition: 'queue_full'; code: 'queue_full' };

export interface EventDraft {
  type: string;
  state: RunState | null;
  summary: string;
  data?: unknown;
}

const eventType = /^[a-z][a-z0-9_-]*\.[a-z0-9][a-z0-9._-]*$/;

export interface RecoveryResult {
  queued: number;
  failed: number;
}

export interface ExpiredRun {
  runId: string;
  artifactLocations: string[];
}

export interface ActiveRunCursor {
  acceptedAt: string;
  runId: string;
}

export interface ActiveRunPage {
  runs: StoredRun[];
  hasMore: boolean;
  nextCursor: ActiveRunCursor | null;
}

export interface RunEventPage {
  events: RunEvent[];
  hasMore: boolean;
  nextAfterSequence: number;
}

export type CancellationResult =
  | { disposition: 'not_found' }
  | { disposition: 'terminal'; run: StoredRun }
  | { disposition: 'already_requested'; run: StoredRun }
  | { disposition: 'cancelled'; run: StoredRun }
  | { disposition: 'requested'; run: StoredRun };

interface RunRow {
  run_id: string;
  specification_hash: string;
  specification_json: string;
  state: string;
  last_event_sequence: number;
  accepted_at: string;
  updated_at: string;
  terminal_at: string | null;
  cancellation_requested_at: string | null;
  agent_thread_id: string | null;
}

interface EventRow {
  run_id: string;
  sequence: number;
  timestamp: string;
  type: string;
  state: string | null;
  summary: string;
  data_json: string | null;
}

export class RunStore {
  readonly runnerId: string;
  private readonly database: DatabaseSync;
  private readonly eventListeners = new Set<(events: readonly RunEvent[]) => void>();
  private transactionEvents: RunEvent[] | null = null;

  constructor(
    readonly dataRoot: string,
    private readonly now: () => Date = () => new Date(),
  ) {
    prepareDataRoot(dataRoot);
    const databasePath = join(dataRoot, 'runner.sqlite');
    const databaseExisted = existsSync(databasePath);
    if (databaseExisted) assertSecureFile(databasePath, 'Runner database');
    this.database = new DatabaseSync(databasePath);
    if (!databaseExisted) chmodSync(databasePath, 0o600);
    this.database.exec('PRAGMA foreign_keys = ON');
    this.database.exec('PRAGMA journal_mode = WAL');
    this.database.exec('PRAGMA synchronous = FULL');
    this.database.exec('PRAGMA busy_timeout = 5000');
    applyMigrations(this.database);
    this.runnerId = this.getOrCreateRunnerId();
    this.verifyPersistedRuns();
  }

  close(): void {
    this.database.close();
  }

  subscribe(listener: (events: readonly RunEvent[]) => void): () => void {
    this.eventListeners.add(listener);
    return () => this.eventListeners.delete(listener);
  }

  acceptRun(request: RunRequest, queueCapacity: number): AcceptRunResult {
    if (!Number.isSafeInteger(queueCapacity) || queueCapacity <= 0)
      throw new Error('Queue capacity must be a positive integer.');
    const validatedRequest = parseRunRequest(request);
    return this.transaction(() => {
      const existing = this.getRunRow(validatedRequest.specification.runId);
      if (existing !== undefined) {
        if (fixedTimeHashEquals(existing.specification_hash, validatedRequest.specificationHash))
          return { disposition: 'existing', run: toStoredRun(existing) };
        return { disposition: 'conflict', code: 'run_id_conflict' };
      }

      if (this.queueDepth() >= queueCapacity)
        return { disposition: 'queue_full', code: 'queue_full' };

      const timestamp = this.timestamp();
      this.database
        .prepare(
          `INSERT INTO runs (
            run_id, specification_hash, specification_json, state,
            last_event_sequence, accepted_at, updated_at
          ) VALUES (?, ?, ?, 'accepted', 0, ?, ?)`,
        )
        .run(
          validatedRequest.specification.runId,
          validatedRequest.specificationHash,
          canonicalSpecification(validatedRequest.specification),
          timestamp,
          timestamp,
        );
      this.appendEventInTransaction(validatedRequest.specification.runId, {
        type: 'run.state_changed',
        state: 'accepted',
        summary: 'Task accepted',
        data: { previousState: 'requested', nextState: 'accepted' },
      });
      this.transitionInTransaction(validatedRequest.specification.runId, 'queued', 'Run queued', {
        previousState: 'accepted',
        nextState: 'queued',
      });
      this.database
        .prepare('INSERT INTO run_queue (run_id, enqueued_at) VALUES (?, ?)')
        .run(validatedRequest.specification.runId, timestamp);

      return { disposition: 'new', run: this.requireRun(validatedRequest.specification.runId) };
    });
  }

  getRun(runId: string): StoredRun | undefined {
    const row = this.getRunRow(runId);
    return row === undefined ? undefined : toStoredRun(row);
  }

  recordAgentThreadId(runId: string, threadId: string): void {
    if (
      threadId.length === 0 ||
      threadId.length > 256 ||
      threadId !== threadId.trim() ||
      hasControlCharacters(threadId)
    )
      throw new Error('Agent thread ID is invalid.');
    this.transaction(() => {
      const current = this.requireRun(runId);
      if (current.agentThreadId !== null && current.agentThreadId !== threadId)
        throw new Error('Agent thread ID cannot be changed after it is recorded.');
      if (current.agentThreadId === threadId) return;
      this.database
        .prepare('UPDATE runs SET agent_thread_id = ?, updated_at = ? WHERE run_id = ?')
        .run(threadId, this.timestamp(), runId);
    });
  }

  listActiveRuns(limit: number, cursor: ActiveRunCursor | null = null): ActiveRunPage {
    validatePageLimit(limit);
    const rows = (cursor === null
      ? this.database
          .prepare(
            `SELECT run_id, specification_hash, specification_json, state, last_event_sequence,
                      accepted_at, updated_at, terminal_at, cancellation_requested_at,
                      agent_thread_id
               FROM runs WHERE terminal_at IS NULL
               ORDER BY accepted_at, run_id LIMIT ?`,
          )
          .all(limit + 1)
      : this.database
          .prepare(
            `SELECT run_id, specification_hash, specification_json, state, last_event_sequence,
                      accepted_at, updated_at, terminal_at, cancellation_requested_at,
                      agent_thread_id
               FROM runs WHERE terminal_at IS NULL
                 AND (accepted_at > ? OR (accepted_at = ? AND run_id > ?))
               ORDER BY accepted_at, run_id LIMIT ?`,
          )
          .all(
            cursor.acceptedAt,
            cursor.acceptedAt,
            cursor.runId,
            limit + 1,
          )) as unknown as RunRow[];
    const hasMore = rows.length > limit;
    const runs = rows.slice(0, limit).map((row) => toStoredRun(row));
    const last = runs.at(-1);
    return {
      runs,
      hasMore,
      nextCursor:
        hasMore && last !== undefined ? { acceptedAt: last.acceptedAt, runId: last.runId } : null,
    };
  }

  queueDepth(): number {
    const row = this.database.prepare('SELECT COUNT(*) AS count FROM run_queue').get() as {
      count: number;
    };
    return row.count;
  }

  activeRunCount(): number {
    const row = this.database
      .prepare(
        `SELECT COUNT(*) AS count FROM runs
         WHERE state IN (
           'preparing_workspace', 'starting_runtime', 'starting_agent',
           'running', 'validating', 'collecting_artifacts'
         )`,
      )
      .get() as { count: number };
    return row.count;
  }

  claimNextRun(): StoredRun | undefined {
    return this.transaction(() => {
      const row = this.database
        .prepare(
          `SELECT runs.run_id
           FROM run_queue
           JOIN runs ON runs.run_id = run_queue.run_id
           WHERE runs.state = 'queued'
           ORDER BY run_queue.sequence
           LIMIT 1`,
        )
        .get() as { run_id: string } | undefined;
      if (row === undefined) return undefined;

      this.database.prepare('DELETE FROM run_queue WHERE run_id = ?').run(row.run_id);
      this.transitionInTransaction(row.run_id, 'preparing_workspace', 'Preparing workspace', {
        previousState: 'queued',
        nextState: 'preparing_workspace',
      });
      return this.requireRun(row.run_id);
    });
  }

  transition(runId: string, nextState: RunState, summary: string, data?: unknown): RunEvent {
    return this.transaction(() => this.transitionInTransaction(runId, nextState, summary, data));
  }

  appendEvent(runId: string, draft: EventDraft): RunEvent {
    return this.transaction(() => this.appendEventInTransaction(runId, draft));
  }

  eventsAfter(runId: string, afterSequence = 0): RunEvent[] {
    validateSequence(afterSequence);
    return (
      this.database
        .prepare(
          `SELECT run_id, sequence, timestamp, type, state, summary, data_json
           FROM run_events WHERE run_id = ? AND sequence > ? ORDER BY sequence`,
        )
        .all(runId, afterSequence) as unknown as EventRow[]
    ).map(toRunEvent);
  }

  eventPage(runId: string, afterSequence: number, limit: number): RunEventPage {
    validateSequence(afterSequence);
    validatePageLimit(limit);
    const rows = this.database
      .prepare(
        `SELECT run_id, sequence, timestamp, type, state, summary, data_json
         FROM run_events WHERE run_id = ? AND sequence > ? ORDER BY sequence LIMIT ?`,
      )
      .all(runId, afterSequence, limit + 1) as unknown as EventRow[];
    const hasMore = rows.length > limit;
    const events = rows.slice(0, limit).map(toRunEvent);
    return {
      events,
      hasMore,
      nextAfterSequence: events.at(-1)?.sequence ?? afterSequence,
    };
  }

  requestCancellation(runId: string): CancellationResult {
    return this.transaction(() => {
      const current = this.getRun(runId);
      if (current === undefined) return { disposition: 'not_found' };
      if (isTerminal(current.state)) return { disposition: 'terminal', run: current };
      if (current.cancellationRequestedAt !== null)
        return { disposition: 'already_requested', run: current };

      const timestamp = this.timestamp();
      this.database
        .prepare('UPDATE runs SET cancellation_requested_at = ?, updated_at = ? WHERE run_id = ?')
        .run(timestamp, timestamp, runId);
      this.appendEventInTransaction(runId, {
        type: 'run.cancellation_requested',
        state: current.state,
        summary: 'Cancellation requested',
        data: { reason: 'client_requested' },
      });
      if (current.state === 'accepted' || current.state === 'queued') {
        this.transitionInTransaction(runId, 'cancelled', 'Run cancelled', {
          previousState: current.state,
          nextState: 'cancelled',
          reason: 'client_requested',
        });
        return { disposition: 'cancelled', run: this.requireRun(runId) };
      }
      return { disposition: 'requested', run: this.requireRun(runId) };
    });
  }

  isCancellationRequested(runId: string): boolean {
    const run = this.getRun(runId);
    return run !== undefined && run.cancellationRequestedAt !== null;
  }

  recover(): RecoveryResult {
    return this.transaction(() => {
      const rows = this.database
        .prepare(
          `SELECT run_id, state FROM runs
           WHERE state NOT IN ('completed', 'failed', 'cancelled')
           ORDER BY accepted_at, run_id`,
        )
        .all() as unknown as { run_id: string; state: string }[];
      let requeued = 0;
      let failed = 0;

      for (const row of rows) {
        if (!isRunState(row.state) || row.state === 'requested')
          throw new Error(`Persisted run ${row.run_id} has an invalid state.`);
        if (row.state === 'accepted') {
          this.transitionInTransaction(row.run_id, 'queued', 'Run recovered into queue', {
            previousState: 'accepted',
            nextState: 'queued',
            reason: 'runner_restarted',
          });
          this.ensureQueued(row.run_id);
          requeued += 1;
        } else if (row.state === 'queued') {
          this.ensureQueued(row.run_id);
          requeued += 1;
        } else if (activeRunStates.includes(row.state)) {
          this.transitionInTransaction(row.run_id, 'failed', 'Run interrupted by runner restart', {
            previousState: row.state,
            nextState: 'failed',
            reason: 'runner_restarted',
          });
          failed += 1;
        }
      }
      return { queued: requeued, failed };
    });
  }

  recordArtifact(runId: string, artifact: RunArtifact, relativeLocation: string): void {
    validateArtifact(artifact);
    validateArtifactLocation(runId, relativeLocation);
    if (this.getRun(runId) === undefined) throw new Error(`Run ${runId} does not exist.`);
    this.database
      .prepare(
        `INSERT INTO run_artifacts (run_id, artifact_id, metadata_json, relative_location)
         VALUES (?, ?, ?, ?)
         ON CONFLICT(run_id, artifact_id) DO UPDATE SET
           metadata_json = excluded.metadata_json,
           relative_location = excluded.relative_location`,
      )
      .run(runId, artifact.artifactId, JSON.stringify(artifact), relativeLocation);
  }

  listArtifacts(runId: string): RunArtifact[] {
    return (
      this.database
        .prepare('SELECT metadata_json FROM run_artifacts WHERE run_id = ? ORDER BY artifact_id')
        .all(runId) as unknown as { metadata_json: string }[]
    ).map((row) => parseStoredArtifact(row.metadata_json));
  }

  getArtifact(runId: string, artifactId: string): RunArtifact | undefined {
    const row = this.database
      .prepare('SELECT metadata_json FROM run_artifacts WHERE run_id = ? AND artifact_id = ?')
      .get(runId, artifactId) as { metadata_json: string } | undefined;
    return row === undefined ? undefined : parseStoredArtifact(row.metadata_json);
  }

  expiredTerminalRuns(cutoff: Date): ExpiredRun[] {
    const rows = this.database
      .prepare(
        `SELECT run_id FROM runs
         WHERE terminal_at IS NOT NULL AND terminal_at <= ?
         ORDER BY terminal_at, run_id`,
      )
      .all(cutoff.toISOString()) as unknown as { run_id: string }[];
    const locations = this.database.prepare(
      'SELECT relative_location FROM run_artifacts WHERE run_id = ? ORDER BY artifact_id',
    );
    return rows.map((row) => ({
      runId: row.run_id,
      artifactLocations: (
        locations.all(row.run_id) as unknown as { relative_location: string }[]
      ).map((artifact) => artifact.relative_location),
    }));
  }

  deleteTerminalRun(runId: string): boolean {
    return this.transaction(() => {
      const run = this.getRun(runId);
      if (run === undefined || !isTerminal(run.state)) return false;
      this.database.prepare('DELETE FROM runs WHERE run_id = ?').run(runId);
      return true;
    });
  }

  private transitionInTransaction(
    runId: string,
    nextState: RunState,
    summary: string,
    data?: unknown,
  ): RunEvent {
    const current = this.requireRun(runId);
    if (!canTransition(current.state, nextState))
      throw new Error(`Run cannot transition from ${current.state} to ${nextState}.`);
    const timestamp = this.timestamp();
    this.database
      .prepare('UPDATE runs SET state = ?, updated_at = ?, terminal_at = ? WHERE run_id = ?')
      .run(nextState, timestamp, isTerminal(nextState) ? timestamp : null, runId);
    if (isTerminal(nextState))
      this.database.prepare('DELETE FROM run_queue WHERE run_id = ?').run(runId);
    return this.appendEventInTransaction(runId, {
      type: 'run.state_changed',
      state: nextState,
      summary,
      data,
    });
  }

  private appendEventInTransaction(runId: string, draft: EventDraft): RunEvent {
    const current = this.requireRun(runId);
    const sanitized = sanitizeEventDraft(draft.type, draft.summary, draft.data ?? null);
    if (
      sanitized.type.length === 0 ||
      sanitized.type.length > 256 ||
      !eventType.test(sanitized.type) ||
      sanitized.summary.length === 0 ||
      sanitized.summary.length > 4096 ||
      hasControlCharacters(sanitized.type) ||
      hasControlCharacters(sanitized.summary) ||
      (draft.state !== null && draft.state !== current.state)
    )
      throw new Error('Durable run event is invalid.');
    const sequence = current.lastEventSequence + 1;
    const timestamp = this.timestamp();
    const event: RunEvent = {
      protocolVersion: '1.0',
      runId,
      sequence,
      timestamp,
      type: sanitized.type,
      state: draft.state,
      summary: sanitized.summary,
      data: sanitized.data,
    };
    this.database
      .prepare(
        `INSERT INTO run_events (run_id, sequence, timestamp, type, state, summary, data_json)
         VALUES (?, ?, ?, ?, ?, ?, ?)`,
      )
      .run(
        runId,
        sequence,
        timestamp,
        event.type,
        event.state,
        event.summary,
        event.data === null ? null : JSON.stringify(event.data),
      );
    this.database
      .prepare('UPDATE runs SET last_event_sequence = ?, updated_at = ? WHERE run_id = ?')
      .run(sequence, timestamp, runId);
    if (this.transactionEvents === null)
      throw new Error('Durable events must be appended inside a transaction.');
    this.transactionEvents.push(event);
    return event;
  }

  private ensureQueued(runId: string): void {
    this.database
      .prepare('INSERT OR IGNORE INTO run_queue (run_id, enqueued_at) VALUES (?, ?)')
      .run(runId, this.timestamp());
  }

  private requireRun(runId: string): StoredRun {
    const row = this.getRunRow(runId);
    if (row === undefined) throw new Error(`Run ${runId} does not exist.`);
    return toStoredRun(row);
  }

  private getRunRow(runId: string): RunRow | undefined {
    return this.database
      .prepare(
        `SELECT run_id, specification_hash, specification_json, state, last_event_sequence,
                accepted_at, updated_at, terminal_at, cancellation_requested_at,
                agent_thread_id
         FROM runs WHERE run_id = ?`,
      )
      .get(runId) as RunRow | undefined;
  }

  private getOrCreateRunnerId(): string {
    const existing = this.database
      .prepare("SELECT value FROM runner_metadata WHERE key = 'runner_id'")
      .get() as { value: string } | undefined;
    if (existing !== undefined) return existing.value;
    const runnerId = `runner-${randomUUID()}`;
    this.database
      .prepare("INSERT INTO runner_metadata (key, value) VALUES ('runner_id', ?)")
      .run(runnerId);
    return runnerId;
  }

  private verifyPersistedRuns(): void {
    const rows = this.database
      .prepare(
        `SELECT run_id, specification_hash, specification_json, state, last_event_sequence,
                accepted_at, updated_at, terminal_at, cancellation_requested_at,
                agent_thread_id
         FROM runs ORDER BY run_id`,
      )
      .all() as unknown as RunRow[];
    for (const row of rows) toStoredRun(row, true);
  }

  private transaction<T>(operation: () => T): T {
    if (this.transactionEvents !== null)
      throw new Error('Nested runner transactions are not supported.');
    this.transactionEvents = [];
    this.database.exec('BEGIN IMMEDIATE');
    try {
      const result = operation();
      this.database.exec('COMMIT');
      const events = this.transactionEvents;
      this.transactionEvents = null;
      if (events.length > 0)
        for (const listener of this.eventListeners) {
          try {
            listener(events);
          } catch {
            // A committed journal entry cannot be rolled back because a stream notifier failed.
          }
        }
      return result;
    } catch (error) {
      this.database.exec('ROLLBACK');
      this.transactionEvents = null;
      throw error;
    }
  }

  private timestamp(): string {
    return this.now().toISOString();
  }
}

function toStoredRun(row: RunRow, validateSpecification = false): StoredRun {
  if (!isRunState(row.state) || row.state === 'requested')
    throw new Error(`Persisted run ${row.run_id} has an invalid state.`);
  const parsedSpecification = JSON.parse(row.specification_json) as unknown;
  const specification = validateSpecification
    ? parseRunRequest({
        specificationHash: row.specification_hash,
        specification: parsedSpecification,
      }).specification
    : (parsedSpecification as RunSpecification);
  return {
    runId: row.run_id,
    specificationHash: row.specification_hash,
    specification,
    state: row.state,
    lastEventSequence: row.last_event_sequence,
    acceptedAt: row.accepted_at,
    updatedAt: row.updated_at,
    terminalAt: row.terminal_at,
    cancellationRequestedAt: row.cancellation_requested_at,
    agentThreadId: row.agent_thread_id,
  };
}

function toRunEvent(row: EventRow): RunEvent {
  if (row.state !== null && !isRunState(row.state))
    throw new Error(`Persisted event ${row.run_id}/${row.sequence} has an invalid state.`);
  return {
    protocolVersion: '1.0',
    runId: row.run_id,
    sequence: row.sequence,
    timestamp: row.timestamp,
    type: row.type,
    state: row.state,
    summary: row.summary,
    data: row.data_json === null ? null : (JSON.parse(row.data_json) as unknown),
  };
}

function parseStoredArtifact(json: string): RunArtifact {
  const artifact = JSON.parse(json) as RunArtifact;
  validateArtifact(artifact);
  return artifact;
}

function validateSequence(value: number): void {
  if (!Number.isSafeInteger(value) || value < 0)
    throw new Error('Event replay sequence must be a non-negative integer.');
}

function validatePageLimit(value: number): void {
  if (!Number.isSafeInteger(value) || value <= 0 || value > 500)
    throw new Error('Page limit must be between 1 and 500.');
}

function validateArtifactLocation(runId: string, relativeLocation: string): void {
  const expectedPrefix = `runs/${runId}/artifacts/`;
  if (
    !relativeLocation.startsWith(expectedPrefix) ||
    relativeLocation.startsWith('/') ||
    relativeLocation.includes('\\') ||
    relativeLocation.split('/').some((segment) => segment === '..' || segment.length === 0)
  )
    throw new Error('Artifact locations must remain inside the run artifact directory.');
}

function hasControlCharacters(value: string): boolean {
  return /[\u0000-\u001f\u007f]/.test(value);
}

function prepareDataRoot(dataRoot: string): void {
  const existed = existsSync(dataRoot);
  if (existed) {
    const stats = lstatSync(dataRoot);
    if (stats.isSymbolicLink() || !stats.isDirectory())
      throw new Error('Runner data root must be a real directory.');
    if ((stats.mode & 0o077) !== 0)
      throw new Error('Runner data root must be accessible only by its owner.');
    return;
  }

  mkdirSync(dataRoot, { recursive: true, mode: 0o700 });
  chmodSync(dataRoot, 0o700);
}

function assertSecureFile(path: string, name: string): void {
  const stats = lstatSync(path);
  if (stats.isSymbolicLink() || !stats.isFile()) throw new Error(`${name} must be a regular file.`);
  if ((stats.mode & 0o077) !== 0) throw new Error(`${name} must be accessible only by its owner.`);
}
