import { createServer, type Server } from 'node:https';
import type { IncomingMessage, ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import type { CapabilityService } from './capabilities.js';
import type { JsonLogger } from './logging.js';
import type { CredentialStore } from './auth/credential-store.js';
import { hashPairingCode, validateClientIdentity, verifyRotationProof } from './auth/crypto.js';
import { RequestAuthenticator } from './auth/authentication.js';
import type { TlsMaterial } from './auth/tls.js';
import type { ActiveRunCursor, RunStore, StoredRun } from './persistence/run-store.js';
import { RunCoordinator } from './run-coordinator.js';
import { EventStreamManager, StreamCapacityError } from './event-stream.js';
import { ProtocolValidationError, parseRunRequest } from './protocol/validation.js';

const maximumBodyBytes = 1_048_576;
const defaultPageLimit = 100;
const maximumPageLimit = 500;

export interface AgentHostServerOptions {
  listenAddress: string;
  port: number;
  tls: TlsMaterial;
  runnerId: string;
  credentials: CredentialStore;
  capabilities: CapabilityService;
  runStore: RunStore;
  runCoordinator: RunCoordinator;
  logger: JsonLogger;
  now?: () => Date;
  eventStreamOptions?: {
    maximumStreams?: number;
    heartbeatMilliseconds?: number;
    backpressureTimeoutMilliseconds?: number;
  };
}

export class AgentHostServer {
  private readonly server: Server;
  private readonly authenticator: RequestAuthenticator;
  private readonly eventStreams: EventStreamManager;

  constructor(private readonly options: AgentHostServerOptions) {
    this.authenticator = new RequestAuthenticator(options.credentials, options.now);
    this.eventStreams = new EventStreamManager(
      options.runStore,
      options.eventStreamOptions?.maximumStreams,
      options.eventStreamOptions?.heartbeatMilliseconds,
      options.eventStreamOptions?.backpressureTimeoutMilliseconds,
    );
    this.server = createServer(options.tls.options, (request, response) => {
      void this.handle(request, response);
    });
    this.server.headersTimeout = 10_000;
    this.server.requestTimeout = 30_000;
    this.server.keepAliveTimeout = 5_000;
    this.server.maxRequestsPerSocket = 100;
  }

  async start(): Promise<number> {
    await new Promise<void>((resolve, reject) => {
      const onError = (error: Error): void => reject(error);
      this.server.once('error', onError);
      this.server.listen(this.options.port, this.options.listenAddress, () => {
        this.server.off('error', onError);
        resolve();
      });
    });
    return (this.server.address() as AddressInfo).port;
  }

  async stop(): Promise<void> {
    this.eventStreams.close();
    if (!this.server.listening) return;
    await new Promise<void>((resolve, reject) =>
      this.server.close((error) => (error === undefined ? resolve() : reject(error))),
    );
  }

  private async handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    applySecurityHeaders(response);
    try {
      const method = request.method ?? '';
      const pathAndQuery = request.url ?? '/';
      const pathname = new URL(pathAndQuery, 'https://runner.invalid').pathname;
      const body = await readBody(request);
      if (pathname === '/v1/pairing/complete' && method === 'POST') {
        this.completePairing(body, response);
        return;
      }

      const authentication = this.authenticator.authenticate(
        method,
        pathAndQuery,
        request.headers,
        body,
      );
      if (!authentication.authenticated) {
        if (authentication.status === 426)
          response.setHeader('PM-Runner-Supported-Protocols', '1.0');
        writeError(
          response,
          authentication.status,
          authentication.errorCode,
          authentication.message,
        );
        return;
      }

      if (pathname === '/v1/health' && method === 'GET') {
        writeJson(response, 200, {
          runnerId: this.options.runnerId,
          status: 'online',
          protocolVersion: authentication.protocolVersion,
          timestamp: (this.options.now ?? (() => new Date()))().toISOString(),
        });
        return;
      }
      if (pathname === '/v1/capabilities' && method === 'GET') {
        writeJson(response, 200, this.options.capabilities.get());
        return;
      }
      if (await this.handleRunRoute(method, pathname, pathAndQuery, request, response, body))
        return;
      if (pathname === '/v1/client/rotate' && method === 'POST') {
        this.rotateClient(authentication.client.clientId, authentication.nonce, body, response);
        return;
      }
      if (pathname === '/v1/client' && method === 'DELETE') {
        const revoked = this.options.credentials.revokeClient(authentication.client.clientId);
        if (!revoked) {
          writeError(response, 409, 'client_changed', 'The paired client changed.');
          return;
        }
        response.statusCode = 204;
        response.end();
        this.options.logger.warn('client.revoked');
        return;
      }
      writeError(response, 404, 'not_found', 'The requested runner resource was not found.');
    } catch (error) {
      if (error instanceof HttpInputError) {
        writeError(response, error.status, error.errorCode, error.message);
      } else if (error instanceof ProtocolValidationError) {
        writeError(response, 400, error.code, error.message);
      } else if (error instanceof StreamCapacityError) {
        writeError(
          response,
          503,
          'stream_capacity_reached',
          'Runner event stream capacity is full.',
        );
      } else {
        this.options.logger.error('request.failed', { errorCode: 'request_failed' });
        writeError(response, 500, 'runner_error', 'The runner could not process the request.');
      }
    }
  }

  private async handleRunRoute(
    method: string,
    pathname: string,
    pathAndQuery: string,
    request: IncomingMessage,
    response: ServerResponse,
    body: Buffer,
  ): Promise<boolean> {
    if (!pathname.startsWith('/v1/runs')) return false;
    const url = new URL(pathAndQuery, 'https://runner.invalid');

    if (pathname === '/v1/runs' && method === 'POST') {
      const result = this.options.runCoordinator.start(parseRunRequest(parseJson(body)));
      switch (result.disposition) {
        case 'new':
          writeJson(response, 202, { disposition: 'new', run: runResponse(result.run) });
          return true;
        case 'existing':
          writeJson(response, 200, { disposition: 'existing', run: runResponse(result.run) });
          return true;
        case 'conflict':
          writeError(
            response,
            409,
            result.code,
            'Run ID is already used by another specification.',
          );
          return true;
        case 'queue_full':
          writeError(response, 429, result.code, 'The runner queue is full.');
          return true;
        case 'invalid_capability':
          writeError(response, 409, result.validation.errorCode, result.validation.message);
          return true;
      }
    }

    if (pathname === '/v1/runs' && method === 'GET') {
      if (url.searchParams.get('scope') !== 'active')
        throw new HttpInputError(400, 'invalid_scope', 'Only the active run scope is supported.');
      const limit = pageLimit(url.searchParams.get('limit'));
      const cursor = decodeActiveCursor(url.searchParams.get('cursor'));
      const page = this.options.runStore.listActiveRuns(limit, cursor);
      writeJson(response, 200, {
        runs: page.runs.map(RunCoordinator.summary),
        nextCursor: page.nextCursor === null ? null : encodeActiveCursor(page.nextCursor),
        hasMore: page.hasMore,
      });
      return true;
    }

    const route = runRoute(pathname);
    if (route === null) return false;
    const run = this.options.runStore.getRun(route.runId);
    if (run === undefined) {
      writeError(response, 404, 'run_not_found', 'The run was not found.');
      return true;
    }

    if (route.suffix === '' && method === 'GET') {
      writeJson(response, 200, { run: runResponse(run) });
      return true;
    }
    if (route.suffix === '/cancel' && method === 'POST') {
      const result = this.options.runCoordinator.cancel(route.runId);
      if (result.disposition === 'not_found') {
        writeError(response, 404, 'run_not_found', 'The run was not found.');
        return true;
      }
      writeJson(response, result.disposition === 'requested' ? 202 : 200, {
        disposition: result.disposition,
        run: runResponse(result.run),
      });
      return true;
    }
    if (route.suffix === '/events' && method === 'GET') {
      const afterSequence = sequence(url.searchParams.get('afterSequence'));
      const limit = pageLimit(url.searchParams.get('limit'));
      const page = this.options.runStore.eventPage(route.runId, afterSequence, limit);
      writeJson(response, 200, {
        events: page.events,
        nextAfterSequence: page.nextAfterSequence,
        hasMore: page.hasMore,
        terminal: isTerminalState(run.state),
      });
      return true;
    }
    if (route.suffix === '/events/stream' && method === 'GET') {
      const afterSequence = sequence(url.searchParams.get('afterSequence'));
      await this.eventStreams.stream(route.runId, afterSequence, request, response);
      return true;
    }
    if (route.suffix === '/artifacts' && method === 'GET') {
      writeJson(response, 200, { artifacts: this.options.runStore.listArtifacts(route.runId) });
      return true;
    }
    if (route.suffix.startsWith('/artifacts/') && method === 'GET') {
      const artifactId = route.suffix.slice('/artifacts/'.length);
      if (!safeIdentifier(artifactId))
        throw new HttpInputError(400, 'invalid_artifact_id', 'Artifact ID is invalid.');
      const artifact = this.options.runStore.getArtifact(route.runId, artifactId);
      if (artifact === undefined) {
        writeError(response, 404, 'artifact_not_found', 'The artifact metadata was not found.');
        return true;
      }
      writeJson(response, 200, { artifact });
      return true;
    }
    return false;
  }

  private completePairing(body: Buffer, response: ServerResponse): void {
    const input = objectBody(body);
    const versions = stringArray(input['protocolVersions'], 'protocolVersions');
    if (!versions.includes('1.0')) {
      response.setHeader('PM-Runner-Supported-Protocols', '1.0');
      writeError(
        response,
        426,
        'incompatible_protocol',
        'No compatible runner protocol is available.',
      );
      return;
    }
    const code = stringField(input['code'], 'code', 64);
    const identity = objectField(input['client'], 'client');
    const clientId = stringField(identity['clientId'], 'client.clientId', 256);
    const displayName = stringField(identity['displayName'], 'client.displayName', 512);
    const publicKey = stringField(identity['publicKey'], 'client.publicKey', 4096);
    try {
      validateClientIdentity(clientId, displayName, publicKey);
    } catch {
      throw new HttpInputError(
        400,
        'invalid_client_identity',
        'The PM client identity is invalid.',
      );
    }
    const result = this.options.credentials.pair(hashPairingCode(code), {
      clientId,
      displayName,
      publicKey,
    });
    switch (result.disposition) {
      case 'paired':
        this.options.logger.info('client.paired');
        writeJson(response, 201, {
          runnerId: this.options.runnerId,
          protocolVersion: '1.0',
          tlsFingerprint: this.options.tls.fingerprint,
          client: {
            clientId: result.client.clientId,
            displayName: result.client.displayName,
            fingerprint: result.client.fingerprint,
          },
          capabilities: this.options.capabilities.get(),
        });
        return;
      case 'already_paired':
        writeError(response, 409, 'client_already_paired', 'A PM client is already paired.');
        return;
      case 'expired':
        writeError(response, 410, 'pairing_expired', 'The pairing code expired.');
        return;
      case 'locked':
        writeError(response, 429, 'pairing_locked', 'The pairing window is locked.');
        return;
      case 'invalid':
        writeError(response, 401, 'pairing_failed', 'Pairing failed.');
    }
  }

  private rotateClient(
    oldClientId: string,
    requestNonce: string,
    body: Buffer,
    response: ServerResponse,
  ): void {
    const input = objectBody(body);
    const clientId = stringField(input['clientId'], 'clientId', 256);
    const displayName = stringField(input['displayName'], 'displayName', 512);
    const publicKey = stringField(input['publicKey'], 'publicKey', 4096);
    const proof = stringField(input['newKeySignature'], 'newKeySignature', 4096);
    try {
      validateClientIdentity(clientId, displayName, publicKey);
    } catch {
      throw new HttpInputError(400, 'invalid_client_identity', 'The new PM identity is invalid.');
    }
    if (
      !verifyRotationProof(
        this.options.runnerId,
        oldClientId,
        clientId,
        publicKey,
        requestNonce,
        proof,
      )
    )
      throw new HttpInputError(400, 'invalid_rotation_proof', 'The new identity proof is invalid.');
    const rotated = this.options.credentials.rotateClient(oldClientId, {
      clientId,
      displayName,
      publicKey,
    });
    if (rotated === undefined) {
      writeError(response, 409, 'client_changed', 'The paired client changed.');
      return;
    }
    this.options.logger.info('client.rotated');
    writeJson(response, 200, {
      clientId: rotated.clientId,
      displayName: rotated.displayName,
      fingerprint: rotated.fingerprint,
      rotatedAt: rotated.rotatedAt,
    });
  }
}

class HttpInputError extends Error {
  constructor(
    readonly status: number,
    readonly errorCode: string,
    message: string,
  ) {
    super(message);
  }
}

async function readBody(request: IncomingMessage): Promise<Buffer> {
  const chunks: Buffer[] = [];
  let length = 0;
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk as Uint8Array);
    length += buffer.length;
    if (length > maximumBodyBytes)
      throw new HttpInputError(413, 'request_too_large', 'Request body exceeds 1048576 bytes.');
    chunks.push(buffer);
  }
  return Buffer.concat(chunks);
}

function objectBody(body: Buffer): Record<string, unknown> {
  return objectField(parseJson(body), 'body');
}

function parseJson(body: Buffer): unknown {
  try {
    return JSON.parse(body.toString('utf8')) as unknown;
  } catch {
    throw new HttpInputError(400, 'invalid_json', 'Request body must be valid JSON.');
  }
}

function objectField(value: unknown, name: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value))
    throw new HttpInputError(400, 'invalid_request', `${name} must be an object.`);
  return value as Record<string, unknown>;
}

function stringField(value: unknown, name: string, maximum: number): string {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > maximum ||
    value !== value.trim() ||
    /[\u0000-\u001f\u007f]/.test(value)
  )
    throw new HttpInputError(400, 'invalid_request', `${name} is invalid.`);
  return value;
}

function stringArray(value: unknown, name: string): string[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > 16)
    throw new HttpInputError(400, 'invalid_request', `${name} is invalid.`);
  return value.map((entry) => stringField(entry, name, 16));
}

function runRoute(pathname: string): { runId: string; suffix: string } | null {
  const match = /^\/v1\/runs\/([A-Za-z0-9][A-Za-z0-9._-]{0,127})(\/.*)?$/.exec(pathname);
  return match === null ? null : { runId: match[1]!, suffix: match[2] ?? '' };
}

function safeIdentifier(value: string): boolean {
  return /^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$/.test(value);
}

function pageLimit(value: string | null): number {
  if (value === null) return defaultPageLimit;
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed <= 0 || parsed > maximumPageLimit)
    throw new HttpInputError(400, 'invalid_page_limit', 'Page limit must be between 1 and 500.');
  return parsed;
}

function sequence(value: string | null): number {
  if (value === null) return 0;
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 0)
    throw new HttpInputError(
      400,
      'invalid_event_sequence',
      'Event sequence must be a non-negative integer.',
    );
  return parsed;
}

function encodeActiveCursor(cursor: ActiveRunCursor): string {
  return Buffer.from(JSON.stringify(cursor), 'utf8').toString('base64url');
}

function decodeActiveCursor(value: string | null): ActiveRunCursor | null {
  if (value === null) return null;
  try {
    const parsed = JSON.parse(Buffer.from(value, 'base64url').toString('utf8')) as unknown;
    const cursor = objectField(parsed, 'cursor');
    const acceptedAt = stringField(cursor['acceptedAt'], 'cursor.acceptedAt', 32);
    const runId = stringField(cursor['runId'], 'cursor.runId', 128);
    if (Number.isNaN(Date.parse(acceptedAt)) || !safeIdentifier(runId)) throw new Error();
    return { acceptedAt, runId };
  } catch {
    throw new HttpInputError(400, 'invalid_cursor', 'Active run cursor is invalid.');
  }
}

function runResponse(run: StoredRun): object {
  return {
    runId: run.runId,
    specificationHash: run.specificationHash,
    specification: run.specification,
    state: run.state,
    lastEventSequence: run.lastEventSequence,
    acceptedAt: run.acceptedAt,
    updatedAt: run.updatedAt,
    terminalAt: run.terminalAt,
    cancellationRequestedAt: run.cancellationRequestedAt,
  };
}

function isTerminalState(state: StoredRun['state']): boolean {
  return state === 'completed' || state === 'failed' || state === 'cancelled';
}

function applySecurityHeaders(response: ServerResponse): void {
  response.setHeader('Cache-Control', 'no-store');
  response.setHeader('Content-Security-Policy', "default-src 'none'; frame-ancestors 'none'");
  response.setHeader('Strict-Transport-Security', 'max-age=31536000');
  response.setHeader('X-Content-Type-Options', 'nosniff');
}

function writeJson(response: ServerResponse, status: number, value: unknown): void {
  const body = JSON.stringify(value);
  response.statusCode = status;
  response.setHeader('Content-Type', 'application/json; charset=utf-8');
  response.setHeader('Content-Length', Buffer.byteLength(body));
  response.end(body);
}

function writeError(
  response: ServerResponse,
  status: number,
  errorCode: string,
  message: string,
): void {
  writeJson(response, status, { errorCode, message });
}
