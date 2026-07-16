import { spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(webRoot, '..');
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
const socket = process.platform === 'win32' ? 'socket.cmd' : 'socket';
// npm ci cannot change the reviewed lockfile; allow Socket to install that exact graph while still reporting risks.
const lockedInstallEnv = { ...process.env, SOCKET_CLI_ACCEPT_RISKS: '1' };
const steps = [
  [socket, ['npm', 'ci'], webRoot, lockedInstallEnv],
  [npm, ['run', 'frontend:validate'], webRoot],
  ['dotnet', ['build', 'PM.slnx', '-m:1', '--no-restore'], repositoryRoot],
  ['dotnet', ['test', 'PM.slnx', '-m:1', '--no-restore'], repositoryRoot],
  [
    'dotnet',
    [
      'publish',
      'PM/PM.csproj',
      '-m:1',
      '--no-restore',
      '-p:EmbedAngularAssets=true',
      '-o',
      'artifacts/release',
    ],
    repositoryRoot,
  ],
  [npm, ['run', 'e2e:embedded'], webRoot],
];

for (const [command, args, cwd, env = process.env] of steps) {
  const result = spawnSync(command, args, { cwd, stdio: 'inherit', env });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}
