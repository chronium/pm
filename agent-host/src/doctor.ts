import { existsSync, lstatSync, statfsSync } from 'node:fs';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { loadTlsMaterial } from './auth/tls.js';
import { loadCapabilityManifest } from './capabilities.js';
import type { HostConfig } from './config.js';
import { CommandPodmanProbe, type ContainerRuntimeProbe } from './oci/podman-probe.js';
import type { ReleaseInfo } from './release-info.js';
import { loadReleaseInfo } from './release-info.js';
import { RepositoryPolicy } from './execution/repository-policy.js';

export type DoctorStatus = 'pass' | 'warning' | 'failure';

export interface DoctorCheck {
  id: string;
  status: DoctorStatus;
  summary: string;
}

export interface DoctorReport {
  ok: boolean;
  checkedAt: string;
  release: ReleaseInfo;
  checks: DoctorCheck[];
}

export interface DoctorDependencies {
  platform: () => NodeJS.Platform;
  architecture: () => string;
  nodeVersion: () => string;
  userId: () => number;
  runtimeProbe: ContainerRuntimeProbe;
  now: () => Date;
}

const requiredNodeVersion = 'v26.5.0';

export function runDoctor(
  config: HostConfig,
  dependencies: Partial<DoctorDependencies> = {},
): DoctorReport {
  const checks: DoctorCheck[] = [];
  const deps: DoctorDependencies = {
    platform: dependencies.platform ?? (() => process.platform),
    architecture: dependencies.architecture ?? (() => process.arch),
    nodeVersion: dependencies.nodeVersion ?? (() => process.version),
    userId: dependencies.userId ?? (() => process.getuid?.() ?? 0),
    runtimeProbe: dependencies.runtimeProbe ?? new CommandPodmanProbe(),
    now: dependencies.now ?? (() => new Date()),
  };
  const release = checkRelease(config, checks);

  record(
    checks,
    'platform',
    deps.platform() === 'linux' && deps.architecture() === 'x64',
    `Linux x64 host detected (${deps.platform()} ${deps.architecture()}).`,
    'The packaged runner requires Linux x64.',
  );
  record(
    checks,
    'unprivileged_user',
    deps.userId() !== 0,
    'Runner host is unprivileged.',
    'The runner must not execute as root.',
  );
  record(
    checks,
    'node_version',
    deps.nodeVersion() === requiredNodeVersion,
    `Node ${requiredNodeVersion.slice(1)} is installed.`,
    `Node ${requiredNodeVersion.slice(1)} is required; found ${deps.nodeVersion().replace(/^v/, '')}.`,
  );

  checkDataRoot(config.dataRoot, config.minimumFreeDiskBytes, checks);
  checkFile(config.tlsCertificatePath, 'tls_certificate', false, checks);
  checkFile(config.tlsKeyPath, 'tls_private_key', true, checks);
  checkFile(config.capabilityManifestPath, 'capability_manifest', true, checks);
  checkFile(config.repositoryPolicyPath, 'repository_policy', true, checks);
  checkFile(config.codexAuthPath, 'codex_authentication', true, checks);

  let manifest: ReturnType<typeof loadCapabilityManifest> | undefined;
  try {
    loadTlsMaterial(config.tlsCertificatePath!, config.tlsKeyPath!);
    checks.push({ id: 'tls_material', status: 'pass', summary: 'TLS material is valid.' });
  } catch {
    checks.push({ id: 'tls_material', status: 'failure', summary: 'TLS material is invalid.' });
  }
  try {
    manifest = loadCapabilityManifest(config.capabilityManifestPath!);
    checks.push({
      id: 'capability_contract',
      status: 'pass',
      summary: 'Capability manifest is valid.',
    });
  } catch {
    checks.push({
      id: 'capability_contract',
      status: 'failure',
      summary: 'Capability manifest is invalid.',
    });
  }
  try {
    RepositoryPolicy.load(config.repositoryPolicyPath!);
    checks.push({
      id: 'repository_contract',
      status: 'pass',
      summary: 'Repository allowlist is valid.',
    });
  } catch {
    checks.push({
      id: 'repository_contract',
      status: 'failure',
      summary: 'Repository allowlist is invalid.',
    });
  }
  if (manifest !== undefined) {
    try {
      const runtime = deps.runtimeProbe.inspect(manifest.runtimeProfiles);
      checks.push({
        id: 'container_runtime',
        status: 'pass',
        summary: `Rootless ${runtime.engineId} ${runtime.version} is ready with all images installed.`,
      });
    } catch {
      checks.push({
        id: 'container_runtime',
        status: 'failure',
        summary: 'Rootless Podman or an immutable runtime image is unavailable.',
      });
    }
  }

  checkDatabase(join(config.dataRoot, 'runner.sqlite'), 'runner_database', checks);
  checkDatabase(join(config.dataRoot, 'credentials.sqlite'), 'credential_database', checks);
  return {
    ok: !checks.some((check) => check.status === 'failure'),
    checkedAt: deps.now().toISOString(),
    release,
    checks,
  };
}

export function formatDoctorReport(report: DoctorReport): string {
  const lines = [
    `PM agent host ${report.release.packageVersion} (${report.release.sourceRevision})`,
  ];
  for (const check of report.checks) {
    const marker = check.status === 'pass' ? 'PASS' : check.status === 'warning' ? 'WARN' : 'FAIL';
    lines.push(`[${marker}] ${check.summary}`);
  }
  lines.push(report.ok ? 'Runner is ready.' : 'Runner is not ready.');
  return `${lines.join('\n')}\n`;
}

function checkRelease(config: HostConfig, checks: DoctorCheck[]): ReleaseInfo {
  try {
    const release = loadReleaseInfo(config.releaseManifestPath);
    checks.push({
      id: 'release_manifest',
      status: config.releaseManifestPath === null ? 'warning' : 'pass',
      summary:
        config.releaseManifestPath === null
          ? 'Release manifest is not configured; development build information is in use.'
          : 'Release manifest is valid.',
    });
    return release;
  } catch {
    checks.push({
      id: 'release_manifest',
      status: 'failure',
      summary: 'Release manifest is invalid.',
    });
    return {
      schemaVersion: 1,
      packageVersion: 'unknown',
      sourceRevision: 'development',
      builtAt: 'development',
      platform: 'development',
      nodeVersion: '26.5.0',
      protocolVersion: '1.2',
      workerImageReference: null,
      workerImageDigest: null,
    };
  }
}

function checkDataRoot(path: string, requiredFreeBytes: number, checks: DoctorCheck[]): void {
  try {
    const stats = lstatSync(path);
    if (!stats.isDirectory() || stats.isSymbolicLink() || (stats.mode & 0o077) !== 0)
      throw new Error();
    checks.push({
      id: 'data_root',
      status: 'pass',
      summary: 'Runner data root is an owner-only real directory.',
    });
  } catch {
    checks.push({
      id: 'data_root',
      status: 'failure',
      summary: 'Runner data root is missing, linked, or accessible by other users.',
    });
    return;
  }
  try {
    const fileSystem = statfsSync(path);
    const freeBytes = fileSystem.bavail * fileSystem.bsize;
    record(
      checks,
      'disk_reserve',
      freeBytes >= requiredFreeBytes,
      'Runner data root has the configured free-space reserve.',
      'Runner data root is below the configured free-space reserve.',
    );
  } catch {
    checks.push({
      id: 'disk_reserve',
      status: 'failure',
      summary: 'Runner data root free space could not be inspected.',
    });
  }
}

function checkFile(
  path: string | null,
  id: string,
  ownerOnly: boolean,
  checks: DoctorCheck[],
): void {
  try {
    if (path === null) throw new Error();
    const stats = lstatSync(path);
    if (!stats.isFile() || stats.isSymbolicLink() || (ownerOnly && (stats.mode & 0o077) !== 0))
      throw new Error();
    checks.push({ id, status: 'pass', summary: `${label(id)} is a secure regular file.` });
  } catch {
    checks.push({
      id,
      status: 'failure',
      summary: `${label(id)} is missing, linked, or has unsafe permissions.`,
    });
  }
}

function checkDatabase(path: string, id: string, checks: DoctorCheck[]): void {
  if (!existsSync(path)) {
    checks.push({
      id,
      status: 'warning',
      summary: `${label(id)} has not been created yet.`,
    });
    return;
  }
  let database: DatabaseSync | undefined;
  try {
    const stats = lstatSync(path);
    if (!stats.isFile() || stats.isSymbolicLink() || (stats.mode & 0o077) !== 0) throw new Error();
    database = new DatabaseSync(path, { readOnly: true });
    const row = database.prepare('PRAGMA quick_check').get() as { quick_check: string };
    if (row.quick_check !== 'ok') throw new Error();
    checks.push({ id, status: 'pass', summary: `${label(id)} passed SQLite quick_check.` });
  } catch {
    checks.push({ id, status: 'failure', summary: `${label(id)} failed its integrity check.` });
  } finally {
    database?.close();
  }
}

function record(
  checks: DoctorCheck[],
  id: string,
  condition: boolean,
  success: string,
  failure: string,
): void {
  checks.push({
    id,
    status: condition ? 'pass' : 'failure',
    summary: condition ? success : failure,
  });
}

function label(id: string): string {
  return id.replaceAll('_', ' ').replace(/^./, (value) => value.toUpperCase());
}
