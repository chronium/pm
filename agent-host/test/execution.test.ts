import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { chmodSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import type {
  AgentDriver,
  RuntimeDriver,
  RuntimeHandle,
  RuntimeProcessEvent,
  RuntimeProcessExecutor,
  RuntimeProcessRequest,
} from '../src/drivers.js';
import { ArtifactCollector } from '../src/execution/artifacts.js';
import { RunnerLayout } from '../src/execution/layout.js';
import {
  RepositoryPolicy,
  type RepositoryAccessPolicy,
} from '../src/execution/repository-policy.js';
import { ValidationRunner } from '../src/execution/validation.js';
import { GitWorkspaceService } from '../src/execution/workspace.js';
import { RunStore } from '../src/persistence/run-store.js';
import {
  computeProfileRevision,
  computeSpecificationHash,
} from '../src/protocol/canonical-json.js';
import { DriverRunProcessor, RunScheduler } from '../src/scheduler.js';
import { createRequest, createTempDirectory } from './helpers.js';

test('repository policy is exact, owner-only, and rejects local or credentialed remotes', () => {
  const temporary = createTempDirectory();
  try {
    const path = join(temporary.path, 'repositories.json');
    writeFileSync(
      path,
      JSON.stringify({ repositories: [{ remote: 'https://github.com/chronium/pm.git' }] }),
      {
        mode: 0o600,
      },
    );
    const policy = RepositoryPolicy.load(path);
    policy.assertAllowed('https://github.com/chronium/pm.git');
    assert.throws(
      () => policy.assertAllowed('https://github.com/chronium/other.git'),
      /allowlisted/,
    );

    writeFileSync(
      path,
      JSON.stringify({ repositories: [{ remote: 'https://localhost/repo.git' }] }),
      {
        mode: 0o600,
      },
    );
    assert.throws(() => RepositoryPolicy.load(path), /local remotes/);
    writeFileSync(
      path,
      JSON.stringify({ repositories: [{ remote: 'https://user:secret@example.com/repo.git' }] }),
      {
        mode: 0o600,
      },
    );
    assert.throws(() => RepositoryPolicy.load(path), /credentials/);
    chmodSync(path, 0o644);
    assert.throws(() => RepositoryPolicy.load(path), /owner-only/);
  } finally {
    temporary.dispose();
  }
});

test('workspace preparation materializes exact committed task bytes and isolates Codex auth', async () => {
  const fixture = createGitFixture('run-workspace');
  try {
    const workspace = fixture.workspaceService;
    const prepared = await workspace.prepare(
      fixture.request.specification,
      new AbortController().signal,
    );
    assert.equal(git(['-C', prepared.paths.workspace, 'rev-parse', 'HEAD']).trim(), fixture.commit);
    assert.equal(git(['-C', prepared.paths.workspace, 'remote']).trim(), '');
    assert.equal(
      readFileSync(join(prepared.paths.codexHome, 'auth.json'), 'utf8'),
      '{"tokens":{}}\n',
    );
    await workspace.resetCodexHome(fixture.request.specification.runId);
    assert.equal(existsSync(join(prepared.paths.codexHome, 'auth.json')), false);
    await workspace.cleanup(fixture.request.specification.runId);
    assert.equal(existsSync(prepared.paths.workspace), false);
    assert.equal(existsSync(prepared.mirror), true);
  } finally {
    fixture.dispose();
  }
});

test('workspace preparation rejects task revision drift and unsupported Git features', async () => {
  const revisionFixture = createGitFixture('run-revision');
  try {
    revisionFixture.request.specification.task.revision = '0'.repeat(64);
    revisionFixture.request.specificationHash = computeSpecificationHash(
      revisionFixture.request.specification,
    );
    await assert.rejects(
      revisionFixture.workspaceService.prepare(
        revisionFixture.request.specification,
        new AbortController().signal,
      ),
      /Task revision/,
    );
  } finally {
    revisionFixture.dispose();
  }

  const submoduleFixture = createGitFixture('run-submodule', true);
  try {
    await assert.rejects(
      submoduleFixture.workspaceService.prepare(
        submoduleFixture.request.specification,
        new AbortController().signal,
      ),
      /submodules/,
    );
  } finally {
    submoduleFixture.dispose();
  }

  const authFixture = createGitFixture('run-auth-mode');
  try {
    chmodSync(authFixture.authPath, 0o644);
    await assert.rejects(
      authFixture.workspaceService.prepare(
        authFixture.request.specification,
        new AbortController().signal,
      ),
      /owner-only/,
    );
  } finally {
    authFixture.dispose();
  }
});

test('driver lifecycle uses separate agent and validation runtimes and retains bounded evidence', async () => {
  const fixture = createGitFixture('run-lifecycle');
  const store = new RunStore(fixture.dataRoot);
  try {
    assert.equal(store.acceptRun(fixture.request, 4).disposition, 'new');
    const runtime = new FakeLifecycleRuntime(
      fixture.layout.run(fixture.request.specification.runId),
    );
    const agent: AgentDriver = {
      async *execute() {
        writeFileSync(join(runtime.paths.workspace, 'result.txt'), 'implemented\n');
        yield {
          type: 'agent.message',
          summary: 'Codex response',
          data: { text: 'Finished safely.' },
        };
      },
    };
    const processor = new DriverRunProcessor(store, runtime, agent, {
      workspace: fixture.workspaceService,
      validation: new ValidationRunner(runtime),
      artifacts: new ArtifactCollector(store, fixture.layout),
    });
    const scheduler = new RunScheduler(store, processor, 1);
    scheduler.start();
    await scheduler.waitForIdle();
    await scheduler.stop();

    assert.equal(store.getRun(fixture.request.specification.runId)?.state, 'completed');
    assert.deepEqual(runtime.authAtCreate, [true, false]);
    assert.equal(runtime.destroyCount, 2);
    assert.equal(existsSync(runtime.paths.workspace), false);
    assert.equal(existsSync(runtime.paths.codexHome), false);
    const artifacts = store.listArtifacts(fixture.request.specification.runId);
    assert.ok(artifacts.some((artifact) => artifact.artifactId === 'changes-patch'));
    assert.ok(artifacts.some((artifact) => artifact.artifactId === 'validation'));
    assert.ok(artifacts.some((artifact) => artifact.artifactId === 'agent-response'));
    assert.ok(artifacts.some((artifact) => artifact.artifactId === 'manifest'));
    const validation = JSON.parse(
      readFileSync(join(runtime.paths.artifacts, 'validation.json'), 'utf8'),
    ) as { status: string };
    assert.equal(validation.status, 'failed');
  } finally {
    store.close();
    fixture.dispose();
  }
});

test('artifact collection omits an oversized patch while retaining change metadata', async () => {
  const fixture = createGitFixture('run-bounded-patch');
  const profile = fixture.request.specification.runtime.profile;
  profile.output.maxPatchBytes = 8;
  profile.revision = computeProfileRevision(profile);
  fixture.request.specificationHash = computeSpecificationHash(fixture.request.specification);
  const store = new RunStore(fixture.dataRoot);
  try {
    store.acceptRun(fixture.request, 4);
    const run = store.claimNextRun()!;
    const prepared = await fixture.workspaceService.prepare(
      fixture.request.specification,
      new AbortController().signal,
    );
    for (const state of [
      'starting_runtime',
      'starting_agent',
      'running',
      'validating',
      'collecting_artifacts',
    ] as const)
      store.transition(run.runId, state, state);
    writeFileSync(join(prepared.paths.workspace, 'large.txt'), 'content larger than eight bytes\n');
    const collector = new ArtifactCollector(store, fixture.layout);
    await collector.collect(
      {
        run,
        mirror: prepared.mirror,
        validation: ValidationRunner.skipped(profile.validation),
        agentResponse: null,
        executionStatus: 'succeeded',
        executionError: null,
        startedAt: new Date().toISOString(),
        resourceUsage: { agent: null, validation: null },
      },
      new AbortController().signal,
    );
    assert.equal(store.getArtifact(run.runId, 'changes-patch'), undefined);
    const summary = JSON.parse(
      readFileSync(join(prepared.paths.artifacts, 'changes-summary.json'), 'utf8'),
    ) as { patchExceededLimit: boolean; patchIncluded: boolean };
    assert.equal(summary.patchExceededLimit, true);
    assert.equal(summary.patchIncluded, false);
  } finally {
    store.close();
    fixture.dispose();
  }
});

class FakeLifecycleRuntime implements RuntimeDriver, RuntimeProcessExecutor {
  authAtCreate: boolean[] = [];
  destroyCount = 0;

  constructor(readonly paths: ReturnType<RunnerLayout['run']>) {}

  async create(): Promise<RuntimeHandle> {
    this.authAtCreate.push(existsSync(join(this.paths.codexHome, 'auth.json')));
    return {
      runtimeId: `runtime-${this.authAtCreate.length}`,
      agentContext: {
        workspaceDirectory: '/workspace',
        codexHomeDirectory: '/home/pm/.codex',
        networkAccessEnabled: false,
        workerCommand: { executable: 'pm-agent-worker', arguments: [] },
        pmMcpCommand: { executable: 'pm', arguments: [] },
        environment: { PATH: '/usr/bin' },
      },
    };
  }

  async destroy(): Promise<void> {
    this.destroyCount += 1;
  }

  async *execute(
    _runtime: RuntimeHandle,
    _request: RuntimeProcessRequest,
  ): AsyncIterable<RuntimeProcessEvent> {
    yield { type: 'stdout', chunk: 'validation failed\n' };
    yield { type: 'exit', exitCode: 1, signal: null };
  }
}

function createGitFixture(runId: string, withSubmodules = false) {
  const temporary = createTempDirectory();
  const source = join(temporary.path, 'source');
  const remote = join(temporary.path, 'remote.git');
  const dataRoot = join(temporary.path, 'runner');
  mkdirSync(join(source, '.pm', 'tasks'), { recursive: true });
  git(['init', '--initial-branch=main', source]);
  git(['-C', source, 'config', 'user.name', 'PM Test']);
  git(['-C', source, 'config', 'user.email', 'pm@example.invalid']);
  const taskBytes = Buffer.from('---\nid: AGENT-0008\n---\n\nImplement the runner.\n', 'utf8');
  writeFileSync(join(source, '.pm', 'tasks', 'AGENT-0008.md'), taskBytes);
  writeFileSync(join(source, 'README.md'), '# Fixture\n');
  if (withSubmodules) writeFileSync(join(source, '.gitmodules'), '[submodule "unsafe"]\n');
  git(['-C', source, 'add', '.']);
  git(['-C', source, 'commit', '-m', 'fixture']);
  const commit = git(['-C', source, 'rev-parse', 'HEAD']).trim();
  git(['clone', '--bare', source, remote]);
  const authPath = join(temporary.path, 'auth.json');
  writeFileSync(authPath, '{"tokens":{}}\n', { mode: 0o600 });
  chmodSync(authPath, 0o600);
  const layout = new RunnerLayout(dataRoot);
  const policy: RepositoryAccessPolicy = { assertAllowed: () => undefined };
  const workspaceService = new GitWorkspaceService(layout, policy, authPath, 'git', true);
  const request = createRequest(runId);
  request.specification.repository.remote = remote;
  request.specification.repository.baseCommit = commit;
  request.specification.task.taskId = 'AGENT-0008';
  request.specification.task.revision = createHash('sha256').update(taskBytes).digest('hex');
  request.specificationHash = computeSpecificationHash(request.specification);
  return {
    temporary,
    dataRoot,
    layout,
    workspaceService,
    request,
    commit,
    authPath,
    dispose: temporary.dispose,
  };
}

function git(argumentsValue: readonly string[]): string {
  return execFileSync('git', [...argumentsValue], {
    encoding: 'utf8',
    env: {
      PATH: process.env['PATH'],
      HOME: '/nonexistent',
      GIT_CONFIG_NOSYSTEM: '1',
      GIT_TERMINAL_PROMPT: '0',
    },
  });
}
