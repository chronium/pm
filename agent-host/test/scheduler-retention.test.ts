import assert from 'node:assert/strict';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { JsonLogger } from '../src/logging.js';
import type {
  AgentDriver,
  AgentDriverEvent,
  RuntimeDriver,
  RuntimeHandle,
} from '../src/drivers.js';
import { RunStore, type StoredRun } from '../src/persistence/run-store.js';
import { RetentionService } from '../src/retention.js';
import { DriverRunProcessor, RunScheduler, type RunProcessor } from '../src/scheduler.js';
import { completeRun, createRequest, createTempDirectory, waitUntil } from './helpers.js';

test('scheduler preserves FIFO order and enforces fixed concurrency', async () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    for (const runId of ['run-a', 'run-b', 'run-c']) store.acceptRun(createRequest(runId), 8);

    const started: string[] = [];
    const releases = new Map<string, () => void>();
    let active = 0;
    let maximumActive = 0;
    const processor: RunProcessor = {
      async execute(run: StoredRun): Promise<void> {
        started.push(run.runId);
        active += 1;
        maximumActive = Math.max(maximumActive, active);
        await new Promise<void>((resolve) => releases.set(run.runId, resolve));
        completeRun(store, run.runId);
        active -= 1;
      },
    };

    const scheduler = new RunScheduler(store, processor, 2);
    scheduler.start();
    await waitUntil(() => started.length === 2);
    assert.deepEqual(started, ['run-a', 'run-b']);
    assert.equal(scheduler.activeCount, 2);
    assert.equal(store.queueDepth(), 1);

    releases.get('run-a')?.();
    await waitUntil(() => started.length === 3);
    assert.deepEqual(started, ['run-a', 'run-b', 'run-c']);
    releases.get('run-b')?.();
    releases.get('run-c')?.();
    await scheduler.waitForIdle();
    assert.equal(maximumActive, 2);
    assert.equal(store.getRun('run-c')?.state, 'completed');
    await scheduler.stop();
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('scheduler turns processor failures into safe actionable durable failures', async () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-failure'), 4);
    const scheduler = new RunScheduler(
      store,
      {
        execute(): Promise<void> {
          return Promise.reject(
            new Error('/private/repository?token=super-secret Bearer abcdefghijklmnopqrstuvwxyz'),
          );
        },
      },
      1,
    );
    scheduler.start();
    await scheduler.waitForIdle();
    assert.equal(store.getRun('run-failure')?.state, 'failed');
    const terminal = store.eventsAfter('run-failure').at(-1)!;
    assert.equal(terminal.summary, 'The runner encountered an internal failure.');
    assert.deepEqual((terminal.data as { failure: unknown }).failure, {
      code: 'internal_failure',
      stage: 'system',
      summary: 'The runner encountered an internal failure.',
      recommendedAction: 'Retry once, then inspect private runner logs if the failure repeats.',
      retryable: true,
    });
    assert.doesNotMatch(
      JSON.stringify(terminal),
      /\/private\/repository|super-secret|Bearer|abcdefghijklmnopqrstuvwxyz/,
    );
    await scheduler.stop();
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('scheduler fails a processor that returns without a terminal result', async () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-incomplete'), 4);
    const scheduler = new RunScheduler(store, { execute: async () => undefined }, 1);
    scheduler.start();
    await scheduler.waitForIdle();

    assert.equal(store.getRun('run-incomplete')?.state, 'failed');
    assert.deepEqual(store.eventsAfter('run-incomplete').at(-1)?.data, {
      previousState: 'preparing_workspace',
      nextState: 'failed',
      failure: {
        code: 'internal_failure',
        stage: 'system',
        summary: 'The runner encountered an internal failure.',
        recommendedAction: 'Retry once, then inspect private runner logs if the failure repeats.',
        retryable: true,
      },
    });
    await scheduler.stop();
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('scheduler settles requested active cancellation after the processor stops', async () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-cancelled'), 4);
    let started = false;
    const scheduler = new RunScheduler(
      store,
      {
        execute(_run, signal): Promise<void> {
          started = true;
          return new Promise((resolve) => signal.addEventListener('abort', () => resolve()));
        },
      },
      1,
    );
    scheduler.start();
    await waitUntil(() => started);

    assert.equal(store.requestCancellation('run-cancelled').disposition, 'requested');
    assert.equal(scheduler.cancel('run-cancelled'), true);
    await scheduler.waitForIdle();

    assert.equal(store.getRun('run-cancelled')?.state, 'cancelled');
    assert.equal(
      store.eventsAfter('run-cancelled').filter((event) => event.state === 'cancelled').length,
      1,
    );
    await scheduler.stop();
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('processor completion wins a cancellation race once the run is terminal', async () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-completion-race'), 4);
    let release: (() => void) | undefined;
    const scheduler = new RunScheduler(
      store,
      {
        async execute(run): Promise<void> {
          await new Promise<void>((resolve) => (release = resolve));
          completeRun(store, run.runId);
        },
      },
      1,
    );
    scheduler.start();
    await waitUntil(() => release !== undefined);

    assert.equal(store.requestCancellation('run-completion-race').disposition, 'requested');
    assert.equal(scheduler.cancel('run-completion-race'), true);
    release?.();
    await scheduler.waitForIdle();

    assert.equal(store.getRun('run-completion-race')?.state, 'completed');
    assert.equal(
      store.eventsAfter('run-completion-race').filter((event) => event.state === 'cancelled')
        .length,
      0,
    );
    await scheduler.stop();
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('runtime and agent driver fakes execute through the scheduler seam', async () => {
  const temporary = createTempDirectory();
  try {
    const store = new RunStore(temporary.path);
    store.acceptRun(createRequest('run-drivers'), 4);
    const lifecycle: string[] = [];
    const runtimeHandle: RuntimeHandle = {
      runtimeId: 'fake-runtime',
      agentContext: {
        workspaceDirectory: '/workspace',
        codexHomeDirectory: '/run/codex-home',
        networkAccessEnabled: false,
        workerCommand: { executable: 'node', arguments: ['worker.js'] },
        pmMcpCommand: { executable: 'pm', arguments: [] },
        environment: { CODEX_HOME: '/run/codex-home', PATH: '/usr/bin' },
      },
    };
    const runtimeDriver: RuntimeDriver = {
      async create(): Promise<RuntimeHandle> {
        lifecycle.push('runtime.create');
        return runtimeHandle;
      },
      async destroy(handle, reason): Promise<void> {
        lifecycle.push(`runtime.destroy:${handle.runtimeId}:${reason}`);
      },
    };
    const agentDriver: AgentDriver = {
      async *execute(_specification, runtime): AsyncIterable<AgentDriverEvent> {
        lifecycle.push(`agent.execute:${runtime.runtimeId}`);
        yield {
          type: 'agent.thread_started',
          summary: 'Fake agent started',
          agentThreadId: 'thread-scheduler',
        };
        yield { type: 'agent.message', summary: 'Fake agent response' };
      },
    };
    const scheduler = new RunScheduler(
      store,
      new DriverRunProcessor(store, runtimeDriver, agentDriver),
      1,
    );

    scheduler.start();
    await scheduler.waitForIdle();

    assert.equal(store.getRun('run-drivers')?.state, 'completed');
    assert.deepEqual(lifecycle, [
      'runtime.create',
      'agent.execute:fake-runtime',
      'runtime.destroy:fake-runtime:completed',
    ]);
    assert.equal(
      store.eventsAfter('run-drivers').find((event) => event.type === 'agent.message')?.summary,
      'Fake agent response',
    );
    assert.equal(store.getRun('run-drivers')?.agentThreadId, 'thread-scheduler');
    await scheduler.stop();
    store.close();
  } finally {
    temporary.dispose();
  }
});

test('retention prunes expired terminal runs and owned artifacts only', () => {
  const temporary = createTempDirectory();
  const logs: string[] = [];
  try {
    const completedAt = new Date('2026-06-01T00:00:00.000Z');
    const store = new RunStore(temporary.path, () => completedAt);
    store.acceptRun(createRequest('run-expired'), 4);
    store.claimNextRun();
    completeRun(store, 'run-expired');
    const artifactLocation = 'runs/run-expired/artifacts/result.patch';
    const artifactPath = join(temporary.path, artifactLocation);
    mkdirSync(join(temporary.path, 'runs/run-expired/artifacts'), { recursive: true });
    writeFileSync(artifactPath, 'patch');
    store.recordArtifact(
      'run-expired',
      {
        artifactId: 'patch',
        kind: 'patch',
        fileName: 'result.patch',
        mediaType: 'text/x-diff',
        byteLength: 5,
        sha256: '1'.repeat(64),
        createdAt: completedAt.toISOString(),
      },
      artifactLocation,
    );
    store.acceptRun(createRequest('run-active'), 4);

    const logger = new JsonLogger((line) => logs.push(line));
    const retention = new RetentionService(
      store,
      temporary.path,
      30,
      logger,
      () => new Date('2026-07-15T00:00:00.000Z'),
    );
    assert.equal(retention.prune(), 1);
    assert.equal(store.getRun('run-expired'), undefined);
    assert.equal(existsSync(artifactPath), false);
    assert.equal(store.getRun('run-active')?.state, 'queued');
    assert.match(logs.at(-1) ?? '', /"prunedRuns":1/);

    const disabled = new RetentionService(store, temporary.path, 0, logger);
    assert.equal(disabled.prune(), 0);
    assert.throws(
      () =>
        store.recordArtifact(
          'run-active',
          {
            artifactId: 'escape',
            kind: 'patch',
            fileName: 'escape.patch',
            mediaType: 'text/x-diff',
            byteLength: 0,
            sha256: '2'.repeat(64),
            createdAt: completedAt.toISOString(),
          },
          '../outside',
        ),
      /artifact directory/,
    );
    store.close();
  } finally {
    temporary.dispose();
  }
});
