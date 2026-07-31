import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { chmod, lstat, mkdir, open, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import type { RunSpecification } from '../protocol/types.js';
import { failRun, RunFailureError } from '../run-failure.js';
import type { RunPaths, RunnerLayout } from './layout.js';
import type { RepositoryAccessPolicy } from './repository-policy.js';

const maximumCommandOutputBytes = 4 * 1024 * 1024;
const maximumAuthBytes = 1024 * 1024;
const taskIdPattern = /^[A-Za-z0-9][A-Za-z0-9_-]{0,127}$/;

export interface PreparedWorkspace {
  readonly paths: RunPaths;
  readonly mirror: string;
  readonly linkedContexts: PreparedLinkedContext[];
}

export interface PreparedLinkedContext {
  readonly projectId: string;
  readonly name: string;
  readonly alias: string;
  readonly revision: string;
  readonly requirement: 'required' | 'optional';
  readonly status: 'available' | 'unavailable';
  readonly summary: string;
}

export interface RepositoryPreflightCheck {
  readonly id: string;
  readonly label: string;
  readonly status: 'passed' | 'failed' | 'skipped';
  readonly summary: string;
}

export interface RepositoryPreflightResult {
  readonly ready: boolean;
  readonly checks: RepositoryPreflightCheck[];
}

export class GitWorkspaceService {
  private readonly mirrorLocks = new Map<string, Promise<void>>();

  constructor(
    private readonly layout: RunnerLayout,
    private readonly policy: RepositoryAccessPolicy,
    private readonly codexAuthPath: string,
    private readonly gitExecutable = 'git',
    private readonly allowFileRemotes = false,
  ) {}

  async reconcile(): Promise<number> {
    let entries;
    try {
      entries = await readdir(this.layout.runsRoot, { withFileTypes: true });
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return 0;
      throw error;
    }
    const runIds = entries.filter((entry) => entry.isDirectory()).map((entry) => entry.name);
    await Promise.all(runIds.map((runId) => this.cleanup(runId)));
    return runIds.length;
  }

  async preflight(
    specification: RunSpecification,
    signal: AbortSignal,
  ): Promise<RepositoryPreflightResult> {
    const checks: RepositoryPreflightCheck[] = [];
    const repositories = [
      {
        id: 'primary_repository',
        label: 'Primary repository',
        remote: specification.repository.remote,
        commit: specification.repository.baseCommit,
        required: true,
      },
      ...(specification.linkedContexts ?? []).map((context) => ({
        id: `linked_context_${context.projectId}`,
        label: `Linked wiki context ${context.alias}`,
        remote: context.repository.remote,
        commit: context.repository.baseCommit,
        required: context.requirement === 'required',
      })),
    ];

    for (const repository of repositories) {
      try {
        this.policy.assertAllowed(repository.remote);
        const mirror = this.layout.mirror(repository.remote);
        await this.withMirrorLock(mirror, async () => {
          await this.prepareMirror(mirror, repository.remote, signal);
          await this.assertCommit(mirror, repository.commit, signal);
        });
        checks.push({
          id: repository.id,
          label: repository.label,
          status: 'passed',
          summary: `Exact commit ${repository.commit.slice(0, 12)} is allowlisted and available.`,
        });
      } catch (error) {
        if (error instanceof Error && error.name === 'AbortError') throw error;
        checks.push({
          id: repository.id,
          label: repository.label,
          status: repository.required ? 'failed' : 'skipped',
          summary: repository.required
            ? 'The exact repository revision is not allowlisted or available to this runner.'
            : 'Optional linked wiki context is unavailable and will be omitted.',
        });
      }
    }

    return { ready: checks.every((check) => check.status !== 'failed'), checks };
  }

  async prepare(specification: RunSpecification, signal: AbortSignal): Promise<PreparedWorkspace> {
    try {
      this.policy.assertAllowed(specification.repository.remote);
    } catch {
      throw failRun('repository_not_allowed');
    }
    if (!taskIdPattern.test(specification.task.taskId))
      throw failRun('workspace_policy_unsupported');
    const paths = this.layout.run(specification.runId);
    await this.prepareRunDirectories(paths);
    const mirror = this.layout.mirror(specification.repository.remote);
    await this.withMirrorLock(mirror, async () => {
      await this.prepareMirror(mirror, specification.repository.remote, signal);
      await this.assertCommit(mirror, specification.repository.baseCommit, signal);
      await this.materialize(mirror, paths.workspace, specification.repository.baseCommit, signal);
    });
    await this.assertUnsupportedFeaturesAbsent(paths.workspace, signal);
    try {
      await this.verifyTaskRevision(specification, paths.workspace);
    } catch (error) {
      if (error instanceof RunFailureError) throw error;
      throw failRun('task_revision_mismatch');
    }
    await this.stageCodexAuth(paths.codexHome);
    const linkedContexts = await this.prepareLinkedContexts(specification, paths, signal);
    return { paths, mirror, linkedContexts };
  }

  async resetCodexHome(runId: string): Promise<void> {
    const path = this.layout.run(runId).codexHome;
    await rm(path, { recursive: true, force: true });
    await mkdir(path, { recursive: true, mode: 0o700 });
    await chmod(path, 0o700);
  }

  async cleanup(runId: string): Promise<void> {
    const paths = this.layout.run(runId);
    for (const path of [
      paths.workspace,
      paths.codexHome,
      paths.runtime,
      paths.scratch,
      paths.contexts,
    ])
      await rm(path, { recursive: true, force: true });
  }

  private async prepareRunDirectories(paths: RunPaths): Promise<void> {
    await ensureOwnerDirectory(this.layout.dataRoot);
    await ensureOwnerDirectory(this.layout.runsRoot);
    await ensureOwnerDirectory(paths.runRoot);
    for (const path of [
      paths.workspace,
      paths.codexHome,
      paths.artifacts,
      paths.scratch,
      paths.contexts,
    ]) {
      await rm(path, { recursive: true, force: true });
      await mkdir(path, { recursive: true, mode: 0o700 });
      await chmod(path, 0o700);
    }
  }

  private async prepareLinkedContexts(
    specification: RunSpecification,
    paths: RunPaths,
    signal: AbortSignal,
  ): Promise<PreparedLinkedContext[]> {
    const prepared: PreparedLinkedContext[] = [];
    for (const context of specification.linkedContexts ?? []) {
      const source = join(paths.scratch, `context-${context.projectId}`);
      const projection = join(paths.contexts, context.projectId);
      try {
        this.policy.assertAllowed(context.repository.remote);
        const mirror = this.layout.mirror(context.repository.remote);
        await this.withMirrorLock(mirror, async () => {
          await this.prepareMirror(mirror, context.repository.remote, signal);
          await this.assertCommit(mirror, context.repository.baseCommit, signal);
          await this.materialize(mirror, source, context.repository.baseCommit, signal);
        });
        const identity = (await readFile(join(source, '.pm', 'project_id.txt'), 'utf8')).trim();
        if (identity !== context.projectId) throw new Error('Linked project identity mismatch.');
        await mkdir(join(projection, '.pm'), { recursive: true, mode: 0o700 });
        await writeFile(join(projection, '.pm', 'project_id.txt'), `${identity}\n`, {
          mode: 0o600,
        });
        await writeFile(
          join(projection, '.pm', 'pm_config.yaml'),
          await readFile(join(source, '.pm', 'pm_config.yaml')),
          { mode: 0o600 },
        );
        await copyWikiTree(join(source, '.pm', 'wiki'), join(projection, '.pm', 'wiki'));
        prepared.push({
          projectId: context.projectId,
          name: context.name,
          alias: context.alias,
          revision: context.repository.baseCommit,
          requirement: context.requirement,
          status: 'available',
          summary: 'Exact wiki projection prepared read-only.',
        });
      } catch (error) {
        if (error instanceof Error && error.name === 'AbortError') throw error;
        await rm(projection, { recursive: true, force: true });
        if (context.requirement === 'required') throw failRun('linked_context_unavailable');
        prepared.push({
          projectId: context.projectId,
          name: context.name,
          alias: context.alias,
          revision: context.repository.baseCommit,
          requirement: context.requirement,
          status: 'unavailable',
          summary: 'Optional linked wiki context could not be prepared.',
        });
      } finally {
        await rm(source, { recursive: true, force: true });
      }
    }

    await writeFile(
      paths.contextManifest,
      `${JSON.stringify({ version: 1, primaryProjectId: specification.project.projectId, contexts: prepared }, null, 2)}\n`,
      { mode: 0o600 },
    );
    return prepared;
  }

  private async prepareMirror(mirror: string, remote: string, signal: AbortSignal): Promise<void> {
    await mkdir(this.layout.mirrorsRoot, { recursive: true, mode: 0o700 });
    await chmod(this.layout.mirrorsRoot, 0o700);
    try {
      const stats = await lstat(mirror);
      if (!stats.isDirectory() || stats.isSymbolicLink())
        throw new Error('Repository mirror is not a real directory.');
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
      await run(
        this.gitExecutable,
        ['init', '--bare', mirror],
        undefined,
        signal,
        localGitEnvironment(),
      );
      await chmod(mirror, 0o700);
    }
    const remotes = await run(
      this.gitExecutable,
      ['--git-dir', mirror, 'remote'],
      undefined,
      signal,
      localGitEnvironment(),
    );
    if (remotes.stdout.split('\n').includes('origin'))
      await run(
        this.gitExecutable,
        ['--git-dir', mirror, 'remote', 'set-url', 'origin', remote],
        undefined,
        signal,
        localGitEnvironment(),
      );
    else
      await run(
        this.gitExecutable,
        ['--git-dir', mirror, 'remote', 'add', 'origin', remote],
        undefined,
        signal,
        localGitEnvironment(),
      );
    try {
      await run(
        this.gitExecutable,
        [
          '--git-dir',
          mirror,
          '-c',
          `protocol.file.allow=${this.allowFileRemotes ? 'always' : 'never'}`,
          'fetch',
          '--prune',
          '--no-tags',
          'origin',
          '+refs/heads/*:refs/remotes/origin/*',
        ],
        undefined,
        signal,
        fetchGitEnvironment(),
      );
    } catch (error) {
      if (error instanceof Error && error.name === 'AbortError') throw error;
      throw failRun('repository_fetch_failed');
    }
  }

  private async assertCommit(mirror: string, commit: string, signal: AbortSignal): Promise<void> {
    try {
      await run(
        this.gitExecutable,
        ['--git-dir', mirror, 'cat-file', '-e', `${commit}^{commit}`],
        undefined,
        signal,
        localGitEnvironment(),
      );
      const reachable = await run(
        this.gitExecutable,
        [
          '--git-dir',
          mirror,
          'for-each-ref',
          '--format=%(refname)',
          '--contains',
          commit,
          'refs/remotes/origin/',
        ],
        undefined,
        signal,
        localGitEnvironment(),
      );
      if (reachable.stdout.trim().length === 0) throw failRun('base_revision_unavailable');
    } catch (error) {
      if (
        error instanceof RunFailureError ||
        (error instanceof Error && error.name === 'AbortError')
      )
        throw error;
      throw failRun('base_revision_unavailable');
    }
  }

  private async materialize(
    mirror: string,
    workspace: string,
    commit: string,
    signal: AbortSignal,
  ): Promise<void> {
    await rm(workspace, { recursive: true, force: true });
    await run(
      this.gitExecutable,
      [
        '-c',
        'protocol.file.allow=always',
        'clone',
        '--no-hardlinks',
        '--no-checkout',
        mirror,
        workspace,
      ],
      undefined,
      signal,
      localGitEnvironment(),
    );
    await run(
      this.gitExecutable,
      ['-C', workspace, 'checkout', '--detach', commit],
      undefined,
      signal,
      localGitEnvironment(),
    );
    await run(
      this.gitExecutable,
      ['-C', workspace, 'remote', 'remove', 'origin'],
      undefined,
      signal,
      localGitEnvironment(),
    );
    const head = await run(
      this.gitExecutable,
      ['-C', workspace, 'rev-parse', 'HEAD'],
      undefined,
      signal,
      localGitEnvironment(),
    );
    if (head.stdout.trim() !== commit) throw new Error('Workspace does not match the base commit.');
    await chmod(workspace, 0o700);
  }

  private async assertUnsupportedFeaturesAbsent(
    workspace: string,
    signal: AbortSignal,
  ): Promise<void> {
    try {
      await lstat(join(workspace, '.gitmodules'));
      throw failRun('workspace_policy_unsupported');
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
    }
    const tree = await run(
      this.gitExecutable,
      ['-C', workspace, 'ls-tree', '-r', 'HEAD'],
      undefined,
      signal,
      localGitEnvironment(),
    );
    if (tree.stdout.split('\n').some((line) => line.startsWith('160000 ')))
      throw failRun('workspace_policy_unsupported');
    const candidates = await run(
      this.gitExecutable,
      [
        '-C',
        workspace,
        'grep',
        '-I',
        '-l',
        '-z',
        'version https://git-lfs.github.com/spec/v1',
        'HEAD',
      ],
      undefined,
      signal,
      localGitEnvironment(),
      true,
    );
    if (candidates.exitCode === 0) {
      for (const reference of candidates.stdout.split('\0').filter((value) => value.length > 0)) {
        const blob = await run(
          this.gitExecutable,
          ['-C', workspace, 'cat-file', 'blob', reference],
          undefined,
          signal,
          localGitEnvironment(),
        );
        if (isGitLfsPointer(blob.stdout)) throw failRun('workspace_policy_unsupported');
      }
    }
  }

  private async verifyTaskRevision(
    specification: RunSpecification,
    workspace: string,
  ): Promise<void> {
    const path = join(workspace, '.pm', 'tasks', `${specification.task.taskId}.md`);
    const bytes = await readFile(path);
    const revision = createHash('sha256').update(bytes).digest('hex');
    if (revision !== specification.task.revision) throw failRun('task_revision_mismatch');
  }

  private async stageCodexAuth(codexHome: string): Promise<void> {
    const source = await open(this.codexAuthPath, constants.O_RDONLY | constants.O_NOFOLLOW);
    try {
      const stats = await source.stat();
      const uid = process.getuid?.();
      if (!stats.isFile() || (uid !== undefined && stats.uid !== uid) || (stats.mode & 0o077) !== 0)
        throw new Error('Codex auth must be an owner-only regular file.');
      if (stats.size === 0 || stats.size > maximumAuthBytes)
        throw new Error('Codex auth size is invalid.');
      const bytes = await source.readFile();
      const parsed = JSON.parse(bytes.toString('utf8')) as unknown;
      if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed))
        throw new Error('Codex auth must contain a JSON object.');
      const destination = join(codexHome, 'auth.json');
      await writeFile(destination, bytes, { mode: 0o600, flag: 'wx' });
      await chmod(destination, 0o600);
    } finally {
      await source.close();
    }
  }

  private async withMirrorLock<T>(mirror: string, action: () => Promise<T>): Promise<T> {
    const previous = this.mirrorLocks.get(mirror) ?? Promise.resolve();
    let release!: () => void;
    const current = new Promise<void>((resolveValue) => {
      release = resolveValue;
    });
    const queued = previous.then(() => current);
    this.mirrorLocks.set(mirror, queued);
    await previous;
    try {
      return await action();
    } finally {
      release();
      if (this.mirrorLocks.get(mirror) === queued) this.mirrorLocks.delete(mirror);
    }
  }
}

async function copyWikiTree(source: string, destination: string): Promise<void> {
  const sourceStats = await lstat(source);
  if (!sourceStats.isDirectory() || sourceStats.isSymbolicLink())
    throw new Error('Linked wiki root is invalid.');
  await mkdir(destination, { recursive: true, mode: 0o700 });
  for (const entry of await readdir(source, { withFileTypes: true })) {
    const sourcePath = join(source, entry.name);
    const destinationPath = join(destination, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Linked wiki cannot contain symbolic links.');
    if (entry.isDirectory()) {
      await copyWikiTree(sourcePath, destinationPath);
      continue;
    }
    if (!entry.isFile() || !entry.name.endsWith('.md'))
      throw new Error('Linked wiki can contain only directories and Markdown files.');
    await writeFile(destinationPath, await readFile(sourcePath), { mode: 0o600 });
  }
}

function isGitLfsPointer(content: string): boolean {
  const lines = content.replaceAll('\r\n', '\n').split('\n');
  if (lines[0] !== 'version https://git-lfs.github.com/spec/v1') return false;
  let index = 1;
  while (lines[index]?.startsWith('ext-')) index += 1;
  return (
    /^oid sha256:[0-9a-f]{64}$/.test(lines[index] ?? '') &&
    /^size [0-9]+$/.test(lines[index + 1] ?? '')
  );
}

async function ensureOwnerDirectory(path: string): Promise<void> {
  await mkdir(path, { recursive: true, mode: 0o700 });
  const stats = await lstat(path);
  const uid = process.getuid?.();
  if (
    !stats.isDirectory() ||
    stats.isSymbolicLink() ||
    (uid !== undefined && stats.uid !== uid) ||
    (stats.mode & 0o077) !== 0
  )
    throw new Error('Runner directory must be a real owner-only directory.');
  await chmod(path, 0o700);
}

interface CommandResult {
  exitCode: number;
  stdout: string;
  stderr: string;
}

async function run(
  executable: string,
  argumentsValue: readonly string[],
  cwd: string | undefined,
  signal: AbortSignal,
  environment: NodeJS.ProcessEnv,
  allowFailure = false,
): Promise<CommandResult> {
  if (signal.aborted) throw abortError();
  return await new Promise<CommandResult>((resolveValue, reject) => {
    const child = spawn(executable, [...argumentsValue], {
      cwd,
      env: environment,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let stdout = '';
    let stderr = '';
    const cancel = (): void => {
      child.kill('SIGTERM');
    };
    signal.addEventListener('abort', cancel, { once: true });
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (chunk: string) => {
      if (stdout.length < maximumCommandOutputBytes)
        stdout += chunk.slice(0, maximumCommandOutputBytes - stdout.length);
    });
    child.stderr.on('data', (chunk: string) => {
      if (stderr.length < maximumCommandOutputBytes)
        stderr += chunk.slice(0, maximumCommandOutputBytes - stderr.length);
    });
    child.once('error', reject);
    child.once('close', (code) => {
      signal.removeEventListener('abort', cancel);
      if (signal.aborted) return reject(abortError());
      const result = { exitCode: code ?? 1, stdout, stderr };
      if (!allowFailure && result.exitCode !== 0)
        reject(new Error(`Command ${executable} failed.`));
      else resolveValue(result);
    });
  });
}

function localGitEnvironment(): NodeJS.ProcessEnv {
  return {
    PATH: process.env['PATH'],
    HOME: '/nonexistent',
    GIT_CONFIG_NOSYSTEM: '1',
    GIT_TERMINAL_PROMPT: '0',
    GIT_ASKPASS: '/bin/false',
  };
}

function fetchGitEnvironment(): NodeJS.ProcessEnv {
  const result: NodeJS.ProcessEnv = {
    PATH: process.env['PATH'],
    HOME: process.env['HOME'],
    GIT_TERMINAL_PROMPT: '0',
  };
  for (const name of ['SSH_AUTH_SOCK', 'GIT_ASKPASS', 'GIT_SSH_COMMAND']) {
    const value = process.env[name];
    if (value !== undefined) result[name] = value;
  }
  return result;
}

function abortError(): Error {
  const error = new Error('Workspace operation was cancelled.');
  error.name = 'AbortError';
  return error;
}
