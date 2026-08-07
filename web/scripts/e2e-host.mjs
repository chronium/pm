import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { access, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { configRoot, e2eRoot, projectRoot, resetFixture } from './e2e-fixture.mjs';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(webRoot, '..');
const mode = process.argv[2] ?? 'dev';
const idPort = mode === 'static' ? 0 : requiredPort('PM_E2E_ID_PORT');
const apiPort = mode === 'static' ? 0 : requiredPort('PM_E2E_API_PORT');
const uiPort = requiredPort('PM_E2E_UI_PORT');
const children = new Set();
let stopping = false;
const childProcessEnvironment = { ...process.env };
delete childProcessEnvironment.PM_E2E_MODE;

await rm(e2eRoot, { recursive: true, force: true });
await mkdir(e2eRoot, { recursive: true });
await resetFixture(process.env.PM_E2E_FIXTURE ?? 'small');

if (mode === 'static') {
  const dll = resolve(
    process.env.PM_E2E_PUBLISHED_DLL ?? join(repositoryRoot, 'artifacts/release/PM.dll'),
  );
  await access(dll);
  const siteRoot = join(e2eRoot, 'site');
  const build = spawnSync('dotnet', [dll, 'site', 'build', '--output', siteRoot], {
    cwd: projectRoot,
    env: childProcessEnvironment,
    stdio: 'inherit',
  });
  if (build.error) throw build.error;
  if (build.status !== 0) process.exit(build.status ?? 1);
  const staticServer = createServer(async (request, response) => {
    try {
      const requested = decodeURIComponent(
        new URL(request.url ?? '/', 'http://localhost').pathname,
      );
      const relative = requested === '/' ? 'index.html' : requested.replace(/^\/+/, '');
      if (relative.split('/').some((segment) => segment === '..')) {
        response.writeHead(404).end();
        return;
      }
      const body = await readFile(join(siteRoot, relative));
      response.writeHead(200, { 'content-type': contentType(relative) }).end(body);
    } catch {
      response.writeHead(404).end();
    }
  });
  await new Promise((resolveReady, reject) => {
    staticServer.once('error', reject);
    staticServer.listen(uiPort, '127.0.0.1', resolveReady);
  });
  const stopStatic = () => {
    staticServer.close();
    void rm(e2eRoot, { recursive: true, force: true }).finally(() => process.exit(0));
  };
  process.on('SIGINT', stopStatic);
  process.on('SIGTERM', stopStatic);
  await new Promise(() => {});
}

const idServer = createServer((request, response) => {
  if (request.url === '/health') {
    response.writeHead(200).end('ok');
    return;
  }
  if (!request.headers['pm-user-id'] || !request.headers['pm-signature']) {
    response.writeHead(401).end();
    return;
  }
  if (request.method === 'GET' && request.url === '/projects/playwright-project/members') {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(
      JSON.stringify({
        currentUserId: request.headers['pm-user-id'],
        currentRole: 'admin',
        members: [
          {
            userId: request.headers['pm-user-id'],
            displayName: 'Playwright user',
            publicKey: request.headers['pm-public-key'],
            role: 'admin',
          },
        ],
      }),
    );
    return;
  }
  if (request.method === 'GET' && request.url === '/projects/playwright-project/invitations') {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ invitations: [] }));
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
  idServer.listen(idPort, '127.0.0.1', resolveReady);
});

const env = {
  ...childProcessEnvironment,
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
  start('dotnet', [dll, 'web', '--port', String(uiPort)], projectRoot, env);
} else {
  const dll = join(repositoryRoot, 'PM/bin/Debug/net10.0/PM.dll');
  await access(dll);
  const proxyPath = join(e2eRoot, 'proxy.conf.json');
  await writeFile(
    proxyPath,
    `${JSON.stringify({ '/api': { target: `http://127.0.0.1:${apiPort}`, secure: false } }, null, 2)}\n`,
  );
  start('dotnet', [dll, 'web', '--api', '--port', String(apiPort)], projectRoot, env);
  start(
    join(webRoot, 'node_modules', '.bin', process.platform === 'win32' ? 'ng.cmd' : 'ng'),
    ['serve', '--proxy-config', proxyPath, '--host', '127.0.0.1', '--port', String(uiPort)],
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

function requiredPort(name) {
  const port = Number(process.env[name]);
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    throw new Error(`${name} must be set to an integer between 1 and 65535.`);
  }
  return port;
}

function contentType(path) {
  if (path.endsWith('.html')) return 'text/html; charset=utf-8';
  if (path.endsWith('.js')) return 'text/javascript; charset=utf-8';
  if (path.endsWith('.css')) return 'text/css; charset=utf-8';
  if (path.endsWith('.json')) return 'application/json; charset=utf-8';
  return 'application/octet-stream';
}
