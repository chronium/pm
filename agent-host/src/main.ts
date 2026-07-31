#!/usr/bin/env node
import { parseHostConfig, helpText, type HostConfig } from './config.js';
import { JsonLogger } from './logging.js';
import { RunStore } from './persistence/run-store.js';
import { RetentionService } from './retention.js';
import { CredentialStore } from './auth/credential-store.js';
import { certificateFingerprint, loadTlsMaterial } from './auth/tls.js';
import { generatePairingCode, hashPairingCode } from './auth/crypto.js';
import { formatPairingInstructions } from './auth/pairing.js';
import { CapabilityService, loadCapabilityManifest } from './capabilities.js';
import { AgentHostServer } from './server.js';
import { RunCoordinator } from './run-coordinator.js';
import { RunnerLayout } from './execution/layout.js';
import { RepositoryPolicy } from './execution/repository-policy.js';
import { GitWorkspaceService } from './execution/workspace.js';
import { NodePodmanClient, PodmanRuntimeDriver } from './oci/podman-runtime.js';
import { CodexAgentDriver } from './codex/agent-driver.js';
import { ValidationRunner } from './execution/validation.js';
import { ArtifactCollector } from './execution/artifacts.js';
import { DriverRunProcessor, RunScheduler } from './scheduler.js';
import { formatDoctorReport, runDoctor } from './doctor.js';
import { loadReleaseInfo } from './release-info.js';

const retentionIntervalMilliseconds = 60 * 60 * 1000;
const pairingLifetimeMilliseconds = 10 * 60 * 1000;

async function main(): Promise<void> {
  const parsed = parseHostConfig(process.argv.slice(2));
  if (parsed.help) {
    process.stdout.write(helpText);
    return;
  }

  switch (parsed.command) {
    case 'version': {
      const release = loadReleaseInfo(parsed.config.releaseManifestPath);
      process.stdout.write(
        parsed.json
          ? `${JSON.stringify(release)}\n`
          : `pm-agent-host ${release.packageVersion} (${release.sourceRevision}) protocol ${release.protocolVersion}\n`,
      );
      return;
    }
    case 'doctor': {
      const report = runDoctor(parsed.config);
      process.stdout.write(
        parsed.json ? `${JSON.stringify(report)}\n` : formatDoctorReport(report),
      );
      if (!report.ok) process.exitCode = 1;
      return;
    }
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
      formatPairingInstructions({
        runnerId: store.runnerId,
        code,
        tlsFingerprint: fingerprint,
        expiresIn: '10 minutes',
      }),
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
  let scheduler: RunScheduler | undefined;
  try {
    const release = loadReleaseInfo(config.releaseManifestPath);
    const manifest = loadCapabilityManifest(config.capabilityManifestPath!);
    const layout = new RunnerLayout(config.dataRoot);
    const repositoryPolicy = RepositoryPolicy.load(config.repositoryPolicyPath!);
    const workspace = new GitWorkspaceService(layout, repositoryPolicy, config.codexAuthPath!);
    const runtime = new PodmanRuntimeDriver(new NodePodmanClient(), {
      dataRoot: config.dataRoot,
      runnerId: store.runnerId,
      minimumFreeDiskBytes: config.minimumFreeDiskBytes,
    });
    const removedContainers = await runtime.reconcile();
    const recovery = store.recover();
    const reconciledRuns = await workspace.reconcile();
    if (recovery.failed + recovery.queued > 0)
      logger.warn('runner.recovered', {
        recoveredRuns: recovery.failed + recovery.queued,
        removedContainers,
        reconciledRuns,
      });
    const retention = new RetentionService(store, config.dataRoot, config.retentionDays, logger);
    retention.prune();
    timer = setInterval(() => retention.prune(), retentionIntervalMilliseconds);
    const tls = loadTlsMaterial(config.tlsCertificatePath!, config.tlsKeyPath!);
    const capabilities = new CapabilityService(store, manifest, config.maxConcurrency);
    const agent = new CodexAgentDriver(runtime);
    const processor = new DriverRunProcessor(store, runtime, agent, {
      workspace,
      validation: new ValidationRunner(runtime),
      artifacts: new ArtifactCollector(store, layout),
    });
    scheduler = new RunScheduler(store, processor, config.maxConcurrency);
    const runCoordinator = new RunCoordinator(store, capabilities, config.queueCapacity, scheduler);
    server = new AgentHostServer({
      listenAddress: config.listenAddress!,
      port: config.port,
      tls,
      runnerId: store.runnerId,
      credentials,
      capabilities,
      runStore: store,
      runCoordinator,
      workspace,
      logger,
      release,
    });
    await server.start();
    scheduler.start();
    logger.info('runner.ready', {
      queueDepth: store.queueDepth(),
      activeRuns: store.activeRunCount(),
    });
    await waitForShutdown();
  } finally {
    if (timer !== undefined) clearInterval(timer);
    if (server !== undefined) await server.stop();
    if (scheduler !== undefined) await scheduler.stop();
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
