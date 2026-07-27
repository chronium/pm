import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { totalmem } from 'node:os';
import type { RunStore } from './persistence/run-store.js';
import type { CapabilityManifest, RunnerCapabilities } from './protocol/types.js';
import { parseCapabilityManifest } from './protocol/validation.js';

export interface DockerProbe {
  available(): boolean;
}

export class CommandDockerProbe implements DockerProbe {
  available(): boolean {
    const result = spawnSync('docker', ['info', '--format', '{{.ServerVersion}}'], {
      encoding: 'utf8',
      stdio: ['ignore', 'ignore', 'ignore'],
      timeout: 2_000,
    });
    return result.status === 0;
  }
}

export function loadCapabilityManifest(path: string): CapabilityManifest {
  let value: unknown;
  try {
    value = JSON.parse(readFileSync(path, 'utf8')) as unknown;
  } catch {
    throw new Error('Runner capability manifest could not be read as JSON.');
  }
  return parseCapabilityManifest(value);
}

export class CapabilityService {
  private readonly dockerAvailable: boolean;

  constructor(
    private readonly store: RunStore,
    private readonly manifest: CapabilityManifest,
    private readonly maximumRuns: number,
    dockerProbe: DockerProbe = new CommandDockerProbe(),
    private readonly memoryBytes: () => number = totalmem,
    private readonly platform: () => NodeJS.Platform = () => process.platform,
    private readonly architecture: () => string = () => process.arch,
  ) {
    this.dockerAvailable = dockerProbe.available();
  }

  get(): RunnerCapabilities {
    return {
      runnerId: this.store.runnerId,
      displayName: this.manifest.displayName,
      protocolVersions: ['1.0'],
      operatingSystem: normalizePlatform(this.platform()),
      architecture: normalizeArchitecture(this.architecture()),
      dockerAvailable: this.dockerAvailable,
      capacity: {
        maximumRuns: this.maximumRuns,
        activeRuns: this.store.activeRunCount(),
        memoryBytes: this.memoryBytes(),
      },
      agentProviders: this.manifest.agentProviders,
      runtimeProfiles: this.manifest.runtimeProfiles,
    };
  }
}

function normalizePlatform(value: NodeJS.Platform): string {
  return value === 'win32' ? 'windows' : value;
}

function normalizeArchitecture(value: string): string {
  return value === 'x64' || value === 'arm64' ? value : value.replaceAll('_', '-');
}
