import assert from 'node:assert/strict';
import { createHash, sign } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { request } from 'node:https';
import type { TLSSocket } from 'node:tls';
import { join } from 'node:path';
import test from 'node:test';
import { CredentialStore } from '../src/auth/credential-store.js';
import { hashPairingCode } from '../src/auth/crypto.js';
import { certificateFingerprint, loadTlsMaterial } from '../src/auth/tls.js';
import { CapabilityService, type DockerProbe } from '../src/capabilities.js';
import { JsonLogger } from '../src/logging.js';
import { RunStore } from '../src/persistence/run-store.js';
import { parseCapabilityManifest } from '../src/protocol/validation.js';
import { AgentHostServer } from '../src/server.js';
import {
  createIdentity,
  createTempDirectory,
  createTestCertificate,
  signRequest,
  type TestIdentity,
} from './helpers.js';

interface Response {
  status: number;
  body: string;
}

test('HTTPS server pairs, authenticates, rejects replay, rotates, and revokes', async () => {
  const temporary = createTempDirectory();
  const tlsFiles = createTestCertificate(temporary.path);
  const tls = loadTlsMaterial(tlsFiles.certificatePath, tlsFiles.keyPath);
  const runStore = new RunStore(temporary.path);
  const credentials = new CredentialStore(temporary.path);
  const manifestFixture = JSON.parse(
    readFileSync(
      join(process.cwd(), '..', 'contracts/agent-runs/v1/runner-capabilities.json'),
      'utf8',
    ),
  ) as Record<string, unknown>;
  const manifest = parseCapabilityManifest({
    displayName: manifestFixture['displayName'],
    agentProviders: manifestFixture['agentProviders'],
    runtimeProfiles: manifestFixture['runtimeProfiles'],
  });
  const dockerProbe: DockerProbe = { available: () => true };
  const capabilities = new CapabilityService(runStore, manifest, 2, dockerProbe);
  const logLines: string[] = [];
  const logger = new JsonLogger((line) => logLines.push(line));
  const server = new AgentHostServer({
    listenAddress: '127.0.0.1',
    port: 0,
    tls,
    runnerId: runStore.runnerId,
    credentials,
    capabilities,
    logger,
  });
  let port = 0;
  try {
    credentials.createChallenge(hashPairingCode('ABCD-EFGH-JKMP'), new Date(Date.now() + 600_000));
    port = await server.start();
    const identity = createIdentity();

    const unauthorized = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      '/v1/capabilities',
    );
    assert.equal(unauthorized.status, 401);

    await assert.rejects(
      send(port, tlsFiles.certificatePath, `sha256:${'0'.repeat(64)}`, 'GET', '/v1/health'),
      /fingerprint mismatch/,
    );

    const pairingBody = JSON.stringify({
      code: 'ABCD-EFGH-JKMP',
      protocolVersions: ['1.0'],
      client: {
        clientId: identity.clientId,
        displayName: identity.displayName,
        publicKey: identity.publicKey,
      },
    });
    const pairing = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      '/v1/pairing/complete',
      pairingBody,
    );
    assert.equal(pairing.status, 201);
    assert.equal(JSON.parse(pairing.body).tlsFingerprint, tls.fingerprint);

    const nonce = 'nonce_1234567890123456';
    const authenticatedHeaders = signedHeaders(identity, 'GET', '/v1/capabilities', '', nonce);
    const discovered = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      '/v1/capabilities',
      '',
      authenticatedHeaders,
    );
    assert.equal(discovered.status, 200);
    assert.equal(JSON.parse(discovered.body).runnerId, runStore.runnerId);

    const replay = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      '/v1/capabilities',
      '',
      authenticatedHeaders,
    );
    assert.equal(replay.status, 401);

    const queryTampering = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      '/v1/health?probe=1',
      '',
      signedHeaders(identity, 'GET', '/v1/health', '', 'nonce_1334567890123456'),
    );
    assert.equal(queryTampering.status, 401);

    const stale = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      '/v1/health',
      '',
      signedHeaders(
        identity,
        'GET',
        '/v1/health',
        '',
        'nonce_1434567890123456',
        '1.0',
        String(Math.floor(Date.now() / 1000) - 301),
      ),
    );
    assert.equal(stale.status, 401);

    const incompatible = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      '/v1/health',
      '',
      signedHeaders(identity, 'GET', '/v1/health', '', 'nonce_2234567890123456', '2.0'),
    );
    assert.equal(incompatible.status, 426);

    const replacement = createIdentity('usr_replacement');
    const rotationNonce = 'nonce_3234567890123456';
    const rotationBody = JSON.stringify({
      clientId: replacement.clientId,
      displayName: replacement.displayName,
      publicKey: replacement.publicKey,
      newKeySignature: signRotationProof(
        replacement,
        runStore.runnerId,
        identity.clientId,
        rotationNonce,
      ),
    });
    const rotation = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      '/v1/client/rotate',
      rotationBody,
      signedHeaders(identity, 'POST', '/v1/client/rotate', rotationBody, rotationNonce),
    );
    assert.equal(rotation.status, 200);

    const oldCredential = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      '/v1/health',
      '',
      signedHeaders(identity, 'GET', '/v1/health', '', 'nonce_4234567890123456'),
    );
    assert.equal(oldCredential.status, 401);

    const revokePath = '/v1/client';
    const revoked = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'DELETE',
      revokePath,
      '',
      signedHeaders(replacement, 'DELETE', revokePath, '', 'nonce_5234567890123456'),
    );
    assert.equal(revoked.status, 204);
    assert.equal(credentials.getClient(), undefined);
    assert.doesNotMatch(logLines.join('\n'), /ABCD|publicKey|certificatePath|\/v1\//);
  } finally {
    await server.stop();
    credentials.close();
    runStore.close();
    temporary.dispose();
  }
});

function signedHeaders(
  identity: TestIdentity,
  method: string,
  pathAndQuery: string,
  body: string,
  nonce: string,
  protocolVersion = '1.0',
  timestamp = String(Math.floor(Date.now() / 1000)),
): Record<string, string> {
  const values = {
    method,
    pathAndQuery,
    protocolVersion,
    timestamp,
    nonce,
    clientId: identity.clientId,
    body: Buffer.from(body),
  };
  return {
    'PM-Runner-Client-Id': identity.clientId,
    'PM-Runner-Timestamp': timestamp,
    'PM-Runner-Nonce': nonce,
    'PM-Runner-Signature': signRequest(identity, values),
    'PM-Runner-Protocol-Version': protocolVersion,
  };
}

function signRotationProof(
  identity: TestIdentity,
  runnerId: string,
  oldClientId: string,
  nonce: string,
): string {
  const canonical = [
    'pm-runner-rotation-v1',
    runnerId,
    oldClientId,
    identity.clientId,
    identity.publicKey,
    nonce,
  ].join('\n');
  return sign('sha256', Buffer.from(canonical), {
    key: identity.privateKey,
    dsaEncoding: 'ieee-p1363',
  }).toString('base64url');
}

function send(
  port: number,
  certificatePath: string,
  expectedFingerprint: string,
  method: string,
  path: string,
  body = '',
  headers: Record<string, string> = {},
): Promise<Response> {
  return new Promise((resolve, reject) => {
    const call = request(
      {
        hostname: '127.0.0.1',
        port,
        path,
        method,
        agent: false,
        ca: readFileSync(certificatePath),
        minVersion: 'TLSv1.3',
        headers: {
          ...headers,
          ...(body.length === 0
            ? {}
            : {
                'Content-Type': 'application/json',
                'Content-Length': String(Buffer.byteLength(body)),
              }),
        },
      },
      (response) => {
        const chunks: Buffer[] = [];
        response.on('data', (chunk: Buffer) => chunks.push(chunk));
        response.on('end', () =>
          resolve({
            status: response.statusCode ?? 0,
            body: Buffer.concat(chunks).toString('utf8'),
          }),
        );
      },
    );
    call.on('socket', (socket) => {
      const tlsSocket = socket as TLSSocket;
      tlsSocket.once('secureConnect', () => {
        const certificate = tlsSocket.getPeerCertificate();
        const actual = `sha256:${createHash('sha256').update(certificate.raw).digest('hex')}`;
        if (actual !== expectedFingerprint)
          tlsSocket.destroy(new Error('TLS fingerprint mismatch.'));
      });
    });
    call.on('error', reject);
    if (body.length > 0) call.write(body);
    call.end();
  });
}
