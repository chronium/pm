import { isIP } from 'node:net';
import { resolve } from 'node:path';

export type HostCommand = 'serve' | 'pair' | 'revoke-client';

export interface HostConfig {
  dataRoot: string;
  maxConcurrency: number;
  queueCapacity: number;
  retentionDays: number;
  minimumFreeDiskBytes: number;
  listenAddress: string | null;
  port: number;
  tlsCertificatePath: string | null;
  tlsKeyPath: string | null;
  capabilityManifestPath: string | null;
}

export interface ParsedHostConfig {
  command: HostCommand;
  config: HostConfig;
  help: boolean;
}

const defaults = {
  dataRoot: '/var/lib/pm-runner',
  maxConcurrency: 1,
  queueCapacity: 32,
  retentionDays: 30,
  minimumFreeDiskBytes: 5 * 1024 * 1024 * 1024,
  port: 7443,
} as const;

const environmentNames = {
  dataRoot: 'PM_AGENT_HOST_DATA_ROOT',
  maxConcurrency: 'PM_AGENT_HOST_MAX_CONCURRENCY',
  queueCapacity: 'PM_AGENT_HOST_QUEUE_CAPACITY',
  retentionDays: 'PM_AGENT_HOST_RETENTION_DAYS',
  minimumFreeDiskBytes: 'PM_AGENT_HOST_MIN_FREE_DISK_BYTES',
  listenAddress: 'PM_AGENT_HOST_LISTEN_ADDRESS',
  port: 'PM_AGENT_HOST_PORT',
  tlsCertificatePath: 'PM_AGENT_HOST_TLS_CERT_PATH',
  tlsKeyPath: 'PM_AGENT_HOST_TLS_KEY_PATH',
  capabilityManifestPath: 'PM_AGENT_HOST_CAPABILITIES_PATH',
} as const;

export function parseHostConfig(
  args: readonly string[],
  environment: NodeJS.ProcessEnv = process.env,
): ParsedHostConfig {
  const first = args[0];
  const command: HostCommand =
    first === 'serve' || first === 'pair' || first === 'revoke-client' ? first : 'serve';
  const optionArgs = command === first ? args.slice(1) : args;
  const values = {
    dataRoot: environment[environmentNames.dataRoot] ?? defaults.dataRoot,
    maxConcurrency: environment[environmentNames.maxConcurrency] ?? String(defaults.maxConcurrency),
    queueCapacity: environment[environmentNames.queueCapacity] ?? String(defaults.queueCapacity),
    retentionDays: environment[environmentNames.retentionDays] ?? String(defaults.retentionDays),
    minimumFreeDiskBytes:
      environment[environmentNames.minimumFreeDiskBytes] ?? String(defaults.minimumFreeDiskBytes),
    listenAddress: environment[environmentNames.listenAddress] ?? null,
    port: environment[environmentNames.port] ?? String(defaults.port),
    tlsCertificatePath: environment[environmentNames.tlsCertificatePath] ?? null,
    tlsKeyPath: environment[environmentNames.tlsKeyPath] ?? null,
    capabilityManifestPath: environment[environmentNames.capabilityManifestPath] ?? null,
  };
  const seen = new Set<string>();
  let help = false;

  for (let index = 0; index < optionArgs.length; index += 1) {
    const argument = optionArgs[index];
    if (argument === '--help' || argument === '-h') {
      help = true;
      continue;
    }

    const option = readOption(optionArgs, index);
    if (option === null) throw new Error(`Unknown option: ${argument ?? ''}.`);
    index = option.nextIndex;
    if (seen.has(option.name)) throw new Error(`Option ${option.name} may only be specified once.`);
    seen.add(option.name);

    switch (option.name) {
      case '--data-root':
        values.dataRoot = option.value;
        break;
      case '--max-concurrency':
        values.maxConcurrency = option.value;
        break;
      case '--queue-capacity':
        values.queueCapacity = option.value;
        break;
      case '--retention-days':
        values.retentionDays = option.value;
        break;
      case '--min-free-disk-bytes':
        values.minimumFreeDiskBytes = option.value;
        break;
      case '--listen-address':
        values.listenAddress = option.value;
        break;
      case '--port':
        values.port = option.value;
        break;
      case '--tls-cert':
        values.tlsCertificatePath = option.value;
        break;
      case '--tls-key':
        values.tlsKeyPath = option.value;
        break;
      case '--capabilities':
        values.capabilityManifestPath = option.value;
        break;
      default:
        throw new Error(`Unknown option: ${option.name}.`);
    }
  }

  if (!values.dataRoot.startsWith('/'))
    throw new Error('The agent-host data root must be absolute.');
  const config: HostConfig = {
    dataRoot: resolve(values.dataRoot),
    maxConcurrency: positiveInteger(values.maxConcurrency, '--max-concurrency'),
    queueCapacity: positiveInteger(values.queueCapacity, '--queue-capacity'),
    retentionDays: nonNegativeInteger(values.retentionDays, '--retention-days'),
    minimumFreeDiskBytes: nonNegativeInteger(values.minimumFreeDiskBytes, '--min-free-disk-bytes'),
    listenAddress: values.listenAddress,
    port: portNumber(values.port),
    tlsCertificatePath: absoluteOptionalPath(values.tlsCertificatePath, '--tls-cert'),
    tlsKeyPath: absoluteOptionalPath(values.tlsKeyPath, '--tls-key'),
    capabilityManifestPath: absoluteOptionalPath(values.capabilityManifestPath, '--capabilities'),
  };

  if (!help) validateCommandConfig(command, config);
  return { command, config, help };
}

export const helpText = `Usage: pm-agent-host <serve|pair|revoke-client> [options]

Commands:
  serve          Start the authenticated HTTPS runner service
  pair           Open a one-use pairing window and print its code and TLS fingerprint
  revoke-client  Remove the paired PM client using local runner access

Options:
  --data-root <path>          Host-owned state directory (default: /var/lib/pm-runner)
  --max-concurrency <count>  Maximum active runs (default: 1)
  --queue-capacity <count>   Maximum queued runs (default: 32)
  --retention-days <days>    Terminal run retention; 0 disables pruning (default: 30)
  --min-free-disk-bytes <n>  Stop runs below this host free-space floor (default: 5368709120)
  --listen-address <ip>      Explicit non-wildcard HTTPS interface
  --port <port>              HTTPS port (default: 7443)
  --tls-cert <path>          Operator-provided PEM certificate
  --tls-key <path>           Protected PEM private key
  --capabilities <path>      Static runner capability manifest
  --help                     Show this help
`;

function validateCommandConfig(command: HostCommand, config: HostConfig): void {
  if (command === 'serve') {
    if (config.listenAddress === null) throw new Error('--listen-address is required for serve.');
    if (isIP(config.listenAddress) === 0)
      throw new Error('--listen-address must be an explicit IP address.');
    if (config.listenAddress === '0.0.0.0' || config.listenAddress === '::')
      throw new Error('--listen-address cannot be a wildcard interface.');
    if (config.tlsCertificatePath === null) throw new Error('--tls-cert is required for serve.');
    if (config.tlsKeyPath === null) throw new Error('--tls-key is required for serve.');
    if (config.capabilityManifestPath === null)
      throw new Error('--capabilities is required for serve.');
  }
  if (command === 'pair' && config.tlsCertificatePath === null)
    throw new Error('--tls-cert is required for pair.');
}

interface OptionValue {
  name: string;
  value: string;
  nextIndex: number;
}

function readOption(args: readonly string[], index: number): OptionValue | null {
  const argument = args[index];
  if (argument === undefined || !argument.startsWith('--')) return null;
  const equals = argument.indexOf('=');
  if (equals >= 0) {
    const value = argument.slice(equals + 1).trim();
    if (value.length === 0)
      throw new Error(`Option ${argument.slice(0, equals)} requires a value.`);
    return { name: argument.slice(0, equals), value, nextIndex: index };
  }

  const value = args[index + 1];
  if (value === undefined || value.startsWith('--'))
    throw new Error(`Option ${argument} requires a value.`);
  return { name: argument, value: value.trim(), nextIndex: index + 1 };
}

function positiveInteger(value: string, name: string): number {
  const result = Number(value);
  if (!Number.isSafeInteger(result) || result <= 0)
    throw new Error(`${name} must be a positive integer.`);
  return result;
}

function nonNegativeInteger(value: string, name: string): number {
  const result = Number(value);
  if (!Number.isSafeInteger(result) || result < 0)
    throw new Error(`${name} must be a non-negative integer.`);
  return result;
}

function portNumber(value: string): number {
  const result = positiveInteger(value, '--port');
  if (result > 65_535) throw new Error('--port must be between 1 and 65535.');
  return result;
}

function absoluteOptionalPath(value: string | null, name: string): string | null {
  if (value === null) return null;
  if (!value.startsWith('/')) throw new Error(`${name} must be absolute.`);
  return resolve(value);
}
