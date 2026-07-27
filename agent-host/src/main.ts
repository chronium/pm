#!/usr/bin/env node
import { parseHostConfig, helpText, type HostConfig } from './config.js';
import { JsonLogger } from './logging.js';
import { RunStore } from './persistence/run-store.js';
import { RetentionService } from './retention.js';
import { CredentialStore } from './auth/credential-store.js';
import { certificateFingerprint, loadTlsMaterial } from './auth/tls.js';
import { generatePairingCode, hashPairingCode } from './auth/crypto.js';
import { CapabilityService, loadCapabilityManifest } from './capabilities.js';
import { AgentHostServer } from './server.js';

const retentionIntervalMilliseconds = 60 * 60 * 1000;
const pairingLifetimeMilliseconds = 10 * 60 * 1000;

async function main(): Promise<void> {
  const parsed = parseHostConfig(process.argv.slice(2));
  if (parsed.help) {
    process.stdout.write(helpText);
    return;
  }

  switch (parsed.command) {
    case 'pair':
      openPairingWindow(parsed.config);
      return;
    case 'revoke-client':
      revokeClient(parsed.config);
      return;
    case 'serve':
      await serve(parsed.config);
  }
}

function openPairingWindow(config: HostConfig): void {
  const store = new RunStore(config.dataRoot);
  const credentials = new CredentialStore(config.dataRoot);
  try {
    const fingerprint = certificateFingerprint(config.tlsCertificatePath!);
    const code = generatePairingCode();
    credentials.createChallenge(
      hashPairingCode(code),
      new Date(Date.now() + pairingLifetimeMilliseconds),
    );
    process.stdout.write(
      `Runner: ${store.runnerId}\nPairing code: ${code}\nTLS fingerprint: ${fingerprint}\nExpires in: 10 minutes\n`,
    );
  } finally {
    credentials.close();
    store.close();
  }
}

function revokeClient(config: HostConfig): void {
  const store = new RunStore(config.dataRoot);
  const credentials = new CredentialStore(config.dataRoot);
  try {
    const revoked = credentials.revokeClient();
    process.stdout.write(revoked ? 'Paired PM client revoked.\n' : 'No PM client is paired.\n');
  } finally {
    credentials.close();
    store.close();
  }
}

async function serve(config: HostConfig): Promise<void> {
  const logger = new JsonLogger();
  const store = new RunStore(config.dataRoot);
  const credentials = new CredentialStore(config.dataRoot);
  let timer: NodeJS.Timeout | undefined;
  let server: AgentHostServer | undefined;
  try {
    const recovery = store.recover();
    if (recovery.failed + recovery.queued > 0)
      logger.warn('runner.recovered', { recoveredRuns: recovery.failed + recovery.queued });
    const retention = new RetentionService(store, config.dataRoot, config.retentionDays, logger);
    retention.prune();
    timer = setInterval(() => retention.prune(), retentionIntervalMilliseconds);
    const tls = loadTlsMaterial(config.tlsCertificatePath!, config.tlsKeyPath!);
    const capabilities = new CapabilityService(
      store,
      loadCapabilityManifest(config.capabilityManifestPath!),
      config.maxConcurrency,
    );
    server = new AgentHostServer({
      listenAddress: config.listenAddress!,
      port: config.port,
      tls,
      runnerId: store.runnerId,
      credentials,
      capabilities,
      logger,
    });
    await server.start();
    logger.info('runner.ready', {
      queueDepth: store.queueDepth(),
      activeRuns: store.activeRunCount(),
    });
    await waitForShutdown();
  } finally {
    if (timer !== undefined) clearInterval(timer);
    if (server !== undefined) await server.stop();
    credentials.close();
    store.close();
    logger.info('runner.stopped');
  }
}

function waitForShutdown(): Promise<void> {
  return new Promise((resolve) => {
    const stop = (): void => resolve();
    process.once('SIGINT', stop);
    process.once('SIGTERM', stop);
  });
}

main().catch(() => {
  new JsonLogger().error('runner.start_failed', { errorCode: 'runner_start_failed' });
  process.exitCode = 1;
});
