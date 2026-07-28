import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { lstat, mkdir, rm } from 'node:fs/promises';
import { join, posix, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import type {
  AgentRuntimeContext,
  RuntimeHandle,
  RuntimeProcessEvent,
  RuntimeProcessExecutor,
  RuntimeProcessRequest,
  RuntimeDriver,
} from '../drivers.js';
import type { RunSpecification, RuntimeProfile } from '../protocol/types.js';
import {
  DiskBudgetExceededError,
  HostDiskBudgetChecker,
  type DiskBudgetChecker,
} from './disk-budget.js';

const runnerLabel = 'io.chronium.pm.runner-id';
const runLabel = 'io.chronium.pm.run-id';
const maximumCommandOutputBytes = 1_048_576;
const runtimeRunTmpfsBytes = 16 * 1024 * 1024;

export interface PodmanCommandResult {
  exitCode: number | null;
  signal: string | null;
  stdout: string;
  stderr: string;
}

export interface PodmanClient {
  run(argumentsValue: readonly string[], signal?: AbortSignal): Promise<PodmanCommandResult>;
  stream(
    argumentsValue: readonly string[],
    standardInput: string,
    signal: AbortSignal,
  ): AsyncIterable<RuntimeProcessEvent>;
}

export interface PodmanRuntimeOptions {
  dataRoot: string;
  runnerId: string;
  minimumFreeDiskBytes: number;
  diskCheckIntervalMilliseconds?: number;
  uid?: number;
  gid?: number;
}

export interface PodmanRuntimeHandle extends RuntimeHandle {
  readonly containerId: string;
  readonly containerName: string;
  readonly hostWorkspaceDirectory: string;
  readonly hostCodexHomeDirectory: string;
  readonly hostRuntimeDirectory: string;
  policyFailure: string | null;
  monitorController: AbortController;
  monitorPromise: Promise<void>;
  destroyed: boolean;
}

export class NodePodmanClient implements PodmanClient {
  constructor(private readonly executable = 'podman') {}

  async run(argumentsValue: readonly string[], signal?: AbortSignal): Promise<PodmanCommandResult> {
    const controller = signal ?? new AbortController().signal;
    let stdout = '';
    let stderr = '';
    let exitCode: number | null = null;
    let exitSignal: string | null = null;
    for await (const event of this.stream(argumentsValue, '', controller)) {
      if (event.type === 'stdout' && stdout.length < maximumCommandOutputBytes)
        stdout += event.chunk.slice(0, maximumCommandOutputBytes - stdout.length);
      else if (event.type === 'stderr' && stderr.length < maximumCommandOutputBytes)
        stderr += event.chunk.slice(0, maximumCommandOutputBytes - stderr.length);
      else if (event.type === 'exit') {
        exitCode = event.exitCode;
        exitSignal = event.signal;
      }
    }
    return { exitCode, signal: exitSignal, stdout, stderr };
  }

  async *stream(
    argumentsValue: readonly string[],
    standardInput: string,
    signal: AbortSignal,
  ): AsyncIterable<RuntimeProcessEvent> {
    const queue = new AsyncEventQueue<RuntimeProcessEvent>();
    const child = spawn(this.executable, [...argumentsValue], {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: safePodmanEnvironment(),
    });
    const cancel = (): void => {
      child.kill('SIGTERM');
    };
    signal.addEventListener('abort', cancel, { once: true });
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (chunk: string) => queue.push({ type: 'stdout', chunk }));
    child.stderr.on('data', (chunk: string) => queue.push({ type: 'stderr', chunk }));
    child.once('error', () => {
      queue.push({ type: 'stderr', chunk: 'Podman process could not be started.' });
      queue.push({ type: 'exit', exitCode: 127, signal: null });
      queue.close();
    });
    child.once('exit', (exitCode, exitSignal) => {
      queue.push({ type: 'exit', exitCode, signal: exitSignal });
      queue.close();
    });
    child.stdin.on('error', () => undefined);
    child.stdin.end(standardInput);
    try {
      for await (const event of queue) yield event;
    } finally {
      signal.removeEventListener('abort', cancel);
      if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
    }
  }
}

export class PodmanRuntimeDriver implements RuntimeDriver, RuntimeProcessExecutor {
  private readonly dataRoot: string;
  private readonly diskCheckIntervalMilliseconds: number;
  private readonly uid: number;
  private readonly gid: number;

  constructor(
    private readonly client: PodmanClient,
    private readonly options: PodmanRuntimeOptions,
    private readonly diskBudget: DiskBudgetChecker = new HostDiskBudgetChecker(),
  ) {
    this.dataRoot = resolve(options.dataRoot);
    this.diskCheckIntervalMilliseconds = options.diskCheckIntervalMilliseconds ?? 1_000;
    this.uid = options.uid ?? process.getuid?.() ?? 1_000;
    this.gid = options.gid ?? process.getgid?.() ?? 1_000;
    if (this.uid === 0 || this.gid === 0)
      throw new Error('Podman runtime host must be unprivileged.');
    if (this.diskCheckIntervalMilliseconds <= 0)
      throw new Error('Disk check interval must be positive.');
  }

  async create(specification: RunSpecification, signal: AbortSignal): Promise<RuntimeHandle> {
    if (signal.aborted) throw abortError();
    const profile = specification.runtime.profile;
    const paths = this.paths(specification.runId, profile);
    await assertDirectoryChain(this.dataRoot, paths.workspace, true);
    await assertDirectoryChain(this.dataRoot, paths.codexHome, true);
    for (const cache of paths.caches)
      await assertDirectoryChain(this.dataRoot, cache.hostPath, false);
    await mkdir(paths.runtime, { recursive: true, mode: 0o700 });
    await this.diskBudget.check(
      [paths.workspace, paths.codexHome],
      profile.limits.diskBytes,
      this.dataRoot,
      this.options.minimumFreeDiskBytes,
    );

    const containerName = containerNameFor(specification.runtime.runnerId, specification.runId);
    const created = await this.client.run(
      buildPodmanCreateArguments(specification, containerName, paths, this.uid, this.gid),
      signal,
    );
    if (created.exitCode !== 0) throw new Error('Podman could not create the runtime container.');
    const containerId = created.stdout.trim();
    if (!/^[0-9a-f]{12,64}$/.test(containerId)) {
      await this.client.run(['rm', '--force', containerName]);
      throw new Error('Podman returned an invalid container ID.');
    }
    const started = await this.client.run(['start', containerId], signal);
    if (started.exitCode !== 0) {
      await this.client.run(['rm', '--force', containerId]);
      throw new Error('Podman could not start the runtime container.');
    }

    const monitorController = new AbortController();
    const handle: PodmanRuntimeHandle = {
      runtimeId: containerId,
      containerId,
      containerName,
      hostWorkspaceDirectory: paths.workspace,
      hostCodexHomeDirectory: paths.codexHome,
      hostRuntimeDirectory: paths.runtime,
      policyFailure: null,
      monitorController,
      monitorPromise: Promise.resolve(),
      destroyed: false,
      agentContext: this.agentContext(profile),
    };
    handle.monitorPromise = this.monitor(handle, profile, monitorController.signal);
    return handle;
  }

  async destroy(
    runtime: RuntimeHandle,
    _reason: 'completed' | 'failed' | 'cancelled',
  ): Promise<void> {
    const handle = podmanHandle(runtime);
    if (handle.destroyed) return;
    handle.destroyed = true;
    handle.monitorController.abort();
    await handle.monitorPromise;
    await this.client.run(['stop', '--time', '5', handle.containerId]);
    await this.client.run(['rm', '--force', handle.containerId]);
    await rm(handle.hostRuntimeDirectory, {
      recursive: true,
      force: true,
    });
  }

  async *execute(
    runtime: RuntimeHandle,
    request: RuntimeProcessRequest,
    signal: AbortSignal,
  ): AsyncIterable<RuntimeProcessEvent> {
    const handle = podmanHandle(runtime);
    if (handle.destroyed) throw new Error('Podman runtime has already been destroyed.');
    const argumentsValue = [
      'exec',
      '--interactive',
      '--workdir',
      request.workingDirectory,
      ...Object.entries(request.environment).flatMap(([name, value]) => [
        '--env',
        `${name}=${value}`,
      ]),
      handle.containerId,
      request.command.executable,
      ...request.command.arguments,
    ];
    for await (const event of this.client.stream(argumentsValue, request.standardInput, signal))
      yield event;
    if (handle.policyFailure !== null)
      yield {
        type: 'stderr',
        chunk: `Runtime policy terminated execution: ${handle.policyFailure}`,
      };
  }

  async reconcile(): Promise<number> {
    const listed = await this.client.run([
      'ps',
      '--all',
      '--filter',
      `label=${runnerLabel}=${this.options.runnerId}`,
      '--format',
      'json',
    ]);
    if (listed.exitCode !== 0) throw new Error('Podman containers could not be reconciled.');
    let containers: unknown;
    try {
      containers = JSON.parse(listed.stdout) as unknown;
    } catch {
      throw new Error('Podman container inventory is invalid.');
    }
    if (!Array.isArray(containers)) throw new Error('Podman container inventory is invalid.');
    let removed = 0;
    for (const entry of containers) {
      if (entry === null || typeof entry !== 'object') continue;
      const record = entry as Record<string, unknown>;
      const id = typeof record['Id'] === 'string' ? record['Id'] : record['ID'];
      if (typeof id !== 'string' || !/^[0-9a-f]{12,64}$/.test(id)) continue;
      const result = await this.client.run(['rm', '--force', id]);
      if (result.exitCode === 0) removed += 1;
    }
    return removed;
  }

  private async monitor(
    handle: PodmanRuntimeHandle,
    profile: RuntimeProfile,
    signal: AbortSignal,
  ): Promise<void> {
    try {
      while (!signal.aborted) {
        await delay(this.diskCheckIntervalMilliseconds, undefined, { signal });
        await this.diskBudget.check(
          [handle.hostWorkspaceDirectory, handle.hostCodexHomeDirectory],
          profile.limits.diskBytes,
          this.dataRoot,
          this.options.minimumFreeDiskBytes,
        );
      }
    } catch (error) {
      if (signal.aborted) return;
      handle.policyFailure =
        error instanceof DiskBudgetExceededError ? error.code : 'runtime_disk_monitor_failed';
      await this.client.run(['stop', '--time', '0', handle.containerId]);
    }
  }

  private paths(runId: string, profile: RuntimeProfile): RuntimeHostPaths {
    const runRoot = join(this.dataRoot, 'runs', runId);
    return {
      runRoot,
      workspace: join(runRoot, 'workspace'),
      codexHome: join(runRoot, 'codex-home'),
      runtime: join(runRoot, 'runtime'),
      caches: profile.container.readOnlyCaches.map((cache) => ({
        ...cache,
        hostPath: join(this.dataRoot, 'caches', profile.profileId, cache.cacheId),
      })),
    };
  }

  private agentContext(profile: RuntimeProfile): AgentRuntimeContext {
    const available = {
      CODEX_HOME: profile.container.codexHomePath,
      HOME: posix.dirname(profile.container.codexHomePath),
      PATH: '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin',
      TMPDIR: profile.container.temporaryPath,
    } as const;
    const environment = Object.fromEntries(
      profile.container.environmentAllowlist.map((name) => [
        name,
        available[name as keyof typeof available],
      ]),
    );
    return {
      workspaceDirectory: profile.container.workspacePath,
      codexHomeDirectory: profile.container.codexHomePath,
      networkAccessEnabled: profile.network.mode === 'open',
      workerCommand: { executable: 'pm-agent-worker', arguments: [] },
      pmMcpCommand: { executable: 'pm', arguments: [] },
      environment,
    };
  }
}

interface RuntimeHostPaths {
  runRoot: string;
  workspace: string;
  codexHome: string;
  runtime: string;
  caches: Array<{ cacheId: string; containerPath: string; hostPath: string }>;
}

export function buildPodmanCreateArguments(
  specification: RunSpecification,
  containerName: string,
  paths: RuntimeHostPaths,
  uid: number,
  gid: number,
): string[] {
  const profile = specification.runtime.profile;
  const environment = {
    CODEX_HOME: profile.container.codexHomePath,
    HOME: posix.dirname(profile.container.codexHomePath),
    PATH: '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin',
    TMPDIR: profile.container.temporaryPath,
  } as const;
  return [
    'create',
    '--name',
    containerName,
    '--label',
    `${runnerLabel}=${specification.runtime.runnerId}`,
    '--label',
    `${runLabel}=${specification.runId}`,
    '--pull=never',
    '--restart=no',
    '--log-driver=none',
    '--http-proxy=false',
    '--unsetenv-all',
    '--hostname=pm-worker',
    '--read-only',
    '--read-only-tmpfs=false',
    '--cap-drop=all',
    '--security-opt=no-new-privileges',
    '--seccomp-policy=default',
    '--userns=keep-id',
    '--user',
    `${uid}:${gid}`,
    '--pid=private',
    '--ipc=private',
    '--uts=private',
    '--cgroupns=private',
    '--network',
    profile.network.mode === 'offline' ? 'none' : 'private',
    '--cpus',
    (profile.limits.cpuMillicores / 1_000).toFixed(3),
    '--memory',
    `${profile.limits.memoryBytes}b`,
    '--memory-swap',
    `${profile.limits.memoryBytes}b`,
    '--pids-limit',
    String(profile.limits.pids),
    '--timeout',
    String(profile.limits.timeoutSeconds),
    '--stop-timeout',
    '5',
    '--shm-size',
    '64m',
    '--tmpfs',
    `${profile.container.temporaryPath}:rw,nodev,nosuid,size=${profile.container.temporaryBytes},mode=1777`,
    '--tmpfs',
    `/run:rw,nodev,nosuid,noexec,size=${runtimeRunTmpfsBytes},mode=755`,
    '--mount',
    bindMount(paths.workspace, profile.container.workspacePath, false),
    '--mount',
    bindMount(paths.codexHome, profile.container.codexHomePath, false),
    ...paths.caches.flatMap((cache) => [
      '--mount',
      bindMount(cache.hostPath, cache.containerPath, true),
    ]),
    ...profile.container.environmentAllowlist.flatMap((name) => [
      '--env',
      `${name}=${environment[name as keyof typeof environment]}`,
    ]),
    profile.imageReference,
    '/bin/sleep',
    'infinity',
  ];
}

function bindMount(source: string, target: string, readOnly: boolean): string {
  if (source.includes(',') || target.includes(','))
    throw new Error('Runtime mount path is invalid.');
  return `type=bind,src=${source},target=${target},${readOnly ? 'ro' : 'rw'}=true`;
}

async function assertDirectoryChain(
  root: string,
  target: string,
  ownerOnlyLeaf: boolean,
): Promise<void> {
  const relative = target.slice(root.length).replace(/^\/+/, '');
  if (target !== root && (!target.startsWith(`${root}/`) || relative.length === 0))
    throw new Error('Runtime directory escapes the runner data root.');
  let current = root;
  const segments = relative.length === 0 ? [] : relative.split('/');
  const paths = [root, ...segments.map((_, index) => join(root, ...segments.slice(0, index + 1)))];
  for (const path of paths) {
    const stats = await lstat(path);
    if (!stats.isDirectory() || stats.isSymbolicLink())
      throw new Error('Runtime directory chain must contain only real directories.');
    const isLeaf = path === target;
    if ((isLeaf && ownerOnlyLeaf && (stats.mode & 0o077) !== 0) || (stats.mode & 0o002) !== 0)
      throw new Error('Runtime directory permissions are too broad.');
    current = path;
  }
  if (current !== target) throw new Error('Runtime directory is invalid.');
}

function containerNameFor(runnerId: string, runId: string): string {
  const runner = createHash('sha256').update(runnerId).digest('hex').slice(0, 10);
  const run = createHash('sha256').update(runId).digest('hex').slice(0, 20);
  return `pm-run-${runner}-${run}`;
}

function podmanHandle(runtime: RuntimeHandle): PodmanRuntimeHandle {
  const candidate = runtime as Partial<PodmanRuntimeHandle>;
  if (typeof candidate.containerId !== 'string' || typeof candidate.containerName !== 'string')
    throw new Error('Runtime handle does not belong to Podman.');
  return candidate as PodmanRuntimeHandle;
}

function safePodmanEnvironment(): NodeJS.ProcessEnv {
  const result: NodeJS.ProcessEnv = {};
  for (const name of ['HOME', 'PATH', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME', 'XDG_RUNTIME_DIR']) {
    const value = process.env[name];
    if (value !== undefined) result[name] = value;
  }
  return result;
}

function abortError(): Error {
  const error = new Error('Podman runtime operation was cancelled.');
  error.name = 'AbortError';
  return error;
}

class AsyncEventQueue<T> implements AsyncIterable<T> {
  private readonly values: T[] = [];
  private readonly waiters: Array<(value: IteratorResult<T>) => void> = [];
  private closed = false;

  push(value: T): void {
    if (this.closed) return;
    const waiter = this.waiters.shift();
    if (waiter === undefined) this.values.push(value);
    else waiter({ done: false, value });
  }

  close(): void {
    if (this.closed) return;
    this.closed = true;
    for (const waiter of this.waiters.splice(0)) waiter({ done: true, value: undefined });
  }

  [Symbol.asyncIterator](): AsyncIterator<T> {
    return {
      next: () => {
        const value = this.values.shift();
        if (value !== undefined) return Promise.resolve({ done: false, value });
        if (this.closed) return Promise.resolve({ done: true, value: undefined });
        return new Promise<IteratorResult<T>>((resolveValue) => this.waiters.push(resolveValue));
      },
    };
  }
}
