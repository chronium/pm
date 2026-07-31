import { createHash } from 'node:crypto';
import { join, relative, resolve } from 'node:path';

export interface RunPaths {
  readonly runRoot: string;
  readonly workspace: string;
  readonly codexHome: string;
  readonly runtime: string;
  readonly artifacts: string;
  readonly scratch: string;
  readonly contexts: string;
  readonly contextManifest: string;
}

export class RunnerLayout {
  readonly dataRoot: string;
  readonly runsRoot: string;
  readonly mirrorsRoot: string;

  constructor(dataRoot: string) {
    this.dataRoot = resolve(dataRoot);
    this.runsRoot = join(this.dataRoot, 'runs');
    this.mirrorsRoot = join(this.dataRoot, 'mirrors');
  }

  run(runId: string): RunPaths {
    const runRoot = this.owned(join(this.runsRoot, runId));
    return {
      runRoot,
      workspace: join(runRoot, 'workspace'),
      codexHome: join(runRoot, 'codex-home'),
      runtime: join(runRoot, 'runtime'),
      artifacts: join(runRoot, 'artifacts'),
      scratch: join(runRoot, 'scratch'),
      contexts: join(runRoot, 'contexts'),
      contextManifest: join(runRoot, 'contexts', 'manifest.json'),
    };
  }

  mirror(remote: string): string {
    const key = createHash('sha256').update(remote, 'utf8').digest('hex');
    return this.owned(join(this.mirrorsRoot, `${key}.git`));
  }

  relative(path: string): string {
    const absolute = this.owned(path);
    return relative(this.dataRoot, absolute).replaceAll('\\', '/');
  }

  private owned(path: string): string {
    const absolute = resolve(path);
    if (absolute !== this.dataRoot && !absolute.startsWith(`${this.dataRoot}/`))
      throw new Error('Runner path escapes the data root.');
    return absolute;
  }
}
