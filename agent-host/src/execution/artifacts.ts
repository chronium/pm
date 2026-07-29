import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstat, mkdir, readdir, readlink, rm, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import type { StoredRun, RunStore } from '../persistence/run-store.js';
import type { RunArtifact } from '../protocol/types.js';
import type { RunnerLayout } from './layout.js';
import type { ValidationResult } from './validation.js';
import type { RuntimeUsage } from '../drivers.js';

const maximumCommandBytes = 64 * 1024 * 1024;
const maximumEventExportBytes = 16 * 1024 * 1024;

export interface CollectionInput {
  run: StoredRun;
  mirror: string;
  validation: ValidationResult;
  agentResponse: string | null;
  executionStatus: 'succeeded' | 'failed' | 'cancelled';
  executionError: string | null;
  startedAt: string;
  resourceUsage: {
    agent: RuntimeUsage | null;
    validation: RuntimeUsage | null;
  };
}

export class ArtifactCollector {
  constructor(
    private readonly store: RunStore,
    private readonly layout: RunnerLayout,
    private readonly gitExecutable = 'git',
    private readonly now: () => Date = () => new Date(),
  ) {}

  async collect(input: CollectionInput, signal: AbortSignal): Promise<RunArtifact[]> {
    const paths = this.layout.run(input.run.runId);
    await mkdir(paths.artifacts, { recursive: true, mode: 0o700 });
    await rm(paths.scratch, { recursive: true, force: true });
    await mkdir(join(paths.scratch, 'objects'), { recursive: true, mode: 0o700 });
    const environment = artifactGitEnvironment(input.mirror, paths.workspace, paths.scratch);
    const warnings = await inspectWorkspace(paths.workspace);
    await git(
      this.gitExecutable,
      ['read-tree', input.run.specification.repository.baseCommit],
      environment,
      signal,
    );
    await git(this.gitExecutable, ['add', '-A', '--', '.'], environment, signal);
    const summary = await git(
      this.gitExecutable,
      ['diff', '--cached', '--numstat', '--summary', input.run.specification.repository.baseCommit],
      environment,
      signal,
    );
    const names = await git(
      this.gitExecutable,
      [
        'diff',
        '--cached',
        '--name-status',
        '--no-renames',
        '-z',
        input.run.specification.repository.baseCommit,
      ],
      environment,
      signal,
    );
    const patch = await git(
      this.gitExecutable,
      [
        'diff',
        '--cached',
        '--binary',
        '--full-index',
        '--no-ext-diff',
        input.run.specification.repository.baseCommit,
      ],
      environment,
      signal,
      input.run.specification.runtime.profile.output.maxPatchBytes,
      true,
    );

    const artifacts: RunArtifact[] = [];
    const patchAllowed = !patch.truncated;
    if (patchAllowed)
      artifacts.push(
        await this.write(
          input.run.runId,
          'changes-patch',
          'patch',
          'changes.patch',
          'text/x-diff',
          patch.stdout,
        ),
      );
    artifacts.push(
      await this.writeJson(
        input.run.runId,
        'changes-summary',
        'changes-summary',
        'changes-summary.json',
        {
          baseCommit: input.run.specification.repository.baseCommit,
          changedPaths: parseNameStatus(names.stdout),
          diffStatistics: summary.stdout.toString('utf8'),
          patchIncluded: patchAllowed,
          patchBytes: patch.truncated ? null : patch.stdout.length,
          patchExceededLimit: patch.truncated,
          maximumPatchBytes: input.run.specification.runtime.profile.output.maxPatchBytes,
          warnings,
        },
      ),
    );
    artifacts.push(
      await this.writeJson(
        input.run.runId,
        'validation',
        'validation',
        'validation.json',
        input.validation,
      ),
    );
    if (input.agentResponse !== null)
      artifacts.push(
        await this.write(
          input.run.runId,
          'agent-response',
          'agent-response',
          'agent-response.md',
          'text/markdown',
          Buffer.from(input.agentResponse, 'utf8'),
        ),
      );
    artifacts.push(
      await this.writeJson(input.run.runId, 'run-report', 'run-report', 'run-report.json', {
        runId: input.run.runId,
        specificationHash: input.run.specificationHash,
        taskRevision: input.run.specification.task.revision,
        executionStatus: input.executionStatus,
        executionError: input.executionError,
        validationStatus: input.validation.status,
        startedAt: input.startedAt,
        collectedAt: this.now().toISOString(),
        resourceUsage: input.resourceUsage,
      }),
    );
    if (input.run.specification.runtime.profile.output.includeEventLog) {
      const lines: string[] = [];
      let bytes = 0;
      for (const event of this.store.eventsAfter(input.run.runId)) {
        const line = `${JSON.stringify(event)}\n`;
        const lineBytes = Buffer.byteLength(line);
        if (bytes + lineBytes > maximumEventExportBytes) break;
        lines.push(line);
        bytes += lineBytes;
      }
      artifacts.push(
        await this.write(
          input.run.runId,
          'events',
          'event-log',
          'events.jsonl',
          'application/x-ndjson',
          Buffer.from(lines.join(''), 'utf8'),
        ),
      );
    }
    const manifestEntries = artifacts.map((artifact) => ({ ...artifact }));
    artifacts.push(
      await this.writeJson(input.run.runId, 'manifest', 'manifest', 'manifest.json', {
        runId: input.run.runId,
        artifacts: manifestEntries,
      }),
    );
    await rm(paths.scratch, { recursive: true, force: true });
    return artifacts;
  }

  private async writeJson(
    runId: string,
    artifactId: string,
    kind: string,
    fileName: string,
    value: unknown,
  ): Promise<RunArtifact> {
    return await this.write(
      runId,
      artifactId,
      kind,
      fileName,
      'application/json',
      Buffer.from(`${JSON.stringify(value, null, 2)}\n`, 'utf8'),
    );
  }

  private async write(
    runId: string,
    artifactId: string,
    kind: string,
    fileName: string,
    mediaType: string,
    bytes: Buffer,
  ): Promise<RunArtifact> {
    const paths = this.layout.run(runId);
    const path = join(paths.artifacts, fileName);
    await writeFile(path, bytes, { mode: 0o600, flag: 'wx' });
    const artifact: RunArtifact = {
      artifactId,
      kind,
      fileName,
      mediaType,
      byteLength: bytes.length,
      sha256: createHash('sha256').update(bytes).digest('hex'),
      createdAt: this.now().toISOString(),
    };
    this.store.recordArtifact(runId, artifact, this.layout.relative(path));
    this.store.appendEvent(runId, {
      type: 'artifact.created',
      state: 'collecting_artifacts',
      summary: `Collected ${fileName}`,
      data: artifact,
    });
    return artifact;
  }
}

async function git(
  executable: string,
  argumentsValue: readonly string[],
  environment: NodeJS.ProcessEnv,
  signal: AbortSignal,
  maximumBytes = maximumCommandBytes,
  truncateOutput = false,
): Promise<{ stdout: Buffer; stderr: Buffer; truncated: boolean }> {
  return await new Promise((resolveValue, reject) => {
    const child = spawn(executable, [...argumentsValue], {
      env: environment,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    const stdout: Buffer[] = [];
    const stderr: Buffer[] = [];
    let stdoutBytes = 0;
    let stderrBytes = 0;
    let truncated = false;
    const cancel = (): void => {
      child.kill('SIGTERM');
    };
    signal.addEventListener('abort', cancel, { once: true });
    child.stdout.on('data', (chunk: Buffer) => {
      const previousBytes = stdoutBytes;
      stdoutBytes += chunk.length;
      const retained = Math.max(0, maximumBytes - previousBytes);
      if (retained > 0) stdout.push(chunk.subarray(0, retained));
      if (retained < chunk.length) {
        truncated = true;
        if (!truncateOutput) child.kill('SIGTERM');
      }
    });
    child.stderr.on('data', (chunk: Buffer) => {
      stderrBytes += chunk.length;
      if (stderrBytes <= 1024 * 1024) stderr.push(chunk);
    });
    child.once('error', reject);
    child.once('close', (code) => {
      signal.removeEventListener('abort', cancel);
      if (signal.aborted) return reject(abortError());
      if (stdoutBytes > maximumBytes && !truncateOutput)
        return reject(new Error('Git artifact output exceeded its bound.'));
      if (code !== 0) return reject(new Error('Git artifact command failed.'));
      resolveValue({ stdout: Buffer.concat(stdout), stderr: Buffer.concat(stderr), truncated });
    });
  });
}

function artifactGitEnvironment(
  mirror: string,
  workspace: string,
  scratch: string,
): NodeJS.ProcessEnv {
  return {
    PATH: process.env['PATH'],
    HOME: join(scratch, 'home'),
    GIT_CONFIG_NOSYSTEM: '1',
    GIT_ATTR_NOSYSTEM: '1',
    GIT_TERMINAL_PROMPT: '0',
    GIT_DIR: mirror,
    GIT_WORK_TREE: workspace,
    GIT_INDEX_FILE: join(scratch, 'index'),
    GIT_OBJECT_DIRECTORY: join(scratch, 'objects'),
    GIT_ALTERNATE_OBJECT_DIRECTORIES: join(mirror, 'objects'),
    GIT_OPTIONAL_LOCKS: '0',
  };
}

function parseNameStatus(bytes: Buffer): Array<{ status: string; path: string }> {
  const values = bytes
    .toString('utf8')
    .split('\0')
    .filter((value) => value.length > 0);
  const result: Array<{ status: string; path: string }> = [];
  for (let index = 0; index < values.length; index += 2)
    result.push({ status: values[index] ?? '', path: values[index + 1] ?? '' });
  return result;
}

async function inspectWorkspace(root: string): Promise<string[]> {
  const warnings: string[] = [];
  const visit = async (directory: string): Promise<void> => {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      if (entry.name === '.git' && directory === root) continue;
      const path = join(directory, entry.name);
      const stats = await lstat(path);
      if (stats.isDirectory()) await visit(path);
      else if (stats.isSymbolicLink()) {
        const target = await readlink(path);
        const resolved = resolve(dirname(path), target);
        if (isAbsolute(target) || (resolved !== root && !resolved.startsWith(`${root}/`)))
          warnings.push(`Symlink escapes workspace: ${relative(root, path)}`);
      } else if (!stats.isFile())
        warnings.push(`Unsupported special file: ${relative(root, path)}`);
    }
  };
  await visit(root);
  return warnings;
}

function abortError(): Error {
  const error = new Error('Artifact collection was cancelled.');
  error.name = 'AbortError';
  return error;
}
