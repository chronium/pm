import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  chmodSync,
  copyFileSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { executeCodexWorker } from '../src/codex/worker.js';
import { codexWorkerProtocolVersion, type CodexWorkerRequest } from '../src/codex/protocol.js';
import { computeSpecificationHash } from '../src/protocol/canonical-json.js';
import { createRequest } from './helpers.js';

if (process.env['PM_AGENT_HOST_CODEX_SMOKE'] !== '1')
  throw new Error('Set PM_AGENT_HOST_CODEX_SMOKE=1 to run the credentialed Codex smoke test.');

const authenticationSource = requiredEnvironment('PM_AGENT_HOST_CODEX_SMOKE_AUTH');
const pmCommand = parsePmCommand(requiredEnvironment('PM_AGENT_HOST_PM_COMMAND_JSON'));
const repositoryRoot = resolve(process.cwd(), '..');
const temporaryRoot = mkdtempSync(join(tmpdir(), 'pm-agent-codex-smoke-'));

try {
  const workspace = join(temporaryRoot, 'workspace');
  const codexHome = join(temporaryRoot, 'codex-home');
  const temporaryDirectory = join(temporaryRoot, 'tmp');
  execFileSync('git', ['clone', '--quiet', '--no-hardlinks', repositoryRoot, workspace]);
  mkdirSync(codexHome, { mode: 0o700 });
  mkdirSync(temporaryDirectory, { mode: 0o700 });
  const authenticationTarget = join(codexHome, 'auth.json');
  copyFileSync(authenticationSource, authenticationTarget);
  chmodSync(authenticationTarget, 0o600);

  const taskId = 'SMOKE-0001';
  const taskMarkdown = `---
id: ${taskId}
title: Create the Codex runner smoke marker
track: AGENT
milestone: agent-runs
createdAt: 2026-07-28T00:00:00.0000000Z
modifiedAt: 2026-07-28T00:00:00.0000000Z
---

## Goal

Create a file named \`codex-smoke.txt\` in the repository root containing exactly \`PM Codex smoke passed\` and a trailing newline.

## Constraints

- Do not modify any other file.
- Report the validation result without changing this task's state.
`;
  const taskPath = join(workspace, '.pm', 'tasks', `${taskId}.md`);
  const stateReference = join(workspace, '.pm', 'states', 'todo', `${taskId}.ref`);
  mkdirSync(dirname(stateReference), { recursive: true });
  writeFileSync(taskPath, taskMarkdown);
  writeFileSync(stateReference, `../../tasks/${taskId}.md`);
  execFileSync('git', ['add', '.pm'], { cwd: workspace });
  execFileSync(
    'git',
    [
      '-c',
      'user.name=PM Codex Smoke',
      '-c',
      'user.email=pm-codex-smoke.invalid',
      'commit',
      '--quiet',
      '-m',
      'Add isolated Codex smoke task',
    ],
    { cwd: workspace },
  );

  const runRequest = createRequest('run-codex-smoke');
  runRequest.specification.task.taskId = taskId;
  runRequest.specification.task.title = 'Create the Codex runner smoke marker';
  runRequest.specification.task.revision = sha256(taskMarkdown);
  runRequest.specification.repository.baseCommit = execFileSync('git', ['rev-parse', 'HEAD'], {
    cwd: workspace,
    encoding: 'utf8',
  }).trim();
  runRequest.specification.agent.modelId =
    process.env['PM_AGENT_HOST_CODEX_SMOKE_MODEL'] ?? 'gpt-5.6-sol';
  runRequest.specification.agent.effortId = 'low';
  runRequest.specificationHash = computeSpecificationHash(runRequest.specification);

  const environment = {
    CODEX_HOME: codexHome,
    HOME: codexHome,
    LANG: process.env['LANG'] ?? 'C.UTF-8',
    PATH: process.env['PATH'] ?? '/usr/bin:/bin',
    TMPDIR: temporaryDirectory,
  };
  const workerRequest: CodexWorkerRequest = {
    protocolVersion: codexWorkerProtocolVersion,
    runRequest,
    workspaceDirectory: workspace,
    codexHomeDirectory: codexHome,
    networkAccessEnabled: false,
    pmMcpCommand: pmCommand,
    environmentNames: Object.keys(environment).sort(),
  };
  const eventTypes: string[] = [];
  for await (const event of executeCodexWorker(
    workerRequest,
    new AbortController().signal,
    environment,
  )) {
    eventTypes.push(event.type);
    process.stdout.write(`${event.type}: ${event.summary} ${JSON.stringify(event.data ?? null)}\n`);
  }

  assert.equal(readFileSync(join(workspace, 'codex-smoke.txt'), 'utf8'), 'PM Codex smoke passed\n');
  assert.equal(readFileSync(stateReference, 'utf8'), `../../tasks/${taskId}.md`);
  assert.ok(eventTypes.includes('agent.thread_started'));
  assert.ok(eventTypes.includes('agent.turn_completed'));
  process.stdout.write('Codex smoke test completed successfully.\n');
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}

function parsePmCommand(value: string): { executable: string; arguments: string[] } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(value) as unknown;
  } catch {
    throw new Error('PM_AGENT_HOST_PM_COMMAND_JSON must be a JSON string array.');
  }
  if (
    !Array.isArray(parsed) ||
    parsed.length === 0 ||
    parsed.some((entry) => typeof entry !== 'string' || entry.length === 0)
  )
    throw new Error('PM_AGENT_HOST_PM_COMMAND_JSON must be a non-empty JSON string array.');
  const [executable, ...argumentsValue] = parsed as string[];
  return { executable: executable!, arguments: argumentsValue };
}

function requiredEnvironment(name: string): string {
  const value = process.env[name];
  if (value === undefined || value.length === 0) throw new Error(`${name} is required.`);
  return value;
}

function sha256(value: string): string {
  return createHash('sha256').update(value).digest('hex');
}
