import assert from 'node:assert/strict';
import test from 'node:test';
import { parseHostConfig } from '../src/config.js';
import { JsonLogger, type LogFields } from '../src/logging.js';

test('host configuration uses defaults, environment, then CLI precedence', () => {
  const defaults = parseHostConfig(['--help'], {});
  assert.deepEqual(defaults.config, {
    dataRoot: '/var/lib/pm-runner',
    maxConcurrency: 1,
    queueCapacity: 32,
    retentionDays: 30,
    minimumFreeDiskBytes: 5 * 1024 * 1024 * 1024,
    listenAddress: null,
    port: 7443,
    tlsCertificatePath: null,
    tlsKeyPath: null,
    capabilityManifestPath: null,
  });

  const configured = parseHostConfig(
    [
      'serve',
      '--data-root',
      '/cli/root',
      '--max-concurrency=3',
      '--retention-days',
      '0',
      '--min-free-disk-bytes',
      '1073741824',
      '--listen-address',
      '100.64.0.2',
      '--tls-cert',
      '/tls/cert.pem',
      '--tls-key',
      '/tls/key.pem',
      '--capabilities',
      '/runner/capabilities.json',
    ],
    {
      PM_AGENT_HOST_DATA_ROOT: '/environment/root',
      PM_AGENT_HOST_MAX_CONCURRENCY: '2',
      PM_AGENT_HOST_QUEUE_CAPACITY: '64',
      PM_AGENT_HOST_RETENTION_DAYS: '14',
    },
  );
  assert.deepEqual(configured.config, {
    dataRoot: '/cli/root',
    maxConcurrency: 3,
    queueCapacity: 64,
    retentionDays: 0,
    minimumFreeDiskBytes: 1_073_741_824,
    listenAddress: '100.64.0.2',
    port: 7443,
    tlsCertificatePath: '/tls/cert.pem',
    tlsKeyPath: '/tls/key.pem',
    capabilityManifestPath: '/runner/capabilities.json',
  });
});

test('host configuration rejects repository-relative and invalid settings', () => {
  assert.throws(() => parseHostConfig(['serve', '--data-root', '.'], {}), /must be absolute/);
  assert.throws(() => parseHostConfig(['serve', '--max-concurrency', '0'], {}), /positive integer/);
  assert.throws(
    () => parseHostConfig(['serve', '--retention-days', '-1'], {}),
    /non-negative integer/,
  );
  assert.throws(
    () => parseHostConfig(['--queue-capacity', '2', '--queue-capacity', '3'], {}),
    /only be specified once/,
  );
  assert.throws(() => parseHostConfig(['serve'], {}), /listen-address is required/);
  assert.throws(
    () =>
      parseHostConfig(
        [
          'serve',
          '--listen-address',
          '0.0.0.0',
          '--tls-cert',
          '/cert',
          '--tls-key',
          '/key',
          '--capabilities',
          '/capabilities',
        ],
        {},
      ),
    /wildcard/,
  );
});

test('structured logger keeps only safe whitelisted fields', () => {
  const lines: string[] = [];
  const logger = new JsonLogger(
    (line) => lines.push(line),
    () => new Date('2026-07-27T10:00:00.000Z'),
  );
  const unsafeFields = {
    runId: 'run-1\n',
    state: 'queued',
    repository: '/Users/example/private/repository',
    token: 'secret-value',
  } as unknown as LogFields;

  logger.info('runner.ready\n', unsafeFields);

  assert.equal(lines.length, 1);
  const output = lines[0] ?? '';
  assert.doesNotMatch(output, /Users|repository|secret-value|\\n/);
  assert.deepEqual(JSON.parse(output), {
    timestamp: '2026-07-27T10:00:00.000Z',
    level: 'info',
    event: 'runner.ready',
    runId: 'run-1',
    state: 'queued',
  });
});
