#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptRoot = dirname(fileURLToPath(import.meta.url));
const defaultRepositoryRoot = resolve(scriptRoot, '../..');
const digestToken = '__PM_ACTION_IMAGE_DIGEST__';
const digestPattern = /^sha256:[0-9a-f]{64}$/;
const versionPattern = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/;

function fail(message) {
  throw new Error(message);
}

function parseScalar(value) {
  if (value === 'null' || value === '~') return null;
  if (value.startsWith('"')) return JSON.parse(value);
  if (value.startsWith("'") && value.endsWith("'")) {
    return value.slice(1, -1).replaceAll("''", "'");
  }
  return value;
}

export function parseTransition(text) {
  const values = new Map();
  for (const line of text.split(/\r?\n/)) {
    if (line.length === 0 || /^\s/.test(line) || line.startsWith('#')) continue;
    const separator = line.indexOf(':');
    if (separator < 1) continue;
    values.set(line.slice(0, separator), parseScalar(line.slice(separator + 1).trim()));
  }

  const transition = {
    schemaVersion: Number(values.get('schemaVersion')),
    at: values.get('at'),
    kind: values.get('kind'),
    fromVersion: values.get('fromVersion'),
    toVersion: values.get('toVersion'),
    source: values.get('source') ?? null,
    reason: values.get('reason') ?? null,
  };
  if (transition.schemaVersion !== 1) fail('Release transition schemaVersion must be 1.');
  if (!['task', 'milestone', 'major'].includes(transition.kind)) {
    fail(`Unsupported release transition kind ${transition.kind}.`);
  }
  if (!versionPattern.test(transition.fromVersion) || !versionPattern.test(transition.toVersion)) {
    fail('Release transition contains a non-canonical version.');
  }
  if (typeof transition.at !== 'string' || transition.at.length === 0) {
    fail('Release transition timestamp is required.');
  }
  if (transition.kind === 'major') {
    if (transition.source !== null || typeof transition.reason !== 'string' || transition.reason.length === 0) {
      fail('Major release transitions require a reason and no source.');
    }
  } else if (typeof transition.source !== 'string' || transition.source.length === 0) {
    fail(`${transition.kind} release transitions require a source.`);
  }
  return transition;
}

export function channelsFor(transition) {
  const channels = ['latest'];
  if (transition.kind === 'milestone') {
    const [major, , patch] = transition.toVersion.split('.').map(Number);
    if (patch !== 0) fail('Milestone release transitions must reset patch to zero.');
    channels.push(`v${major}`);
  }
  return channels;
}

export function renderPromotion({ template, version, transition, sourceRevision, imageDigest }) {
  if (!versionPattern.test(version)) fail(`Invalid PM version ${version}.`);
  if (transition.toVersion !== version) fail('Transition target does not match the PM version.');
  if (!/^[0-9a-f]{40}$/.test(sourceRevision)) fail('Source revision must be a full Git commit SHA.');
  if (!digestPattern.test(imageDigest)) fail('Image digest must be a canonical SHA-256 digest.');
  const tokenCount = template.split(digestToken).length - 1;
  if (tokenCount !== 1) fail('Action template must contain exactly one image digest token.');

  const current = {
    schemaVersion: 1,
    pmVersion: version,
    transition,
    sourceRevision,
    image: `ghcr.io/chronium/pm@${imageDigest}`,
    imageDigest,
    channels: channelsFor(transition),
  };
  return {
    action: template.replace(digestToken, imageDigest.slice('sha256:'.length)),
    current: `${JSON.stringify(current, null, 2)}\n`,
  };
}

export function verifyPromotion({ template, action, current, version, transition, parentRevision, changedPaths }) {
  if (current.schemaVersion !== 1) fail('Current Action release schemaVersion must be 1.');
  const rendered = renderPromotion({
    template,
    version,
    transition,
    sourceRevision: current.sourceRevision,
    imageDigest: current.imageDigest,
  });
  if (current.sourceRevision !== parentRevision) {
    fail('Promotion commit must be a direct child of its candidate source revision.');
  }
  if (action !== rendered.action) fail('action.yml is not the exact digest-pinned promotion template.');
  if (`${JSON.stringify(current, null, 2)}\n` !== rendered.current) {
    fail('Current Action release metadata is not canonical.');
  }
  const expectedPaths = ['action.yml', 'github-action/release/current.json'];
  if (JSON.stringify([...changedPaths].sort()) !== JSON.stringify(expectedPaths)) {
    fail(`Promotion commit may change only ${expectedPaths.join(' and ')}.`);
  }
}

function readVersion(repositoryRoot) {
  const raw = readFileSync(resolve(repositoryRoot, '.pm/release_version.txt'), 'utf8');
  const version = raw.replace(/\r?\n$/, '');
  if (!versionPattern.test(version) || ![version, `${version}\n`, `${version}\r\n`].includes(raw)) {
    fail('.pm/release_version.txt is not canonical.');
  }
  return version;
}

function readTransition(repositoryRoot, version) {
  return parseTransition(readFileSync(resolve(repositoryRoot, `.pm/release_transitions/${version}.yaml`), 'utf8'));
}

function writeGitHubOutput(values) {
  const output = process.env.GITHUB_OUTPUT;
  if (!output) fail('GITHUB_OUTPUT is required.');
  writeFileSync(output, Object.entries(values).map(([key, value]) => `${key}=${value}\n`).join(''), { flag: 'a' });
}

function inspect(repositoryRoot) {
  const version = readVersion(repositoryRoot);
  const transition = readTransition(repositoryRoot, version);
  const currentPath = resolve(repositoryRoot, 'github-action/release/current.json');
  let phase = 'candidate';
  try {
    const current = JSON.parse(readFileSync(currentPath, 'utf8'));
    if (current.pmVersion === version) phase = 'promotion';
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  writeGitHubOutput({
    phase,
    version,
    'immutable-ref': `v${version}`,
    'transition-kind': transition.kind,
    'transition-source': transition.source ?? '',
    'stable-channel': channelsFor(transition).find((channel) => channel !== 'latest') ?? '',
  });
}

function render(repositoryRoot, outputDirectory, sourceRevision, imageDigest) {
  const version = readVersion(repositoryRoot);
  const transition = readTransition(repositoryRoot, version);
  const rendered = renderPromotion({
    template: readFileSync(resolve(repositoryRoot, 'action.template.yml'), 'utf8'),
    version,
    transition,
    sourceRevision,
    imageDigest,
  });
  mkdirSync(resolve(outputDirectory, 'github-action/release'), { recursive: true });
  writeFileSync(resolve(outputDirectory, 'action.yml'), rendered.action);
  writeFileSync(resolve(outputDirectory, 'github-action/release/current.json'), rendered.current);
}

function verify(repositoryRoot) {
  const version = readVersion(repositoryRoot);
  const transition = readTransition(repositoryRoot, version);
  const parentRevision = execFileSync('git', ['rev-parse', 'HEAD^'], { cwd: repositoryRoot, encoding: 'utf8' }).trim();
  const changedPaths = execFileSync('git', ['diff', '--name-only', 'HEAD^', 'HEAD'], {
    cwd: repositoryRoot,
    encoding: 'utf8',
  }).trim().split('\n').filter(Boolean);
  verifyPromotion({
    template: readFileSync(resolve(repositoryRoot, 'action.template.yml'), 'utf8'),
    action: readFileSync(resolve(repositoryRoot, 'action.yml'), 'utf8'),
    current: JSON.parse(readFileSync(resolve(repositoryRoot, 'github-action/release/current.json'), 'utf8')),
    version,
    transition,
    parentRevision,
    changedPaths,
  });
}

function evidence(repositoryRoot, outputPath, actionRevision) {
  const current = JSON.parse(readFileSync(resolve(repositoryRoot, 'github-action/release/current.json'), 'utf8'));
  if (!/^[0-9a-f]{40}$/.test(actionRevision)) fail('Action revision must be a full Git commit SHA.');
  const release = {
    schemaVersion: 1,
    ...current,
    actionRevision,
    immutableRef: `v${current.pmVersion}`,
  };
  writeFileSync(outputPath, `${JSON.stringify(release, null, 2)}\n`);
}

function main(argv) {
  const [command, ...args] = argv;
  const repositoryRoot = resolve(process.env.PM_REPOSITORY_ROOT ?? defaultRepositoryRoot);
  if (command === 'inspect' && args.length === 0) return inspect(repositoryRoot);
  if (command === 'render' && args.length === 3) return render(repositoryRoot, resolve(args[0]), args[1], args[2]);
  if (command === 'verify' && args.length === 0) return verify(repositoryRoot);
  if (command === 'evidence' && args.length === 2) return evidence(repositoryRoot, resolve(args[0]), args[1]);
  fail('Usage: release.mjs inspect | render <output> <source-sha> <image-digest> | verify | evidence <output> <action-sha>');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(`PM Action release: ${error.message}`);
    process.exit(1);
  }
}
