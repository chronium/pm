import { chmodSync, readFileSync, rmSync, mkdtempSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { generateKeyPairSync, sign, type KeyObject } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { computeSpecificationHash } from '../src/protocol/canonical-json.js';
import { parseCapabilityManifest, parseRunRequest } from '../src/protocol/validation.js';
import type { CapabilityManifest, RunRequest, RunState } from '../src/protocol/types.js';
import type { ContainerRuntimeProbe } from '../src/oci/podman-probe.js';
import type { RunStore } from '../src/persistence/run-store.js';
import { canonicalSignedRequest, type SignedRequestValues } from '../src/auth/crypto.js';

const fixture = JSON.parse(
  readFileSync(join(process.cwd(), '..', 'contracts/agent-runs/v1/run-request.json'), 'utf8'),
) as unknown;

export function createRequest(runId: string, runnerId?: string): RunRequest {
  const request = structuredClone(parseRunRequest(fixture));
  request.specification.runId = runId;
  if (runnerId !== undefined) request.specification.runtime.runnerId = runnerId;
  request.specificationHash = computeSpecificationHash(request.specification);
  return request;
}

export function createCapabilityManifest(): CapabilityManifest {
  const capabilities = JSON.parse(
    readFileSync(
      join(process.cwd(), '..', 'contracts/agent-runs/v1/runner-capabilities.json'),
      'utf8',
    ),
  ) as Record<string, unknown>;
  return parseCapabilityManifest({
    displayName: capabilities['displayName'],
    agentProviders: capabilities['agentProviders'],
    runtimeProfiles: capabilities['runtimeProfiles'],
  });
}

export function createRuntimeProbe(): ContainerRuntimeProbe {
  return {
    inspect: () => ({
      engineId: 'podman',
      version: '6.0.1',
      rootless: true,
      cgroupVersion: 'v2',
      cgroupManager: 'systemd',
      seccompEnabled: true,
      selinuxEnabled: false,
      appArmorEnabled: false,
    }),
  };
}

export function createTempDirectory(): { path: string; dispose: () => void } {
  const path = mkdtempSync(join(tmpdir(), 'pm-agent-host-'));
  return { path, dispose: () => rmSync(path, { recursive: true, force: true }) };
}

export interface TestIdentity {
  clientId: string;
  displayName: string;
  publicKey: string;
  privateKey: KeyObject;
}

export function createIdentity(clientId = 'usr_test'): TestIdentity {
  const keys = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  return {
    clientId,
    displayName: 'Test client',
    publicKey: keys.publicKey.export({ type: 'spki', format: 'der' }).toString('base64url'),
    privateKey: keys.privateKey,
  };
}

export function signRequest(identity: TestIdentity, values: SignedRequestValues): string {
  return sign('sha256', Buffer.from(canonicalSignedRequest(values), 'utf8'), {
    key: identity.privateKey,
    dsaEncoding: 'ieee-p1363',
  }).toString('base64url');
}

export function createTestCertificate(
  directory: string,
  name = 'runner',
): {
  certificatePath: string;
  keyPath: string;
} {
  const certificatePath = join(directory, `${name}-certificate.pem`);
  const keyPath = join(directory, `${name}-key.pem`);
  execFileSync(
    'openssl',
    [
      'req',
      '-x509',
      '-newkey',
      'ec',
      '-pkeyopt',
      'ec_paramgen_curve:prime256v1',
      '-keyout',
      keyPath,
      '-out',
      certificatePath,
      '-sha256',
      '-days',
      '1',
      '-nodes',
      '-subj',
      '/CN=127.0.0.1',
      '-addext',
      'subjectAltName=IP:127.0.0.1',
    ],
    { stdio: 'ignore' },
  );
  chmodSync(keyPath, 0o600);
  return { certificatePath, keyPath };
}

export function completeRun(store: RunStore, runId: string): void {
  const transitions: RunState[] = [
    'starting_runtime',
    'starting_agent',
    'running',
    'validating',
    'collecting_artifacts',
    'completed',
  ];
  for (const state of transitions)
    store.transition(runId, state, `Transitioned to ${state}`, { nextState: state });
}

export async function waitUntil(
  predicate: () => boolean,
  timeoutMilliseconds = 2000,
): Promise<void> {
  const deadline = Date.now() + timeoutMilliseconds;
  while (!predicate()) {
    if (Date.now() >= deadline) throw new Error('Timed out waiting for test condition.');
    await new Promise<void>((resolve) => setTimeout(resolve, 5));
  }
}
