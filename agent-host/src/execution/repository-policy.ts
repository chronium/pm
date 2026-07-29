import { lstatSync, readFileSync } from 'node:fs';

const maximumPolicyBytes = 1024 * 1024;
const remotePattern = /^(?:https:\/\/|ssh:\/\/|git@)[^\s]+$/;

export interface RepositoryPolicyFile {
  repositories: Array<{ remote: string }>;
}

export interface RepositoryAccessPolicy {
  assertAllowed(remote: string): void;
}

export class RepositoryPolicy implements RepositoryAccessPolicy {
  private constructor(private readonly remotes: ReadonlySet<string>) {}

  static load(path: string, uid = process.getuid?.()): RepositoryPolicy {
    const stats = lstatSync(path);
    if (!stats.isFile() || stats.isSymbolicLink())
      throw new Error('Repository policy must be a regular file.');
    if (uid !== undefined && stats.uid !== uid)
      throw new Error('Repository policy must be owned by the runner user.');
    if ((stats.mode & 0o077) !== 0)
      throw new Error('Repository policy permissions must be owner-only.');
    if (stats.size > maximumPolicyBytes) throw new Error('Repository policy is too large.');
    let value: unknown;
    try {
      value = JSON.parse(readFileSync(path, 'utf8')) as unknown;
    } catch {
      throw new Error('Repository policy is not valid JSON.');
    }
    if (value === null || typeof value !== 'object' || Array.isArray(value))
      throw new Error('Repository policy must be an object.');
    const repositories = (value as Partial<RepositoryPolicyFile>).repositories;
    if (!Array.isArray(repositories) || repositories.length === 0 || repositories.length > 256)
      throw new Error('Repository policy must contain 1 to 256 repositories.');
    const remotes = repositories.map((entry) => {
      const remote = entry?.remote;
      if (typeof remote !== 'string' || remote.length > 2048 || !remotePattern.test(remote))
        throw new Error('Repository policy contains an unsafe remote.');
      assertRemoteIsSafe(remote);
      return remote;
    });
    if (new Set(remotes).size !== remotes.length)
      throw new Error('Repository policy contains duplicate remotes.');
    return new RepositoryPolicy(new Set(remotes));
  }

  assertAllowed(remote: string): void {
    if (!this.remotes.has(remote)) throw new Error('Repository remote is not allowlisted.');
  }
}

function assertRemoteIsSafe(remote: string): void {
  if (remote.startsWith('git@')) {
    const host = remote.slice(4).split(':', 1)[0]?.toLowerCase();
    if (host === undefined || isLocalHost(host))
      throw new Error('Repository policy cannot contain local remotes.');
    return;
  }
  let url: URL;
  try {
    url = new URL(remote);
  } catch {
    throw new Error('Repository policy contains an unsafe remote.');
  }
  if (url.password.length > 0 || url.username.length > 0 || isLocalHost(url.hostname))
    throw new Error('Repository policy cannot contain credentials or local remotes.');
}

function isLocalHost(host: string): boolean {
  const value = host.toLowerCase().replace(/^\[|\]$/g, '');
  return (
    value === 'localhost' ||
    value === '::1' ||
    value.startsWith('127.') ||
    value.startsWith('0.') ||
    value.endsWith('.localhost')
  );
}
