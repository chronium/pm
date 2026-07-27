#!/usr/bin/env node
import { parseHostConfig, helpText } from './config.js';
import { JsonLogger } from './logging.js';
import { RunStore } from './persistence/run-store.js';
import { RetentionService } from './retention.js';

const retentionIntervalMilliseconds = 60 * 60 * 1000;

async function main(): Promise<void> {
  const parsed = parseHostConfig(process.argv.slice(2));
  if (parsed.help) {
    process.stdout.write(helpText);
    return;
  }

  const logger = new JsonLogger();
  const store = new RunStore(parsed.config.dataRoot);
  const recovery = store.recover();
  if (recovery.failed + recovery.queued > 0)
    logger.warn('runner.recovered', { recoveredRuns: recovery.failed + recovery.queued });

  const retention = new RetentionService(
    store,
    parsed.config.dataRoot,
    parsed.config.retentionDays,
    logger,
  );
  retention.prune();
  const timer = setInterval(() => retention.prune(), retentionIntervalMilliseconds);
  logger.info('runner.ready', {
    queueDepth: store.queueDepth(),
    activeRuns: 0,
  });

  await waitForShutdown();
  clearInterval(timer);
  store.close();
  logger.info('runner.stopped');
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
