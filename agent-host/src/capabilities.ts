import { readFileSync } from 'node:fs';
import { totalmem } from 'node:os';
import type { RunStore } from './persistence/run-store.js';
import type {
  CapabilityManifest,
  ContainerRuntimeCapability,
  RunnerCapabilities,
} from './protocol/types.js';
import type { RunRequest } from './protocol/types.js';
import { canonicalRuntimeProfile } from './protocol/canonical-json.js';
import { parseCapabilityManifest } from './protocol/validation.js';
import { CommandPodmanProbe, type ContainerRuntimeProbe } from './oci/podman-probe.js';
import { supportedProtocolVersions } from './auth/authentication.js';

export type RunCapabilityResult =
  | { valid: true }
  | { valid: false; errorCode: string; message: string };

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
  private readonly containerRuntime: ContainerRuntimeCapability;

  constructor(
    private readonly store: RunStore,
    private readonly manifest: CapabilityManifest,
    private readonly maximumRuns: number,
    runtimeProbe: ContainerRuntimeProbe = new CommandPodmanProbe(),
    private readonly memoryBytes: () => number = totalmem,
    private readonly platform: () => NodeJS.Platform = () => process.platform,
    private readonly architecture: () => string = () => process.arch,
  ) {
    this.containerRuntime = runtimeProbe.inspect(manifest.runtimeProfiles);
  }

  get(): RunnerCapabilities {
    return {
      runnerId: this.store.runnerId,
      displayName: this.manifest.displayName,
      protocolVersions: [...supportedProtocolVersions],
      operatingSystem: normalizePlatform(this.platform()),
      architecture: normalizeArchitecture(this.architecture()),
      containerRuntime: this.containerRuntime,
      capacity: {
        maximumRuns: this.maximumRuns,
        activeRuns: this.store.activeRunCount(),
        memoryBytes: this.memoryBytes(),
      },
      agentProviders: this.manifest.agentProviders,
      runtimeProfiles: this.manifest.runtimeProfiles,
    };
  }

  validateRun(request: RunRequest): RunCapabilityResult {
    const specification = request.specification;
    if (specification.runtime.runnerId !== this.store.runnerId)
      return {
        valid: false,
        errorCode: 'runner_mismatch',
        message: 'The run targets a different runner.',
      };
    const provider = this.manifest.agentProviders.find(
      (candidate) => candidate.providerId === specification.agent.providerId,
    );
    if (
      provider === undefined ||
      !provider.modelIds.includes(specification.agent.modelId) ||
      !provider.effortIds.includes(specification.agent.effortId)
    )
      return {
        valid: false,
        errorCode: 'agent_capability_mismatch',
        message: 'The requested agent capability is not installed.',
      };
    const profile = this.manifest.runtimeProfiles.find(
      (candidate) => candidate.profileId === specification.runtime.profile.profileId,
    );
    if (
      profile === undefined ||
      canonicalRuntimeProfile(profile) !== canonicalRuntimeProfile(specification.runtime.profile)
    )
      return {
        valid: false,
        errorCode: 'runtime_profile_mismatch',
        message: 'The requested runtime profile does not match the installed snapshot.',
      };
    return { valid: true };
  }
}

function normalizePlatform(value: NodeJS.Platform): string {
  return value === 'win32' ? 'windows' : value;
}

function normalizeArchitecture(value: string): string {
  return value === 'x64' || value === 'arm64' ? value : value.replaceAll('_', '-');
}
