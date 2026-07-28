import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, readFileSync, symlinkSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import {
  DiskBudgetExceededError,
  HostDiskBudgetChecker,
  type DiskBudgetChecker,
} from '../src/oci/disk-budget.js';
import { CommandPodmanProbe, type PodmanProbeCommand } from '../src/oci/podman-probe.js';
import {
  PodmanRuntimeDriver,
  type PodmanClient,
  type PodmanCommandResult,
} from '../src/oci/podman-runtime.js';
import { parseRuntimeProfile } from '../src/protocol/validation.js';
import type { RuntimeProcessEvent } from '../src/drivers.js';
import { createRequest, createTempDirectory } from './helpers.js';

const containerId = 'a'.repeat(64);

test('runtime profile rejects mutable images and weakened or unsafe policies', () => {
  const profile = rawProfile();
  profile['imageReference'] = 'docker.io/library/alpine:latest';
  assert.throws(() => parseRuntimeProfile(profile), /pinned by a SHA-256 digest/);

  const weakened = rawProfile();
  ((weakened['container'] as Record<string, unknown>)['security'] as Record<string, unknown>)[
    'noNewPrivileges'
  ] = false;
  assert.throws(() => parseRuntimeProfile(weakened), /cannot weaken/);

  const sensitive = rawProfile();
  (sensitive['container'] as Record<string, unknown>)['environmentAllowlist'] = ['OPENAI_API_KEY'];
  assert.throws(() => parseRuntimeProfile(sensitive), /invalid or sensitive/);

  const overlapping = rawProfile();
  (overlapping['container'] as Record<string, unknown>)['temporaryPath'] = '/workspace/tmp';
  assert.throws(() => parseRuntimeProfile(overlapping), /must not overlap/);
});

test('Podman probe requires rootless cgroup v2, seccomp, no SELinux, and installed images', () => {
  const command = new FakeProbeCommand();
  const probe = new CommandPodmanProbe(command, () => 'linux');
  const runtime = probe.inspect([createRequest('run-probe').specification.runtime.profile]);
  assert.equal(runtime.engineId, 'podman');
  assert.equal(runtime.version, '6.0.1');
  assert.equal(runtime.rootless, true);
  assert.deepEqual(command.calls.at(-1), [
    'image',
    'exists',
    createRequest('run-probe').specification.runtime.profile.imageReference,
  ]);

  command.selinuxEnabled = true;
  assert.throws(() => probe.inspect([]), /SELinux runtime labeling is deferred/);
  command.selinuxEnabled = false;
  command.imageAvailable = false;
  assert.throws(
    () => probe.inspect([createRequest('run-missing').specification.runtime.profile]),
    /image for profile .* is unavailable/,
  );
});

test('Podman runtime creates a fixed hardened container and executes through podman exec', async () => {
  const temporary = createTempDirectory();
  const request = createRequest('run-podman-unit');
  const paths = prepareRunDirectories(temporary.path, request.specification.runId);
  const client = new FakePodmanClient();
  const disk = new PassingDiskBudget();
  const runtime = new PodmanRuntimeDriver(
    client,
    {
      dataRoot: temporary.path,
      runnerId: request.specification.runtime.runnerId,
      minimumFreeDiskBytes: 1024,
      diskCheckIntervalMilliseconds: 60_000,
      uid: 1000,
      gid: 1000,
    },
    disk,
  );
  try {
    const handle = await runtime.create(request.specification, new AbortController().signal);
    const create = client.runCalls[0]!;
    assert.equal(create[0], 'create');
    for (const flag of [
      '--read-only',
      '--read-only-tmpfs=false',
      '--cap-drop=all',
      '--security-opt=no-new-privileges',
      '--seccomp-policy=default',
      '--userns=keep-id',
      '--pid=private',
      '--ipc=private',
      '--uts=private',
      '--cgroupns=private',
      '--pull=never',
    ])
      assert.ok(create.includes(flag), `missing ${flag}`);
    assert.ok(create.includes('none') === false);
    assert.ok(create.includes('private'));
    assert.ok(create.some((value) => value.includes(`src=${paths.workspace},target=/workspace`)));
    assert.ok(
      create.some((value) => value.includes(`src=${paths.codexHome},target=/home/pm/.codex`)),
    );
    assert.ok(create.every((value) => !value.includes(process.env['HOME'] ?? '/unavailable-home')));
    assert.ok(create.every((value) => !value.includes('privileged')));
    assert.deepEqual(handle.agentContext.environment, {
      CODEX_HOME: '/home/pm/.codex',
      HOME: '/home/pm',
      PATH: '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin',
    });

    const events: RuntimeProcessEvent[] = [];
    for await (const event of runtime.execute(
      handle,
      {
        command: { executable: '/bin/echo', arguments: ['ok'] },
        workingDirectory: '/workspace',
        environment: handle.agentContext.environment,
        standardInput: 'input',
      },
      new AbortController().signal,
    ))
      events.push(event);
    assert.deepEqual(events, [
      { type: 'stdout', chunk: 'ok\n' },
      { type: 'exit', exitCode: 0, signal: null },
    ]);
    const execution = client.streamCalls[0]!;
    assert.equal(execution.argumentsValue[0], 'exec');
    assert.ok(execution.argumentsValue.includes(containerId));
    assert.equal(execution.standardInput, 'input');

    await runtime.destroy(handle, 'completed');
    await runtime.destroy(handle, 'completed');
    assert.equal(client.runCalls.filter((call) => call[0] === 'rm').length, 1);
  } finally {
    temporary.dispose();
  }
});

test('disk watchdog stops an over-budget runtime and reconciliation removes owned containers', async () => {
  const temporary = createTempDirectory();
  const request = createRequest('run-podman-budget');
  prepareRunDirectories(temporary.path, request.specification.runId);
  const client = new FakePodmanClient();
  client.inventory = [{ Id: 'b'.repeat(64) }, { ID: 'c'.repeat(64) }];
  const disk = new FailingDiskBudget();
  const runtime = new PodmanRuntimeDriver(
    client,
    {
      dataRoot: temporary.path,
      runnerId: request.specification.runtime.runnerId,
      minimumFreeDiskBytes: 1024,
      diskCheckIntervalMilliseconds: 5,
      uid: 1000,
      gid: 1000,
    },
    disk,
  );
  try {
    const handle = await runtime.create(request.specification, new AbortController().signal);
    await waitUntil(() =>
      client.runCalls.some((call) => call.join(' ') === `stop --time 0 ${containerId}`),
    );
    assert.equal(
      (handle as { policyFailure?: string }).policyFailure,
      'runtime_disk_limit_exceeded',
    );
    assert.equal(await runtime.reconcile(), 2);
    assert.ok(client.runCalls.some((call) => call.join(' ') === `rm --force ${'b'.repeat(64)}`));
    await runtime.destroy(handle, 'failed');
  } finally {
    temporary.dispose();
  }
});

test('disk budget counts writable trees without following symlinks and enforces reserve', async () => {
  const temporary = createTempDirectory();
  const outside = createTempDirectory();
  try {
    writeFileSync(join(temporary.path, 'small'), '12345678');
    writeFileSync(join(outside.path, 'large'), 'x'.repeat(64 * 1024));
    symlinkSync(join(outside.path, 'large'), join(temporary.path, 'link'));
    const checker = new HostDiskBudgetChecker();
    const usage = await checker.check([temporary.path], 1024, temporary.path, 0);
    assert.ok(usage.bytes < 1024);
    await assert.rejects(
      checker.check([temporary.path], 1, temporary.path, 0),
      (error: unknown) =>
        error instanceof DiskBudgetExceededError && error.code === 'runtime_disk_limit_exceeded',
    );
    await assert.rejects(
      checker.check([temporary.path], 1024, temporary.path, Number.MAX_SAFE_INTEGER),
      (error: unknown) =>
        error instanceof DiskBudgetExceededError && error.code === 'runner_disk_reserve_reached',
    );
  } finally {
    temporary.dispose();
    outside.dispose();
  }
});

class FakeProbeCommand implements PodmanProbeCommand {
  calls: string[][] = [];
  selinuxEnabled = false;
  imageAvailable = true;

  run(argumentsValue: readonly string[]): { status: number; stdout: string } {
    this.calls.push([...argumentsValue]);
    if (argumentsValue[0] === 'version')
      return { status: 0, stdout: JSON.stringify({ Client: { Version: '6.0.1' } }) };
    if (argumentsValue[0] === 'info')
      return {
        status: 0,
        stdout: JSON.stringify({
          host: {
            cgroupVersion: 'v2',
            cgroupManager: 'systemd',
            security: {
              rootless: true,
              seccompEnabled: true,
              selinuxEnabled: this.selinuxEnabled,
              apparmorEnabled: false,
            },
          },
        }),
      };
    return { status: this.imageAvailable ? 0 : 1, stdout: '' };
  }
}

class FakePodmanClient implements PodmanClient {
  runCalls: string[][] = [];
  streamCalls: Array<{ argumentsValue: string[]; standardInput: string }> = [];
  inventory: unknown[] = [];

  async run(argumentsValue: readonly string[]): Promise<PodmanCommandResult> {
    this.runCalls.push([...argumentsValue]);
    if (argumentsValue[0] === 'create')
      return { exitCode: 0, signal: null, stdout: `${containerId}\n`, stderr: '' };
    if (argumentsValue[0] === 'ps')
      return {
        exitCode: 0,
        signal: null,
        stdout: JSON.stringify(this.inventory),
        stderr: '',
      };
    return { exitCode: 0, signal: null, stdout: '', stderr: '' };
  }

  async *stream(
    argumentsValue: readonly string[],
    standardInput: string,
  ): AsyncIterable<RuntimeProcessEvent> {
    this.streamCalls.push({ argumentsValue: [...argumentsValue], standardInput });
    yield { type: 'stdout', chunk: 'ok\n' };
    yield { type: 'exit', exitCode: 0, signal: null };
  }
}

class PassingDiskBudget implements DiskBudgetChecker {
  async check(): Promise<{ bytes: number; entries: number; freeBytes: number }> {
    return { bytes: 0, entries: 0, freeBytes: 1024 * 1024 };
  }
}

class FailingDiskBudget implements DiskBudgetChecker {
  calls = 0;
  async check(): Promise<{ bytes: number; entries: number; freeBytes: number }> {
    this.calls += 1;
    if (this.calls > 1) throw new DiskBudgetExceededError('runtime_disk_limit_exceeded');
    return { bytes: 0, entries: 0, freeBytes: 1024 * 1024 };
  }
}

function prepareRunDirectories(dataRoot: string, runId: string) {
  const runRoot = join(dataRoot, 'runs', runId);
  const workspace = join(runRoot, 'workspace');
  const codexHome = join(runRoot, 'codex-home');
  mkdirSync(workspace, { recursive: true, mode: 0o700 });
  mkdirSync(codexHome, { recursive: true, mode: 0o700 });
  chmodSync(join(dataRoot, 'runs'), 0o700);
  chmodSync(runRoot, 0o700);
  chmodSync(workspace, 0o700);
  chmodSync(codexHome, 0o700);
  return { workspace, codexHome };
}

function rawProfile(): Record<string, unknown> {
  const fixture = JSON.parse(
    readFileSync(
      join(process.cwd(), '..', 'contracts/agent-runs/v1/runner-capabilities.json'),
      'utf8',
    ),
  ) as { runtimeProfiles: Record<string, unknown>[] };
  return structuredClone(fixture.runtimeProfiles[0]!);
}

async function waitUntil(predicate: () => boolean): Promise<void> {
  const deadline = Date.now() + 2_000;
  while (!predicate()) {
    if (Date.now() > deadline) throw new Error('Timed out waiting for Podman runtime action.');
    await new Promise<void>((resolveValue) => setTimeout(resolveValue, 5));
  }
}
