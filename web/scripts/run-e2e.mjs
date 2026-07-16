import { spawn } from 'node:child_process';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

const mode = process.argv[2] === 'embedded' ? 'embedded' : 'dev';
const extraArguments = process.argv.slice(mode === 'embedded' ? 3 : 2);
const executable = join(
  'node_modules',
  '.bin',
  process.platform === 'win32' ? 'playwright.cmd' : 'playwright',
);
const env = {
  ...process.env,
  PM_E2E_MODE: mode,
  PM_E2E_ROOT: process.env.PM_E2E_ROOT ?? join(tmpdir(), `pm-e2e-${process.pid}`),
};
const child = spawn(executable, ['test', ...extraArguments], { stdio: 'inherit', env });
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
child.once('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
