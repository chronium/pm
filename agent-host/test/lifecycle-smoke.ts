import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { chmodSync, existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { CodexAgentDriver } from '../src/codex/agent-driver.js';
import { ArtifactCollector } from '../src/execution/artifacts.js';
import { RunnerLayout } from '../src/execution/layout.js';
import { RepositoryPolicy } from '../src/execution/repository-policy.js';
import { ValidationRunner } from '../src/execution/validation.js';
import { GitWorkspaceService } from '../src/execution/workspace.js';
import { NodePodmanClient, PodmanRuntimeDriver } from '../src/oci/podman-runtime.js';
import { RunStore } from '../src/persistence/run-store.js';
import {
  computeProfileRevision,
  computeSpecificationHash,
} from '../src/protocol/canonical-json.js';
import { DriverRunProcessor, RunScheduler } from '../src/scheduler.js';
import { createRequest, createTempDirectory } from './helpers.js';

if (process.env['PM_AGENT_HOST_LIFECYCLE_SMOKE'] !== '1')
  throw new Error('Set PM_AGENT_HOST_LIFECYCLE_SMOKE=1 to run the credentialed lifecycle smoke.');

const remote = requiredEnvironment('PM_AGENT_HOST_LIFECYCLE_SMOKE_REMOTE');
const baseCommit = requiredEnvironment('PM_AGENT_HOST_LIFECYCLE_SMOKE_COMMIT');
const authenticationSource = requiredEnvironment('PM_AGENT_HOST_LIFECYCLE_SMOKE_AUTH');
const imageReference = requiredEnvironment('PM_AGENT_HOST_LIFECYCLE_SMOKE_IMAGE');
const taskId = process.env['PM_AGENT_HOST_LIFECYCLE_SMOKE_TASK_ID'] ?? 'SMOKE-0001';
const temporary = createTempDirectory();
const inspection = createTempDirectory();
let store: RunStore | undefined;
let scheduler: RunScheduler | undefined;

try {
  execFileSync('git', ['clone', '--quiet', '--no-checkout', remote, inspection.path], {
    env: hostGitEnvironment(),
  });
  const taskBytes = execFileSync(
    'git',
    ['-C', inspection.path, 'show', `${baseCommit}:.pm/tasks/${taskId}.md`],
    { encoding: 'buffer', env: hostGitEnvironment() },
  );
  const policyPath = join(temporary.path, 'repositories.json');
  writeFileSync(policyPath, JSON.stringify({ repositories: [{ remote }] }), { mode: 0o600 });
  chmodSync(policyPath, 0o600);
  const authPath = join(temporary.path, 'codex-auth.json');
  writeFileSync(authPath, readFileSync(authenticationSource), { mode: 0o600 });
  chmodSync(authPath, 0o600);

  store = new RunStore(join(temporary.path, 'runner'));
  const request = createRequest('run-real-lifecycle-smoke', store.runnerId);
  request.specification.project = { projectId: 'pm-agent-smoke', name: 'PM Agent Smoke' };
  request.specification.task = {
    taskId,
    title: 'Create the isolated runner marker',
    revision: createHash('sha256').update(taskBytes).digest('hex'),
  };
  request.specification.repository = { remote, baseCommit };
  request.specification.agent.modelId =
    process.env['PM_AGENT_HOST_LIFECYCLE_SMOKE_MODEL'] ?? 'gpt-5.6-sol';
  request.specification.agent.effortId = 'medium';
  const profile = request.specification.runtime.profile;
  profile.imageReference = imageReference;
  profile.limits.cpuMillicores = 2_000;
  profile.limits.memoryBytes = 4_294_967_296;
  profile.limits.pids = 512;
  profile.limits.diskBytes = 2_147_483_648;
  profile.limits.timeoutSeconds = 900;
  profile.network = { profileId: 'development-open', mode: 'open' };
  profile.container.temporaryBytes = 536_870_912;
  profile.validation = [
    {
      stepId: 'marker-exists',
      displayName: 'Marker exists',
      executable: '/usr/bin/test',
      arguments: ['-f', 'runner-smoke.txt'],
      workingDirectory: '.',
      timeoutSeconds: 10,
    },
    {
      stepId: 'marker-content',
      displayName: 'Marker content',
      executable: '/usr/bin/grep',
      arguments: ['-Fx', 'PM runner smoke passed', 'runner-smoke.txt'],
      workingDirectory: '.',
      timeoutSeconds: 10,
    },
  ];
  profile.revision = '';
  profile.revision = computeProfileRevision(profile);
  request.specificationHash = computeSpecificationHash(request.specification);

  const layout = new RunnerLayout(store.dataRoot);
  const workspace = new GitWorkspaceService(layout, RepositoryPolicy.load(policyPath), authPath);
  const runtime = new PodmanRuntimeDriver(new NodePodmanClient(), {
    dataRoot: store.dataRoot,
    runnerId: store.runnerId,
    minimumFreeDiskBytes: 1_073_741_824,
  });
  await runtime.reconcile();
  const processor = new DriverRunProcessor(store, runtime, new CodexAgentDriver(runtime), {
    workspace,
    validation: new ValidationRunner(runtime),
    artifacts: new ArtifactCollector(store, layout),
  });
  scheduler = new RunScheduler(store, processor, 1);
  const unsubscribe = store.subscribe((events) => {
    for (const event of events)
      process.stdout.write(`${event.sequence} ${event.type}: ${event.summary}\n`);
  });
  assert.equal(store.acceptRun(request, 1).disposition, 'new');
  scheduler.start();
  await scheduler.waitForIdle();
  unsubscribe();

  const run = store.getRun(request.specification.runId);
  assert.equal(run?.state, 'completed');
  const runPaths = layout.run(request.specification.runId);
  assert.equal(existsSync(runPaths.workspace), false);
  assert.equal(existsSync(runPaths.codexHome), false);
  assert.equal(existsSync(runPaths.runtime), false);
  const summary = JSON.parse(
    readFileSync(join(runPaths.artifacts, 'changes-summary.json'), 'utf8'),
  ) as {
    changedPaths: Array<{ status: string; path: string }>;
  };
  assert.deepEqual(summary.changedPaths, [{ status: 'A', path: 'runner-smoke.txt' }]);
  assert.match(
    readFileSync(join(runPaths.artifacts, 'changes.patch'), 'utf8'),
    /runner-smoke\.txt/,
  );
  const validation = JSON.parse(
    readFileSync(join(runPaths.artifacts, 'validation.json'), 'utf8'),
  ) as {
    status: string;
  };
  assert.equal(validation.status, 'passed');
  process.stdout.write('Runner lifecycle smoke completed successfully.\n');
} finally {
  if (scheduler !== undefined) await scheduler.stop();
  store?.close();
  inspection.dispose();
  temporary.dispose();
}

function requiredEnvironment(name: string): string {
  const value = process.env[name];
  if (value === undefined || value.length === 0) throw new Error(`${name} is required.`);
  return value;
}

function hostGitEnvironment(): NodeJS.ProcessEnv {
  return {
    PATH: process.env['PATH'],
    HOME: process.env['HOME'],
    GIT_TERMINAL_PROMPT: '0',
  };
}
