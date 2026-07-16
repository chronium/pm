import { spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(webRoot, '..');
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
const socket = process.platform === 'win32' ? 'socket.cmd' : 'socket';
const steps = [
  [socket, ['npm', 'ci'], webRoot],
  ['dotnet', ['build', 'PM.slnx', '-m:1', '--no-restore'], repositoryRoot],
  ['dotnet', ['test', 'PM.slnx', '-m:1', '--no-restore'], repositoryRoot],
  [npm, ['run', 'frontend:validate'], webRoot],
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

for (const [command, args, cwd] of steps) {
  const result = spawnSync(command, args, { cwd, stdio: 'inherit', env: process.env });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}
