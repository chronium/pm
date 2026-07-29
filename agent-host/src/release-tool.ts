#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { computeProfileRevision } from './protocol/canonical-json.js';
import type { CapabilityManifest, RuntimeProfile } from './protocol/types.js';
import { parseCapabilityManifest } from './protocol/validation.js';
import type { ReleaseInfo } from './release-info.js';
import { parseReleaseInfo } from './release-info.js';

export interface ReleaseArtifact {
  fileName: string;
  byteLength: number;
  sha256: string;
}

export interface ReleaseArtifactManifest {
  schemaVersion: 1;
  packageVersion: string;
  sourceRevision: string;
  builtAt: string;
  platform: 'linux-x64';
  nodeVersion: string;
  protocolVersion: '1.1';
  workerImageReference: string;
  workerImageDigest: string;
  artifacts: ReleaseArtifact[];
}

export function createReleaseInfo(
  packageVersion: string,
  sourceRevision: string,
  builtAt: string,
  workerImageReference: string,
  workerImageDigest: string,
): ReleaseInfo {
  return parseReleaseInfo({
    schemaVersion: 1,
    packageVersion,
    sourceRevision,
    builtAt,
    platform: 'linux-x64',
    nodeVersion: '26.5.0',
    protocolVersion: '1.1',
    workerImageReference,
    workerImageDigest,
  });
}

export function materializeCapabilities(
  template: unknown,
  workerImageReference: string,
): CapabilityManifest {
  if (template === null || typeof template !== 'object' || Array.isArray(template))
    throw new Error('Capability template must be an object.');
  const value = structuredClone(template) as Record<string, unknown>;
  const profiles = value['runtimeProfiles'];
  if (!Array.isArray(profiles) || profiles.length !== 1)
    throw new Error('The v1 release requires exactly one runtime profile.');
  const profile = profiles[0];
  if (profile === null || typeof profile !== 'object' || Array.isArray(profile))
    throw new Error('Capability template runtime profile is invalid.');
  const runtime = profile as RuntimeProfile;
  runtime.imageReference = workerImageReference;
  runtime.revision = computeProfileRevision(runtime);
  return parseCapabilityManifest(value);
}

export function createArtifactManifest(
  release: ReleaseInfo,
  paths: readonly string[],
): ReleaseArtifactManifest {
  if (
    release.platform !== 'linux-x64' ||
    release.workerImageReference === null ||
    release.workerImageDigest === null
  )
    throw new Error('Artifact manifests require a packaged Linux release.');
  const artifacts = paths
    .map((path) => {
      const bytes = readFileSync(path);
      return {
        fileName: basename(path),
        byteLength: bytes.length,
        sha256: createHash('sha256').update(bytes).digest('hex'),
      };
    })
    .sort((left, right) => left.fileName.localeCompare(right.fileName));
  return {
    schemaVersion: 1,
    packageVersion: release.packageVersion,
    sourceRevision: release.sourceRevision,
    builtAt: release.builtAt,
    platform: 'linux-x64',
    nodeVersion: release.nodeVersion,
    protocolVersion: '1.1',
    workerImageReference: release.workerImageReference,
    workerImageDigest: release.workerImageDigest,
    artifacts,
  };
}

async function main(): Promise<void> {
  const [command, ...args] = process.argv.slice(2);
  if (command === 'capabilities' && args.length === 3) {
    const [templatePath, imageReference, outputPath] = args as [string, string, string];
    const template = JSON.parse(readFileSync(templatePath, 'utf8')) as unknown;
    writeJson(outputPath, materializeCapabilities(template, imageReference));
    return;
  }
  if (command === 'release-info' && args.length === 5) {
    const [version, revision, builtAt, imageReference, outputPath] = args as [
      string,
      string,
      string,
      string,
      string,
    ];
    const digest = digestFromReference(imageReference);
    writeJson(outputPath, createReleaseInfo(version, revision, builtAt, imageReference, digest));
    return;
  }
  if (command === 'artifact-manifest' && args.length >= 3) {
    const [releaseInfoPath, outputDirectory, ...artifactPaths] = args as [
      string,
      string,
      ...string[],
    ];
    const release = parseReleaseInfo(JSON.parse(readFileSync(releaseInfoPath, 'utf8')) as unknown);
    const manifest = createArtifactManifest(release, artifactPaths);
    writeJson(join(outputDirectory, 'release-manifest.json'), manifest);
    writeFileSync(
      join(outputDirectory, 'SHA256SUMS'),
      `${manifest.artifacts.map((item) => `${item.sha256}  ${item.fileName}`).join('\n')}\n`,
      { mode: 0o644 },
    );
    return;
  }
  throw new Error(
    'Usage: pm-agent-release <capabilities|release-info|artifact-manifest> <arguments...>',
  );
}

function digestFromReference(reference: string): string {
  const separator = reference.lastIndexOf('@');
  const digest = separator < 0 ? '' : reference.slice(separator + 1);
  if (!/^sha256:[0-9a-f]{64}$/.test(digest))
    throw new Error('Worker image reference must include a SHA-256 digest.');
  return digest;
}

function writeJson(path: string, value: unknown): void {
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`, { mode: 0o644 });
}

if (process.argv[1]?.endsWith('/release-tool.js'))
  main().catch((error: unknown) => {
    process.stderr.write(`${error instanceof Error ? error.message : 'Release command failed.'}\n`);
    process.exitCode = 1;
  });
