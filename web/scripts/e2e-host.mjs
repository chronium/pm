import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { access, mkdir, rm } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { configRoot, e2eRoot, projectRoot, resetFixture } from './e2e-fixture.mjs';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(webRoot, '..');
const mode = process.argv[2] ?? 'dev';
const children = new Set();
let stopping = false;

await rm(e2eRoot, { recursive: true, force: true });
await mkdir(e2eRoot, { recursive: true });
await resetFixture(process.env.PM_E2E_FIXTURE ?? 'small');

const idServer = createServer((request, response) => {
  if (request.url === '/health') {
    response.writeHead(200).end('ok');
    return;
  }
  if (!request.headers['pm-user-id'] || !request.headers['pm-signature']) {
    response.writeHead(401).end();
    return;
  }
  const match = request.url?.match(/\/tracks\/([^/]+)\/(nextid|peekid)$/);
  if (!match) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, { 'content-type': 'application/json' });
  response.end(JSON.stringify({ id: 1000 }));
});
await new Promise((resolveReady, reject) => {
  idServer.once('error', reject);
  idServer.listen(51238, '127.0.0.1', resolveReady);
});

const env = {
  ...process.env,
  CI: process.env.CI ?? 'true',
  XDG_CONFIG_HOME: configRoot,
  HOME: configRoot,
  PM_IDENTITY_PATH: join(configRoot, 'pm', 'identity.json'),
};
if (mode === 'embedded') {
  const dll = resolve(
    process.env.PM_E2E_PUBLISHED_DLL ?? join(repositoryRoot, 'artifacts/release/PM.dll'),
  );
  await access(dll);
  start('dotnet', [dll, 'web', '--port', '51239'], projectRoot, env);
} else {
  const dll = join(repositoryRoot, 'PM/bin/Debug/net10.0/PM.dll');
  await access(dll);
  start('dotnet', [dll, 'web', '--api'], projectRoot, env);
  start(
    process.platform === 'win32' ? 'npm.cmd' : 'npm',
    ['start', '--', '--host', '127.0.0.1', '--port', '4200'],
    webRoot,
    env,
  );
}

await new Promise(() => {});

function start(command, args, cwd, childEnv) {
  const child = spawn(command, args, {
    cwd,
    env: childEnv,
    stdio: 'inherit',
  });
  children.add(child);
  child.once('exit', (code, signal) => {
    children.delete(child);
    if (!stopping) {
      console.error(`${command} exited before the E2E run completed (${code ?? signal}).`);
      void stop(1);
    }
  });
}

async function stop(exitCode) {
  if (stopping) return;
  stopping = true;
  idServer.close();
  for (const child of children) {
    try {
      child.kill('SIGTERM');
    } catch {}
  }
  await rm(e2eRoot, { recursive: true, force: true });
  process.exit(exitCode);
}

process.on('SIGINT', () => void stop(130));
process.on('SIGTERM', () => void stop(0));
process.on('uncaughtException', (error) => {
  console.error(error);
  void stop(1);
});
