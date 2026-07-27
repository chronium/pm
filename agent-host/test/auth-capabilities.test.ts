import assert from 'node:assert/strict';
import { readFileSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { CredentialStore } from '../src/auth/credential-store.js';
import { hashPairingCode } from '../src/auth/crypto.js';
import {
  CapabilityService,
  loadCapabilityManifest,
  type DockerProbe,
} from '../src/capabilities.js';
import { RunStore } from '../src/persistence/run-store.js';
import { createIdentity, createTempDirectory } from './helpers.js';

test('pairing challenges are one-use, bounded, and credentials persist privately', () => {
  const temporary = createTempDirectory();
  let now = new Date('2026-07-27T10:00:00.000Z');
  const runStore = new RunStore(temporary.path, () => now);
  let credentials = new CredentialStore(temporary.path, () => now);
  const identity = createIdentity();
  try {
    credentials.createChallenge(
      hashPairingCode('ABCD-EFGH-JKMP'),
      new Date(now.getTime() + 600_000),
    );
    assert.equal(
      credentials.pair(hashPairingCode('WRNG-WRNG-WRNG'), identity).disposition,
      'invalid',
    );
    const paired = credentials.pair(hashPairingCode('ABCD-EFGH-JKMP'), identity);
    assert.equal(paired.disposition, 'paired');
    assert.equal(
      credentials.pair(hashPairingCode('ABCD-EFGH-JKMP'), identity).disposition,
      'already_paired',
    );
    assert.equal(statSync(join(temporary.path, 'credentials.sqlite')).mode & 0o777, 0o600);
    assert.equal(
      credentials.useNonce(
        identity.clientId,
        'nonce_1234567890123456',
        new Date(now.getTime() + 600_000),
      ),
      true,
    );
    assert.equal(
      credentials.useNonce(
        identity.clientId,
        'nonce_1234567890123456',
        new Date(now.getTime() + 600_000),
      ),
      false,
    );

    credentials.close();
    credentials = new CredentialStore(temporary.path, () => now);
    assert.equal(credentials.getClient()?.clientId, identity.clientId);
    assert.equal(
      credentials.useNonce(
        identity.clientId,
        'nonce_1234567890123456',
        new Date(now.getTime() + 600_000),
      ),
      false,
    );

    const replacement = createIdentity('usr_replacement');
    assert.equal(
      credentials.rotateClient(identity.clientId, replacement)?.clientId,
      replacement.clientId,
    );
    assert.equal(credentials.revokeClient(identity.clientId), false);
    assert.equal(credentials.revokeClient(replacement.clientId), true);

    now = new Date('2026-07-27T11:00:00.000Z');
    credentials.createChallenge(hashPairingCode('ABCD-EFGH-JKMP'), new Date(now.getTime() - 1));
    assert.equal(
      credentials.pair(hashPairingCode('ABCD-EFGH-JKMP'), identity).disposition,
      'expired',
    );
  } finally {
    credentials.close();
    runStore.close();
    temporary.dispose();
  }
});

test('pairing locks after five invalid attempts', () => {
  const temporary = createTempDirectory();
  const runStore = new RunStore(temporary.path);
  const credentials = new CredentialStore(temporary.path);
  const identity = createIdentity();
  try {
    credentials.createChallenge(hashPairingCode('ABCD-EFGH-JKMP'), new Date(Date.now() + 600_000));
    for (let attempt = 1; attempt <= 4; attempt += 1)
      assert.equal(
        credentials.pair(hashPairingCode(`WRNG-WRNG-WRN${attempt}`), identity).disposition,
        'invalid',
      );
    assert.equal(
      credentials.pair(hashPairingCode('WRNG-WRNG-WRN5'), identity).disposition,
      'locked',
    );
    assert.equal(
      credentials.pair(hashPairingCode('ABCD-EFGH-JKMP'), identity).disposition,
      'locked',
    );
  } finally {
    credentials.close();
    runStore.close();
    temporary.dispose();
  }
});

test('capability manifest validates profile revisions and combines dynamic host state', () => {
  const temporary = createTempDirectory();
  const store = new RunStore(temporary.path);
  try {
    const fixture = JSON.parse(
      readFileSync(
        join(process.cwd(), '..', 'contracts/agent-runs/v1/runner-capabilities.json'),
        'utf8',
      ),
    ) as Record<string, unknown>;
    const manifestPath = join(temporary.path, 'capabilities.json');
    writeFileSync(
      manifestPath,
      JSON.stringify({
        displayName: fixture['displayName'],
        agentProviders: fixture['agentProviders'],
        runtimeProfiles: fixture['runtimeProfiles'],
      }),
    );
    const manifest = loadCapabilityManifest(manifestPath);
    const probe: DockerProbe = { available: () => true };
    const capabilities = new CapabilityService(
      store,
      manifest,
      3,
      probe,
      () => 64 * 1024 * 1024,
      () => 'linux',
      () => 'x64',
    ).get();
    assert.equal(capabilities.runnerId, store.runnerId);
    assert.equal(capabilities.capacity.maximumRuns, 3);
    assert.equal(capabilities.capacity.activeRuns, 0);
    assert.equal(capabilities.dockerAvailable, true);
    assert.deepEqual(capabilities.protocolVersions, ['1.0']);

    const invalid = structuredClone(
      JSON.parse(readFileSync(manifestPath, 'utf8')) as Record<string, unknown>,
    );
    const profiles = invalid['runtimeProfiles'] as { revision: string }[];
    profiles[0]!.revision = '0'.repeat(64);
    writeFileSync(manifestPath, JSON.stringify(invalid));
    assert.throws(() => loadCapabilityManifest(manifestPath), /revision does not match/);
  } finally {
    store.close();
    temporary.dispose();
  }
});
