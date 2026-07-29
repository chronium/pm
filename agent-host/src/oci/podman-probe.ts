import { spawnSync } from 'node:child_process';
import type { ContainerRuntimeCapability, RuntimeProfile } from '../protocol/types.js';

export interface PodmanProbeCommand {
  run(argumentsValue: readonly string[]): { status: number | null; stdout: string };
}

export interface ContainerRuntimeProbe {
  inspect(profiles: readonly RuntimeProfile[]): ContainerRuntimeCapability;
}

export class ContainerRuntimeUnavailableError extends Error {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'ContainerRuntimeUnavailableError';
  }
}

class NodePodmanProbeCommand implements PodmanProbeCommand {
  run(argumentsValue: readonly string[]): { status: number | null; stdout: string } {
    const result = spawnSync('podman', [...argumentsValue], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
      timeout: 5_000,
      env: safePodmanEnvironment(),
    });
    return { status: result.status, stdout: result.stdout };
  }
}

export class CommandPodmanProbe implements ContainerRuntimeProbe {
  constructor(
    private readonly command: PodmanProbeCommand = new NodePodmanProbeCommand(),
    private readonly platform: () => NodeJS.Platform = () => process.platform,
  ) {}

  inspect(profiles: readonly RuntimeProfile[]): ContainerRuntimeCapability {
    if (this.platform() !== 'linux')
      unavailable('container_runtime_unsupported_platform', 'Podman runners require Linux.');

    const version = this.json(['version', '--format', 'json'], 'Podman version');
    const info = this.json(['info', '--format', 'json'], 'Podman host information');
    const client = objectField(version, 'Client', 'Podman client');
    const host = objectField(info, 'host', 'Podman host');
    const security = objectField(host, 'security', 'Podman security');
    const versionValue = stringField(client, 'Version', 'Podman version');
    const major = Number(versionValue.split('.')[0]);
    if (!Number.isSafeInteger(major) || major < 5)
      unavailable('container_runtime_version_unsupported', 'Podman 5 or newer is required.');
    if (security['rootless'] !== true)
      unavailable('container_runtime_not_rootless', 'Podman must run rootless.');
    if (host['cgroupVersion'] !== 'v2' || host['cgroupManager'] !== 'systemd')
      unavailable(
        'container_runtime_cgroups_unavailable',
        'Rootless Podman requires cgroup v2 with the systemd manager.',
      );
    if (security['seccompEnabled'] !== true)
      unavailable('container_runtime_seccomp_unavailable', 'Podman seccomp must be enabled.');
    if (security['selinuxEnabled'] === true)
      unavailable(
        'container_runtime_lsm_unsupported',
        'SELinux runtime labeling is deferred and cannot be disabled implicitly.',
      );

    for (const profile of profiles) {
      const image = this.command.run(['image', 'exists', profile.imageReference]);
      if (image.status !== 0)
        unavailable(
          'container_runtime_image_missing',
          `Installed runtime image for profile ${profile.profileId} is unavailable.`,
        );
    }

    return {
      engineId: 'podman',
      version: versionValue,
      rootless: true,
      cgroupVersion: 'v2',
      cgroupManager: 'systemd',
      seccompEnabled: true,
      selinuxEnabled: false,
      appArmorEnabled: security['apparmorEnabled'] === true,
    };
  }

  private json(argumentsValue: readonly string[], name: string): Record<string, unknown> {
    const result = this.command.run(argumentsValue);
    if (result.status !== 0)
      unavailable('container_runtime_unavailable', `${name} could not be queried.`);
    try {
      const parsed = JSON.parse(result.stdout) as unknown;
      if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error();
      return parsed as Record<string, unknown>;
    } catch {
      unavailable('container_runtime_invalid_response', `${name} returned invalid JSON.`);
    }
  }
}

function safePodmanEnvironment(): NodeJS.ProcessEnv {
  const result: NodeJS.ProcessEnv = {};
  for (const name of ['HOME', 'PATH', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME', 'XDG_RUNTIME_DIR']) {
    const value = process.env[name];
    if (value !== undefined) result[name] = value;
  }
  return result;
}

function objectField(
  value: Record<string, unknown>,
  key: string,
  name: string,
): Record<string, unknown> {
  const field = value[key];
  if (field === null || typeof field !== 'object' || Array.isArray(field))
    unavailable('container_runtime_invalid_response', `${name} is invalid.`);
  return field as Record<string, unknown>;
}

function stringField(value: Record<string, unknown>, key: string, name: string): string {
  const field = value[key];
  if (typeof field !== 'string' || field.length === 0 || field.length > 64)
    unavailable('container_runtime_invalid_response', `${name} is invalid.`);
  return field;
}

function unavailable(code: string, message: string): never {
  throw new ContainerRuntimeUnavailableError(code, message);
}
