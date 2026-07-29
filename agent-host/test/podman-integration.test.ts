import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { CommandPodmanProbe } from '../src/oci/podman-probe.js';
import {
  NodePodmanClient,
  PodmanRuntimeDriver,
  type PodmanRuntimeHandle,
} from '../src/oci/podman-runtime.js';
import {
  computeProfileRevision,
  computeSpecificationHash,
} from '../src/protocol/canonical-json.js';
import type { RunRequest } from '../src/protocol/types.js';
import { createRequest, createTempDirectory } from './helpers.js';

const integrationImage = process.env['PM_AGENT_HOST_TEST_IMAGE'];

test(
  'rootless Podman enforces the installed runtime profile on Linux',
  { skip: integrationImage === undefined ? 'PM_AGENT_HOST_TEST_IMAGE is not set' : false },
  async () => {
    const temporary = createTempDirectory();
    const client = new NodePodmanClient();
    const request = integrationRequest('run-podman-integration-offline', 'offline');
    const sentinel = join(temporary.path, `host-sentinel-${process.pid}`);
    writeFileSync(sentinel, 'must-not-be-visible');
    prepareRunDirectories(temporary.path, request.specification.runId);
    const probe = new CommandPodmanProbe();
    const capability = probe.inspect([request.specification.runtime.profile]);
    assert.equal(capability.rootless, true);
    assert.equal(capability.cgroupVersion, 'v2');
    assert.equal(capability.seccompEnabled, true);

    const runtime = new PodmanRuntimeDriver(client, {
      dataRoot: temporary.path,
      runnerId: request.specification.runtime.runnerId,
      minimumFreeDiskBytes: 0,
      diskCheckIntervalMilliseconds: 50,
    });
    let handle: PodmanRuntimeHandle | undefined;
    try {
      assert.equal(await runtime.reconcile(), 0);
      handle = (await runtime.create(
        request.specification,
        new AbortController().signal,
      )) as PodmanRuntimeHandle;
      const inspected = await client.run(['inspect', handle.containerId]);
      assert.equal(inspected.exitCode, 0, inspected.stderr);
      const inventory = JSON.parse(inspected.stdout) as Array<Record<string, unknown>>;
      const hostConfig = inventory[0]?.['HostConfig'] as Record<string, unknown>;
      assert.equal(hostConfig['ReadonlyRootfs'], true);
      assert.equal(hostConfig['PidsLimit'], 64);
      assert.equal(hostConfig['Memory'], 268_435_456);
      assert.equal(hostConfig['NetworkMode'], 'none');
      assert.ok(Array.isArray(hostConfig['CapDrop']) && hostConfig['CapDrop'].length > 0);

      const output = await execute(
        runtime,
        handle,
        [
          'set -eu',
          'test "$(id -u)" != 0',
          'test "$(awk \'/CapEff/ { print $2 }\' /proc/self/status)" = 0000000000000000',
          'test "$(awk \'/NoNewPrivs/ { print $2 }\' /proc/self/status)" = 1',
          'touch /workspace/runtime-write-ok',
          'if touch /etc/runtime-forbidden 2>/dev/null; then exit 20; fi',
          `if test -e ${shellQuote(sentinel)}; then exit 21; fi`,
          'if test -S /run/user/1000/podman/podman.sock; then exit 22; fi',
          'test "$(ls /sys/class/net | wc -l)" -eq 1',
          'printf podman-runtime-ok',
        ].join('; '),
      );
      assert.match(output, /podman-runtime-ok/);
      assert.equal(
        await fileExists(
          join(
            temporary.path,
            'runs',
            request.specification.runId,
            'workspace',
            'runtime-write-ok',
          ),
        ),
        true,
      );

      const completedContainerId = handle.containerId;
      await runtime.destroy(handle, 'completed');
      handle = undefined;
      const exists = await client.run(['container', 'exists', completedContainerId]);
      assert.notEqual(exists.exitCode, 0);

      const openRequest = integrationRequest('run-podman-integration-open', 'open');
      prepareRunDirectories(temporary.path, openRequest.specification.runId);
      handle = (await runtime.create(
        openRequest.specification,
        new AbortController().signal,
      )) as PodmanRuntimeHandle;
      const openOutput = await execute(
        runtime,
        handle,
        'test "$(ls /sys/class/net | wc -l)" -gt 1; printf open-network-ok',
      );
      assert.match(openOutput, /open-network-ok/);

      assert.equal(await runtime.reconcile(), 1);
      handle.monitorController.abort();
      await handle.monitorPromise;
      handle = undefined;
      assert.equal(await runtime.reconcile(), 0);
    } finally {
      if (handle !== undefined) await runtime.destroy(handle, 'failed');
      await runtime.reconcile();
      temporary.dispose();
    }
  },
);

function integrationRequest(runId: string, networkMode: 'offline' | 'open'): RunRequest {
  const request = createRequest(runId);
  const profile = request.specification.runtime.profile;
  profile.imageReference = integrationImage!;
  profile.limits.cpuMillicores = 500;
  profile.limits.memoryBytes = 268_435_456;
  profile.limits.pids = 64;
  profile.limits.diskBytes = 8_388_608;
  profile.limits.timeoutSeconds = 60;
  profile.network.profileId = networkMode === 'offline' ? 'offline' : 'development-open';
  profile.network.mode = networkMode;
  profile.container.temporaryBytes = 4_194_304;
  profile.revision = '';
  profile.revision = computeProfileRevision(profile);
  request.specificationHash = computeSpecificationHash(request.specification);
  return request;
}

async function execute(
  runtime: PodmanRuntimeDriver,
  handle: PodmanRuntimeHandle,
  script: string,
): Promise<string> {
  let output = '';
  let exitCode: number | null = null;
  for await (const event of runtime.execute(
    handle,
    {
      command: { executable: '/bin/sh', arguments: ['-c', script] },
      workingDirectory: '/workspace',
      environment: handle.agentContext.environment,
      standardInput: '',
    },
    new AbortController().signal,
  )) {
    if (event.type === 'stdout') output += event.chunk;
    if (event.type === 'stderr') output += event.chunk;
    if (event.type === 'exit') exitCode = event.exitCode;
  }
  assert.equal(exitCode, 0, output);
  return output;
}

function prepareRunDirectories(dataRoot: string, runId: string): void {
  const runRoot = join(dataRoot, 'runs', runId);
  const workspace = join(runRoot, 'workspace');
  const codexHome = join(runRoot, 'codex-home');
  mkdirSync(workspace, { recursive: true, mode: 0o700 });
  mkdirSync(codexHome, { recursive: true, mode: 0o700 });
  chmodSync(join(dataRoot, 'runs'), 0o700);
  chmodSync(runRoot, 0o700);
  chmodSync(workspace, 0o700);
  chmodSync(codexHome, 0o700);
}

async function fileExists(path: string): Promise<boolean> {
  const { access } = await import('node:fs/promises');
  try {
    await access(path);
    return true;
  } catch {
    return false;
  }
}

function shellQuote(value: string): string {
  return `'${value.replaceAll("'", "'\\''")}'`;
}
