import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import net from 'node:net';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import openapiTS, { astToString, COMMENT_HEADER } from 'openapi-typescript';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(webRoot, '..');
const outputPath = resolve(webRoot, 'src/app/api/generated/pm-api.ts');
const check = process.argv.includes('--check');

function run(command, args, options = {}) {
  return new Promise((resolveProcess, reject) => {
    const child = spawn(command, args, {
      cwd: repositoryRoot,
      stdio: 'inherit',
      ...options,
    });
    child.once('error', reject);
    child.once('exit', (code, signal) => {
      if (code === 0) resolveProcess();
      else reject(new Error(`${command} exited with ${code ?? signal}`));
    });
  });
}

function availablePort() {
  return new Promise((resolvePort, reject) => {
    const server = net.createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      const port = typeof address === 'object' && address ? address.port : 0;
      server.close((error) => (error ? reject(error) : resolvePort(port)));
    });
  });
}

async function fetchOpenApi(url, server) {
  const deadline = Date.now() + 15_000;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) throw new Error(`PM web exited with ${server.exitCode}`);
    try {
      const response = await fetch(url);
      if (response.ok) return response.json();
    } catch {
      // The loopback server may still be starting.
    }
    await new Promise((resolveDelay) => setTimeout(resolveDelay, 100));
  }
  throw new Error(`Timed out waiting for ${url}`);
}

function stop(server) {
  if (server.exitCode !== null) return Promise.resolve();
  return new Promise((resolveStop) => {
    server.once('exit', resolveStop);
    server.kill('SIGTERM');
    setTimeout(() => {
      if (server.exitCode === null) server.kill('SIGKILL');
    }, 5_000).unref();
  });
}

await run('dotnet', ['build', 'PM.slnx', '-m:1', '--no-restore']);
const port = await availablePort();
const server = spawn(
  'dotnet',
  ['PM/bin/Debug/net10.0/PM.dll', 'web', '--api', '--port', String(port)],
  {
    cwd: repositoryRoot,
    stdio: ['ignore', 'inherit', 'inherit'],
  },
);

try {
  const schema = await fetchOpenApi(`http://127.0.0.1:${port}/openapi/v1.json`, server);
  const generated = `${COMMENT_HEADER}${astToString(await openapiTS(schema))}`;

  if (check) {
    const current = await readFile(outputPath, 'utf8').catch(() => '');
    if (current !== generated) {
      throw new Error('Generated PM API types are out of date. Run npm run api:types.');
    }
    console.log('Generated PM API types are current.');
  } else {
    await mkdir(dirname(outputPath), { recursive: true });
    await writeFile(outputPath, generated);
    console.log(`Generated ${outputPath}`);
  }
} finally {
  await stop(server);
}
