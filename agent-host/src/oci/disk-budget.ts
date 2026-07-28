import { lstat, opendir, statfs } from 'node:fs/promises';

export interface DiskBudgetUsage {
  bytes: number;
  entries: number;
  freeBytes: number;
}

export interface DiskBudgetChecker {
  check(
    paths: readonly string[],
    maximumBytes: number,
    filesystemPath: string,
    minimumFreeBytes: number,
  ): Promise<DiskBudgetUsage>;
}

export class DiskBudgetExceededError extends Error {
  constructor(readonly code: 'runtime_disk_limit_exceeded' | 'runner_disk_reserve_reached') {
    super(
      code === 'runtime_disk_limit_exceeded'
        ? 'Runtime writable storage exceeded its disk budget.'
        : 'Runner storage reached its minimum free-space reserve.',
    );
    this.name = 'DiskBudgetExceededError';
  }
}

export class HostDiskBudgetChecker implements DiskBudgetChecker {
  constructor(private readonly maximumEntries = 250_000) {}

  async check(
    paths: readonly string[],
    maximumBytes: number,
    filesystemPath: string,
    minimumFreeBytes: number,
  ): Promise<DiskBudgetUsage> {
    let bytes = 0;
    let entries = 0;
    for (const path of paths) {
      const usage = await directoryUsage(path, this.maximumEntries - entries);
      bytes += usage.bytes;
      entries += usage.entries;
      if (entries > this.maximumEntries || bytes > maximumBytes)
        throw new DiskBudgetExceededError('runtime_disk_limit_exceeded');
    }
    const filesystem = await statfs(filesystemPath);
    const freeBytes = Number(filesystem.bavail) * Number(filesystem.bsize);
    if (!Number.isSafeInteger(freeBytes) || freeBytes < minimumFreeBytes)
      throw new DiskBudgetExceededError('runner_disk_reserve_reached');
    return { bytes, entries, freeBytes };
  }
}

async function directoryUsage(
  root: string,
  remainingEntries: number,
): Promise<{ bytes: number; entries: number }> {
  let bytes = 0;
  let entries = 0;
  const pending = [root];
  while (pending.length > 0) {
    const directoryPath = pending.pop()!;
    const directory = await opendir(directoryPath);
    for await (const entry of directory) {
      entries += 1;
      if (entries > remainingEntries)
        throw new DiskBudgetExceededError('runtime_disk_limit_exceeded');
      const path = `${directoryPath}/${entry.name}`;
      const stats = await lstat(path);
      bytes += stats.size;
      if (stats.isDirectory()) pending.push(path);
    }
  }
  return { bytes, entries };
}
