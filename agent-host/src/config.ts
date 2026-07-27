import { resolve } from 'node:path';

export interface HostConfig {
  dataRoot: string;
  maxConcurrency: number;
  queueCapacity: number;
  retentionDays: number;
}

export interface ParsedHostConfig {
  config: HostConfig;
  help: boolean;
}

const defaults: HostConfig = {
  dataRoot: '/var/lib/pm-runner',
  maxConcurrency: 1,
  queueCapacity: 32,
  retentionDays: 30,
};

const environmentNames = {
  dataRoot: 'PM_AGENT_HOST_DATA_ROOT',
  maxConcurrency: 'PM_AGENT_HOST_MAX_CONCURRENCY',
  queueCapacity: 'PM_AGENT_HOST_QUEUE_CAPACITY',
  retentionDays: 'PM_AGENT_HOST_RETENTION_DAYS',
} as const;

export function parseHostConfig(
  args: readonly string[],
  environment: NodeJS.ProcessEnv = process.env,
): ParsedHostConfig {
  const values = {
    dataRoot: environment[environmentNames.dataRoot] ?? defaults.dataRoot,
    maxConcurrency: environment[environmentNames.maxConcurrency] ?? String(defaults.maxConcurrency),
    queueCapacity: environment[environmentNames.queueCapacity] ?? String(defaults.queueCapacity),
    retentionDays: environment[environmentNames.retentionDays] ?? String(defaults.retentionDays),
  };
  const seen = new Set<string>();
  let help = false;

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === '--help' || argument === '-h') {
      help = true;
      continue;
    }

    const option = readOption(args, index);
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
      default:
        throw new Error(`Unknown option: ${option.name}.`);
    }
  }

  if (!values.dataRoot.startsWith('/'))
    throw new Error('The agent-host data root must be absolute.');

  return {
    help,
    config: {
      dataRoot: resolve(values.dataRoot),
      maxConcurrency: positiveInteger(values.maxConcurrency, '--max-concurrency'),
      queueCapacity: positiveInteger(values.queueCapacity, '--queue-capacity'),
      retentionDays: nonNegativeInteger(values.retentionDays, '--retention-days'),
    },
  };
}

export const helpText = `Usage: pm-agent-host [options]

Options:
  --data-root <path>          Host-owned state directory (default: /var/lib/pm-runner)
  --max-concurrency <count>  Maximum active runs (default: 1)
  --queue-capacity <count>   Maximum queued runs (default: 32)
  --retention-days <days>    Terminal run retention; 0 disables pruning (default: 30)
  --help                     Show this help
`;

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
