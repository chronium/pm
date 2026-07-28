import assert from 'node:assert/strict';
import { chmodSync, statSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { RunStore } from '../src/persistence/run-store.js';
import { computeSpecificationHash } from '../src/protocol/canonical-json.js';
import { createRequest, createTempDirectory } from './helpers.js';

test('run acceptance is durable, idempotent, immutable, and queue bounded', () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path, () => new Date('2026-07-27T10:00:00.000Z'));
    const request = createRequest('run-one');
    const accepted = store.acceptRun(request, 1);
    assert.equal(accepted.disposition, 'new');
    assert.equal(store.getRun('run-one')?.state, 'queued');
    assert.deepEqual(
      store.eventsAfter('run-one').map((event) => event.sequence),
      [1, 2],
    );

    assert.equal(store.acceptRun(request, 1).disposition, 'existing');
    const conflicting = structuredClone(request);
    conflicting.specification.task.title = 'Conflicting title';
    conflicting.specificationHash = computeSpecificationHash(conflicting.specification);
    assert.deepEqual(store.acceptRun(conflicting, 1), {
      disposition: 'conflict',
      code: 'run_id_conflict',
    });
    assert.deepEqual(store.acceptRun(createRequest('run-two'), 1), {
      disposition: 'queue_full',
      code: 'queue_full',
    });

    const runnerId = store.runnerId;
    store.close();
    const reopened = new RunStore(temporary.path);
    assert.equal(reopened.runnerId, runnerId);
    assert.equal(
      reopened.getRun('run-one')?.specification.task.title,
      request.specification.task.title,
    );
    assert.equal(reopened.queueDepth(), 1);
    reopened.close();
  } finally {
    temporary.dispose();
  }
});

test('agent thread ID is durable, idempotent, and immutable', () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-thread'), 4);

    store.recordAgentThreadId('run-thread', 'thread-123');
    store.recordAgentThreadId('run-thread', 'thread-123');
    assert.equal(store.getRun('run-thread')?.agentThreadId, 'thread-123');
    assert.throws(
      () => store.recordAgentThreadId('run-thread', 'thread-other'),
      /cannot be changed/,
    );
    store.close();

    const reopened = new RunStore(temporary.path);
    assert.equal(reopened.getRun('run-thread')?.agentThreadId, 'thread-123');
    reopened.close();
  } finally {
    temporary.dispose();
  }
});

test('event allocation remains contiguous across database reopen', () => {
  const temporary = createTempDirectory();
  try {
    let store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-events'), 4);
    store.appendEvent('run-events', {
      type: 'runner.message',
      state: 'queued',
      summary: 'First message',
    });
    store.close();

    store = new RunStore(temporary.path);
    const event = store.appendEvent('run-events', {
      type: 'runner.message',
      state: 'queued',
      summary: 'Second message',
    });
    assert.equal(event.sequence, 4);
    assert.deepEqual(
      store.eventsAfter('run-events').map((item) => item.sequence),
      [1, 2, 3, 4],
    );
    assert.deepEqual(
      store.eventsAfter('run-events', 2).map((item) => item.sequence),
      [3, 4],
    );
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('restart recovery preserves queued work and fails interrupted work once', () => {
  const temporary = createTempDirectory();
  try {
    let store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-queued'), 4);
    store.acceptRun(createRequest('run-active'), 4);
    assert.equal(store.claimNextRun()?.runId, 'run-queued');
    store.close();

    store = new RunStore(temporary.path);
    const recovery = store.recover();
    assert.deepEqual(recovery, { queued: 1, failed: 1 });
    assert.equal(store.getRun('run-queued')?.state, 'failed');
    assert.equal(store.getRun('run-active')?.state, 'queued');
    const failedEvents = store
      .eventsAfter('run-queued')
      .filter((event) => event.state === 'failed');
    assert.equal(failedEvents.length, 1);
    assert.deepEqual(failedEvents[0]?.data, {
      previousState: 'preparing_workspace',
      nextState: 'failed',
      reason: 'runner_restarted',
    });

    assert.deepEqual(store.recover(), { queued: 1, failed: 0 });
    assert.equal(
      store.eventsAfter('run-queued').filter((event) => event.state === 'failed').length,
      1,
    );
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('run store creates private files and rejects permissive existing roots', () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    assert.equal(statSync(temporary.path).mode & 0o777, 0o700);
    assert.equal(statSync(join(temporary.path, 'runner.sqlite')).mode & 0o777, 0o600);
    store.close();

    chmodSync(temporary.path, 0o755);
    assert.throws(() => new RunStore(temporary.path), /only by its owner/);
    chmodSync(temporary.path, 0o700);
  } finally {
    temporary.dispose();
  }
});
