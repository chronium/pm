import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

const requestedMode = process.argv[2];
const mode =
  requestedMode === 'embedded' || requestedMode === 'static' || requestedMode === 'static-large'
    ? requestedMode
    : 'dev';
const extraArguments = process.argv.slice(mode === 'dev' ? 2 : 3);
const executable = join(
  'node_modules',
  '.bin',
  process.platform === 'win32' ? 'playwright.cmd' : 'playwright',
);
const ports = await allocatePorts([
  ['PM_E2E_ID_PORT', process.env.PM_E2E_ID_PORT],
  ['PM_E2E_API_PORT', process.env.PM_E2E_API_PORT],
  ['PM_E2E_UI_PORT', process.env.PM_E2E_UI_PORT],
]);
const env = {
  ...process.env,
  PM_E2E_MODE: mode === 'static-large' ? 'static' : mode,
  PM_E2E_FIXTURE: mode === 'static-large' ? 'large' : process.env.PM_E2E_FIXTURE,
  PM_E2E_ROOT: process.env.PM_E2E_ROOT ?? join(tmpdir(), `pm-e2e-${process.pid}`),
  ...ports,
};
const child = spawn(executable, ['test', ...extraArguments], { stdio: 'inherit', env });
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
child.once('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));

async function allocatePorts(requests) {
  const reservations = [];
  try {
    for (const [name, override] of requests) {
      const port = parsePort(name, override);
      const reservation = await reservePort(port);
      reservations.push({ name, port: reservation.address().port, server: reservation });
    }
    return Object.fromEntries(reservations.map(({ name, port }) => [name, String(port)]));
  } finally {
    await Promise.all(reservations.map(({ server }) => close(server)));
  }
}

function parsePort(name, value) {
  if (value === undefined) return 0;
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    throw new Error(`${name} must be an integer between 1 and 65535.`);
  }
  return port;
}

function reservePort(port) {
  return new Promise((resolveReady, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(port, '127.0.0.1', () => {
      server.removeListener('error', reject);
      resolveReady(server);
    });
  });
}

function close(server) {
  return new Promise((resolveClosed, reject) => {
    server.close((error) => (error ? reject(error) : resolveClosed()));
  });
}
