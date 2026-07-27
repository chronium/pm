import { rmSync } from 'node:fs';
import { isAbsolute, relative, resolve, sep } from 'node:path';
import type { JsonLogger } from './logging.js';
import type { RunStore } from './persistence/run-store.js';

const dayMilliseconds = 24 * 60 * 60 * 1000;

export class RetentionService {
  constructor(
    private readonly store: RunStore,
    private readonly dataRoot: string,
    private readonly retentionDays: number,
    private readonly logger: JsonLogger,
    private readonly now: () => Date = () => new Date(),
  ) {}

  prune(): number {
    if (this.retentionDays === 0) return 0;
    const cutoff = new Date(this.now().getTime() - this.retentionDays * dayMilliseconds);
    let prunedRuns = 0;

    for (const run of this.store.expiredTerminalRuns(cutoff)) {
      try {
        for (const location of run.artifactLocations) this.assertOwnedLocation(location);
        const runRoot = resolve(this.dataRoot, 'runs', run.runId);
        this.assertInsideDataRoot(runRoot);
        rmSync(runRoot, { recursive: true, force: true });
        if (this.store.deleteTerminalRun(run.runId)) prunedRuns += 1;
      } catch {
        this.logger.error('retention.run_failed', {
          runId: run.runId,
          errorCode: 'retention_cleanup_failed',
        });
      }
    }

    if (prunedRuns > 0) this.logger.info('retention.completed', { prunedRuns });
    return prunedRuns;
  }

  private assertOwnedLocation(location: string): void {
    if (isAbsolute(location)) throw new Error('Artifact location must be relative.');
    this.assertInsideDataRoot(resolve(this.dataRoot, location));
  }

  private assertInsideDataRoot(path: string): void {
    const fromRoot = relative(resolve(this.dataRoot), path);
    if (
      fromRoot.length === 0 ||
      fromRoot === '..' ||
      fromRoot.startsWith(`..${sep}`) ||
      isAbsolute(fromRoot)
    )
      throw new Error('Retention path escapes the runner data root.');
  }
}
