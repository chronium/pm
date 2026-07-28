import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import type { IncomingMessage, ServerResponse } from 'node:http';
import test from 'node:test';
import { CapabilityService } from '../src/capabilities.js';
import { EventStreamManager } from '../src/event-stream.js';
import { RunStore } from '../src/persistence/run-store.js';
import { sanitizeEventDraft } from '../src/protocol/event-sanitizer.js';
import type { RunArtifact } from '../src/protocol/types.js';
import { RunCoordinator, type RunExecutionController } from '../src/run-coordinator.js';
import {
  createCapabilityManifest,
  createRequest,
  createRuntimeProbe,
  createTempDirectory,
} from './helpers.js';

class FakeExecutionController implements RunExecutionController {
  notifications = 0;
  cancelled: string[] = [];
  notify(): void {
    this.notifications += 1;
  }
  cancel(runId: string): boolean {
    this.cancelled.push(runId);
    return true;
  }
}

const runtimeProbe = createRuntimeProbe();

test('coordinator validates capabilities and preserves idempotent acceptance', () => {
  const temporary = createTempDirectory();
  const store = new RunStore(temporary.path);
  const capabilities = new CapabilityService(store, createCapabilityManifest(), 1, runtimeProbe);
  const execution = new FakeExecutionController();
  const coordinator = new RunCoordinator(store, capabilities, 2, execution);
  try {
    const request = createRequest('run-command-1', store.runnerId);
    assert.equal(coordinator.start(request).disposition, 'new');
    assert.equal(coordinator.start(request).disposition, 'existing');
    assert.equal(execution.notifications, 1);

    const reducedCapabilities = new CapabilityService(
      store,
      { ...createCapabilityManifest(), agentProviders: [] },
      1,
      runtimeProbe,
    );
    const restartedCoordinator = new RunCoordinator(store, reducedCapabilities, 2, execution);
    assert.equal(restartedCoordinator.start(request).disposition, 'existing');

    const wrongRunner = createRequest('run-command-2', 'runner-elsewhere');
    const mismatch = coordinator.start(wrongRunner);
    assert.equal(mismatch.disposition, 'invalid_capability');
    if (mismatch.disposition === 'invalid_capability')
      assert.equal(mismatch.validation.errorCode, 'runner_mismatch');

    const wrongModel = createRequest('run-command-3', store.runnerId);
    wrongModel.specification.agent.modelId = 'uninstalled-model';
    const modelMismatch = coordinator.start(wrongModel);
    assert.equal(modelMismatch.disposition, 'invalid_capability');
  } finally {
    store.close();
    temporary.dispose();
  }
});

test('active pages, cancellation, artifacts, and after-commit notifications remain bounded', () => {
  const temporary = createTempDirectory();
  const store = new RunStore(temporary.path);
  const capabilities = new CapabilityService(store, createCapabilityManifest(), 1, runtimeProbe);
  const execution = new FakeExecutionController();
  const coordinator = new RunCoordinator(store, capabilities, 8, execution);
  const published: number[][] = [];
  const unsubscribe = store.subscribe((events) =>
    published.push(events.map((event) => event.sequence)),
  );
  try {
    for (const id of ['run-page-a', 'run-page-b', 'run-page-c'])
      assert.equal(coordinator.start(createRequest(id, store.runnerId)).disposition, 'new');
    assert.deepEqual(published, [
      [1, 2],
      [1, 2],
      [1, 2],
    ]);

    const first = store.listActiveRuns(2);
    assert.equal(first.runs.length, 2);
    assert.equal(first.hasMore, true);
    const second = store.listActiveRuns(2, first.nextCursor);
    assert.deepEqual(
      second.runs.map((run) => run.runId),
      ['run-page-c'],
    );

    const queued = coordinator.cancel('run-page-a');
    assert.equal(queued.disposition, 'cancelled');
    assert.equal(store.getRun('run-page-a')?.state, 'cancelled');
    assert.deepEqual(
      store.eventsAfter('run-page-a').map((event) => event.sequence),
      [1, 2, 3, 4],
    );
    assert.equal(coordinator.cancel('run-page-a').disposition, 'terminal');

    const active = store.claimNextRun();
    assert.equal(active?.runId, 'run-page-b');
    assert.equal(coordinator.cancel('run-page-b').disposition, 'requested');
    assert.deepEqual(execution.cancelled, ['run-page-b']);
    assert.equal(coordinator.cancel('run-page-b').disposition, 'already_requested');

    const artifact: RunArtifact = {
      artifactId: 'artifact-patch',
      kind: 'git_patch',
      fileName: 'changes.patch',
      mediaType: 'text/x-diff',
      byteLength: 42,
      sha256: 'c'.repeat(64),
      createdAt: '2026-07-27T10:15:00.000Z',
    };
    store.recordArtifact('run-page-b', artifact, 'runs/run-page-b/artifacts/changes.patch');
    assert.deepEqual(store.listArtifacts('run-page-b'), [artifact]);
    assert.deepEqual(store.getArtifact('run-page-b', artifact.artifactId), artifact);

    const eventPage = store.eventPage('run-page-b', 0, 2);
    assert.equal(eventPage.events.length, 2);
    assert.equal(eventPage.hasMore, true);
    assert.equal(eventPage.nextAfterSequence, 2);
  } finally {
    unsubscribe();
    store.close();
    temporary.dispose();
  }
});

test('event sanitizer strips terminal controls and redacts bounded nested payloads', () => {
  const cyclic: Record<string, unknown> = {};
  cyclic['self'] = cyclic;
  const nested: Record<string, unknown> = { value: 'deep' };
  let current = nested;
  for (let depth = 0; depth < 12; depth += 1) {
    const next: Record<string, unknown> = {};
    current['next'] = next;
    current = next;
  }
  const sanitized = sanitizeEventDraft('command.output', '\u001b[31mCommand\u001b[0m completed', {
    authorization: 'Bearer visible-secret',
    output: 'token sk_abcdefghijklmnopqrstuvwxyz and\u0000 control',
    privateKey: '-----BEGIN PRIVATE KEY-----\nsecret\n-----END PRIVATE KEY-----',
    cyclic,
    nested,
  });
  const json = JSON.stringify(sanitized);
  assert.equal(sanitized.summary, 'Command completed');
  assert.doesNotMatch(
    json,
    /visible-secret|abcdefghijklmnopqrstuvwxyz|BEGIN PRIVATE|\u001b|\\u0000/,
  );
  assert.match(json, /REDACTED/);
  assert.match(json, /CIRCULAR|MAX_DEPTH/);

  const oversized = sanitizeEventDraft(
    'agent.message',
    'Large output',
    Array.from({ length: 100 }, () => 'x'.repeat(16_000)),
  );
  assert.deepEqual(oversized.data, { redacted: true, reason: 'event_payload_too_large' });
});

test('event journal rejects types outside protocol namespaces', () => {
  const temporary = createTempDirectory();
  const store = new RunStore(temporary.path);
  try {
    store.acceptRun(createRequest('run-event-type'), 4);
    assert.throws(
      () =>
        store.appendEvent('run-event-type', {
          type: 'custom.output',
          state: 'queued',
          summary: 'Unversioned custom output',
        }),
      /event is invalid/,
    );
    assert.equal(store.getRun('run-event-type')?.lastEventSequence, 2);
  } finally {
    store.close();
    temporary.dispose();
  }
});

test('event streams disconnect persistently backpressured clients without changing run state', async () => {
  const temporary = createTempDirectory();
  const store = new RunStore(temporary.path);
  const streams = new EventStreamManager(store, 1, 1, 5);
  try {
    store.acceptRun(createRequest('run-backpressure'), 4);
    const request = new EventEmitter() as IncomingMessage;
    const response = new BackpressuredResponse();

    await streams.stream('run-backpressure', 2, request, response as unknown as ServerResponse);

    assert.equal(response.destroyed, true);
    assert.equal(store.getRun('run-backpressure')?.state, 'queued');
  } finally {
    streams.close();
    store.close();
    temporary.dispose();
  }
});

class BackpressuredResponse extends EventEmitter {
  statusCode = 0;
  writableEnded = false;
  destroyed = false;
  setHeader(): void {}
  flushHeaders(): void {}
  write(): boolean {
    return false;
  }
  end(): void {
    this.writableEnded = true;
  }
  destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.emit('close');
  }
}
