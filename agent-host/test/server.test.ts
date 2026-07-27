import assert from 'node:assert/strict';
import { createHash, sign } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { request } from 'node:https';
import type { ClientRequest } from 'node:http';
import type { TLSSocket } from 'node:tls';
import { join } from 'node:path';
import test from 'node:test';
import { CredentialStore } from '../src/auth/credential-store.js';
import { hashPairingCode } from '../src/auth/crypto.js';
import { certificateFingerprint, loadTlsMaterial } from '../src/auth/tls.js';
import { CapabilityService, type DockerProbe } from '../src/capabilities.js';
import { JsonLogger } from '../src/logging.js';
import { RunStore } from '../src/persistence/run-store.js';
import { computeSpecificationHash } from '../src/protocol/canonical-json.js';
import { parseCapabilityManifest } from '../src/protocol/validation.js';
import { AgentHostServer } from '../src/server.js';
import { QueueOnlyExecutionController, RunCoordinator } from '../src/run-coordinator.js';
import {
  createIdentity,
  createRequest,
  createTempDirectory,
  createTestCertificate,
  signRequest,
  type TestIdentity,
} from './helpers.js';

interface Response {
  status: number;
  body: string;
}

interface OpenStream {
  request: ClientRequest;
  waitFor(pattern: RegExp): Promise<string>;
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
  const runCoordinator = new RunCoordinator(
    runStore,
    capabilities,
    32,
    new QueueOnlyExecutionController(),
  );
  const logLines: string[] = [];
  const logger = new JsonLogger((line) => logLines.push(line));
  const server = new AgentHostServer({
    listenAddress: '127.0.0.1',
    port: 0,
    tls,
    runnerId: runStore.runnerId,
    credentials,
    capabilities,
    runStore,
    runCoordinator,
    logger,
    eventStreamOptions: {
      maximumStreams: 1,
      heartbeatMilliseconds: 20,
      backpressureTimeoutMilliseconds: 100,
    },
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

    const runPath = '/v1/runs';
    const runRequest = createRequest('run-http-contract', runStore.runnerId);
    const runBody = JSON.stringify(runRequest);
    const accepted = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      runPath,
      runBody,
      signedHeaders(identity, 'POST', runPath, runBody, 'nonce_run_123456789012'),
    );
    assert.equal(accepted.status, 202);
    assert.equal(JSON.parse(accepted.body).disposition, 'new');

    const duplicate = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      runPath,
      runBody,
      signedHeaders(identity, 'POST', runPath, runBody, 'nonce_run_223456789012'),
    );
    assert.equal(duplicate.status, 200);
    assert.equal(JSON.parse(duplicate.body).disposition, 'existing');

    const changedRequest = structuredClone(runRequest);
    changedRequest.specification.task.title = 'Changed immutable task title';
    changedRequest.specificationHash = computeSpecificationHash(changedRequest.specification);
    const changedBody = JSON.stringify(changedRequest);
    const conflict = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      runPath,
      changedBody,
      signedHeaders(identity, 'POST', runPath, changedBody, 'nonce_run_323456789012'),
    );
    assert.equal(conflict.status, 409);
    assert.equal(JSON.parse(conflict.body).errorCode, 'run_id_conflict');

    const inspectPath = `${runPath}/${runRequest.specification.runId}`;
    const inspected = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      inspectPath,
      '',
      signedHeaders(identity, 'GET', inspectPath, '', 'nonce_run_423456789012'),
    );
    assert.equal(inspected.status, 200);
    assert.equal(JSON.parse(inspected.body).run.state, 'queued');

    const activePath = '/v1/runs?scope=active&limit=1';
    const active = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      activePath,
      '',
      signedHeaders(identity, 'GET', activePath, '', 'nonce_run_523456789012'),
    );
    assert.equal(active.status, 200);
    assert.equal(JSON.parse(active.body).runs[0].runId, runRequest.specification.runId);

    runStore.recordArtifact(
      runRequest.specification.runId,
      {
        artifactId: 'result-patch',
        kind: 'git_patch',
        fileName: 'result.patch',
        mediaType: 'text/x-diff',
        byteLength: 12,
        sha256: 'a'.repeat(64),
        createdAt: '2026-07-27T12:00:00.000Z',
      },
      'runs/run-http-contract/artifacts/result.patch',
    );
    const artifactsPath = `${inspectPath}/artifacts`;
    const artifacts = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      artifactsPath,
      '',
      signedHeaders(identity, 'GET', artifactsPath, '', 'nonce_run_623456789012'),
    );
    assert.equal(artifacts.status, 200);
    assert.equal(JSON.parse(artifacts.body).artifacts[0].artifactId, 'result-patch');

    const eventPagePath = `${inspectPath}/events?afterSequence=0&limit=1`;
    const eventPage = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      eventPagePath,
      '',
      signedHeaders(identity, 'GET', eventPagePath, '', 'nonce_run_723456789012'),
    );
    assert.equal(eventPage.status, 200);
    assert.equal(JSON.parse(eventPage.body).events.length, 1);
    assert.equal(JSON.parse(eventPage.body).hasMore, true);

    const cancelPath = `${inspectPath}/cancel`;
    const cancelled = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      cancelPath,
      '',
      signedHeaders(identity, 'POST', cancelPath, '', 'nonce_run_823456789012'),
    );
    assert.equal(cancelled.status, 200);
    assert.equal(JSON.parse(cancelled.body).run.state, 'cancelled');

    const streamPath = `${inspectPath}/events/stream?afterSequence=2`;
    const stream = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      streamPath,
      '',
      signedHeaders(identity, 'GET', streamPath, '', 'nonce_run_923456789012'),
    );
    assert.equal(stream.status, 200);
    assert.match(stream.body, /id: 3\nevent: run-event/);
    assert.match(stream.body, /id: 4\nevent: run-event/);
    assert.doesNotMatch(stream.body, /id: 2\nevent: run-event/);
    assert.match(stream.body, /event: stream-end/);

    const liveRequest = createRequest('run-live-stream', runStore.runnerId);
    const liveBody = JSON.stringify(liveRequest);
    const liveAccepted = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      runPath,
      liveBody,
      signedHeaders(identity, 'POST', runPath, liveBody, 'nonce_live_12345678901'),
    );
    assert.equal(liveAccepted.status, 202);

    const livePath = '/v1/runs/run-live-stream/events/stream?afterSequence=2';
    const liveStream = openSse(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      livePath,
      signedHeaders(identity, 'GET', livePath, '', 'nonce_live_22345678901'),
    );
    assert.match(await liveStream.waitFor(/: heartbeat/), /: heartbeat/);

    const capacityPath = '/v1/runs/run-live-stream/events/stream?afterSequence=2';
    const capacity = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      capacityPath,
      '',
      signedHeaders(identity, 'GET', capacityPath, '', 'nonce_live_32345678901'),
    );
    assert.equal(capacity.status, 503);
    assert.equal(JSON.parse(capacity.body).errorCode, 'stream_capacity_reached');

    liveStream.request.destroy();
    await new Promise<void>((resolve) => setTimeout(resolve, 20));
    assert.equal(runStore.getRun('run-live-stream')?.state, 'queued');

    runStore.appendEvent('run-live-stream', {
      type: 'runner.message',
      state: 'queued',
      summary: 'Published after disconnect',
    });
    const liveCancelPath = '/v1/runs/run-live-stream/cancel';
    const liveCancelled = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'POST',
      liveCancelPath,
      '',
      signedHeaders(identity, 'POST', liveCancelPath, '', 'nonce_live_42345678901'),
    );
    assert.equal(liveCancelled.status, 200);

    const resumedPath = '/v1/runs/run-live-stream/events/stream?afterSequence=2';
    const resumed = await send(
      port,
      tlsFiles.certificatePath,
      tls.fingerprint,
      'GET',
      resumedPath,
      '',
      signedHeaders(identity, 'GET', resumedPath, '', 'nonce_live_52345678901'),
    );
    assert.equal(resumed.status, 200);
    assert.match(resumed.body, /Published after disconnect/);
    assert.match(resumed.body, /event: stream-end/);

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

function openSse(
  port: number,
  certificatePath: string,
  expectedFingerprint: string,
  path: string,
  headers: Record<string, string>,
): OpenStream {
  let body = '';
  const waiters: Array<{
    pattern: RegExp;
    resolve: (value: string) => void;
    reject: (error: Error) => void;
    timer: NodeJS.Timeout;
  }> = [];
  const call = request(
    {
      hostname: '127.0.0.1',
      port,
      path,
      method: 'GET',
      agent: false,
      ca: readFileSync(certificatePath),
      minVersion: 'TLSv1.3',
      headers,
    },
    (response) => {
      response.on('data', (chunk: Buffer) => {
        body += chunk.toString('utf8');
        for (const waiter of [...waiters]) {
          if (!waiter.pattern.test(body)) continue;
          waiters.splice(waiters.indexOf(waiter), 1);
          clearTimeout(waiter.timer);
          waiter.resolve(body);
        }
      });
    },
  );
  call.on('socket', (socket) => {
    const tlsSocket = socket as TLSSocket;
    tlsSocket.once('secureConnect', () => {
      const certificate = tlsSocket.getPeerCertificate();
      const actual = `sha256:${createHash('sha256').update(certificate.raw).digest('hex')}`;
      if (actual !== expectedFingerprint) tlsSocket.destroy(new Error('TLS fingerprint mismatch.'));
    });
  });
  call.on('error', () => undefined);
  call.end();
  return {
    request: call,
    waitFor(pattern): Promise<string> {
      if (pattern.test(body)) return Promise.resolve(body);
      return new Promise((resolve, reject) => {
        const waiter = {
          pattern,
          resolve,
          reject,
          timer: setTimeout(() => {
            waiters.splice(waiters.indexOf(waiter), 1);
            reject(new Error(`Timed out waiting for SSE pattern ${pattern}.`));
          }, 2000),
        };
        waiters.push(waiter);
      });
    },
  };
}
