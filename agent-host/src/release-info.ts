import { readFileSync } from 'node:fs';

export interface ReleaseInfo {
  schemaVersion: 1;
  packageVersion: string;
  sourceRevision: string;
  builtAt: string;
  platform: 'linux-x64' | 'development';
  nodeVersion: string;
  protocolVersion: '1.1';
  workerImageReference: string | null;
  workerImageDigest: string | null;
}

export const developmentReleaseInfo: ReleaseInfo = {
  schemaVersion: 1,
  packageVersion: '0.1.0',
  sourceRevision: 'development',
  builtAt: 'development',
  platform: 'development',
  nodeVersion: '26.5.0',
  protocolVersion: '1.1',
  workerImageReference: null,
  workerImageDigest: null,
};

export function loadReleaseInfo(path: string | null): ReleaseInfo {
  if (path === null) return developmentReleaseInfo;
  let value: unknown;
  try {
    value = JSON.parse(readFileSync(path, 'utf8')) as unknown;
  } catch {
    throw new Error('Runner release manifest could not be read as JSON.');
  }
  return parseReleaseInfo(value);
}

export function parseReleaseInfo(value: unknown): ReleaseInfo {
  if (value === null || typeof value !== 'object' || Array.isArray(value))
    throw new Error('Runner release manifest must be an object.');
  const record = value as Record<string, unknown>;
  if (record['schemaVersion'] !== 1)
    throw new Error('Runner release manifest schema is unsupported.');
  const packageVersion = boundedString(record['packageVersion'], 'Package version', 64);
  const sourceRevision = boundedString(record['sourceRevision'], 'Source revision', 64);
  if (sourceRevision !== 'development' && !/^[0-9a-f]{40}$/.test(sourceRevision))
    throw new Error('Runner source revision is invalid.');
  const builtAt = boundedString(record['builtAt'], 'Build timestamp', 64);
  if (builtAt !== 'development' && Number.isNaN(Date.parse(builtAt)))
    throw new Error('Runner build timestamp is invalid.');
  const platform = record['platform'];
  if (platform !== 'linux-x64' && platform !== 'development')
    throw new Error('Runner release platform is invalid.');
  const nodeVersion = boundedString(record['nodeVersion'], 'Node version', 32);
  if (record['protocolVersion'] !== '1.1')
    throw new Error('Runner release protocol version is unsupported.');
  const workerImageReference = optionalString(record['workerImageReference'], 'Image reference');
  const workerImageDigest = optionalString(record['workerImageDigest'], 'Image digest');
  if (workerImageDigest !== null && !/^sha256:[0-9a-f]{64}$/.test(workerImageDigest))
    throw new Error('Runner worker image digest is invalid.');
  if (platform === 'linux-x64' && (workerImageReference === null || workerImageDigest === null))
    throw new Error('Linux runner releases require an immutable worker image.');
  return {
    schemaVersion: 1,
    packageVersion,
    sourceRevision,
    builtAt,
    platform,
    nodeVersion,
    protocolVersion: '1.1',
    workerImageReference,
    workerImageDigest,
  };
}

function boundedString(value: unknown, name: string, maximumLength: number): string {
  if (typeof value !== 'string' || value.length === 0 || value.length > maximumLength)
    throw new Error(`${name} is invalid.`);
  return value;
}

function optionalString(value: unknown, name: string): string | null {
  if (value === null) return null;
  return boundedString(value, name, 512);
}
