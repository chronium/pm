import type { IncomingHttpHeaders } from 'node:http';
import type { CredentialStore, PairedClient } from './credential-store.js';
import { validateNonce, verifySignedRequest } from './crypto.js';

const allowedClockSkewSeconds = 300;
export const supportedProtocolVersions = ['1.2', '1.1', '1.0'] as const;
export type SupportedProtocolVersion = (typeof supportedProtocolVersions)[number];

export type AuthenticationResult =
  | {
      authenticated: true;
      client: PairedClient;
      nonce: string;
      protocolVersion: SupportedProtocolVersion;
    }
  | {
      authenticated: false;
      status: 401 | 426;
      errorCode: string;
      message: string;
      serverTime?: string;
    };

export class RequestAuthenticator {
  constructor(
    private readonly credentials: CredentialStore,
    private readonly now: () => Date = () => new Date(),
  ) {}

  authenticate(
    method: string,
    pathAndQuery: string,
    headers: IncomingHttpHeaders,
    body: Buffer,
  ): AuthenticationResult {
    const clientId = header(headers, 'pm-runner-client-id');
    const timestamp = header(headers, 'pm-runner-timestamp');
    const nonce = header(headers, 'pm-runner-nonce');
    const signature = header(headers, 'pm-runner-signature');
    const protocolVersion = header(headers, 'pm-runner-protocol-version');
    if (
      clientId === null ||
      timestamp === null ||
      nonce === null ||
      signature === null ||
      protocolVersion === null ||
      !validateNonce(nonce)
    )
      return unauthorized();

    const timestampSeconds = Number(timestamp);
    const nowSeconds = Math.floor(this.now().getTime() / 1000);
    if (!Number.isSafeInteger(timestampSeconds)) return unauthorized();
    if (Math.abs(timestampSeconds - nowSeconds) > allowedClockSkewSeconds)
      return unauthorized(String(nowSeconds));

    const client = this.credentials.getClient();
    if (client === undefined || client.clientId !== clientId) return unauthorized();
    if (
      !verifySignedRequest(
        { method, pathAndQuery, protocolVersion, timestamp, nonce, clientId, body },
        client.publicKey,
        signature,
      )
    )
      return unauthorized();
    if (!supportedProtocolVersions.includes(protocolVersion as SupportedProtocolVersion))
      return {
        authenticated: false,
        status: 426,
        errorCode: 'incompatible_protocol',
        message: 'The authenticated protocol version is not supported.',
      };
    const nonceExpiresAt = new Date((timestampSeconds + allowedClockSkewSeconds + 1) * 1000);
    if (!this.credentials.useNonce(clientId, nonce, nonceExpiresAt)) return unauthorized();
    return {
      authenticated: true,
      client,
      nonce,
      protocolVersion: protocolVersion as SupportedProtocolVersion,
    };
  }
}

function header(headers: IncomingHttpHeaders, name: string): string | null {
  const value = headers[name];
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function unauthorized(serverTime?: string): AuthenticationResult {
  return {
    authenticated: false,
    status: 401,
    errorCode: 'unauthorized',
    message: 'Authentication failed.',
    ...(serverTime === undefined ? {} : { serverTime }),
  };
}
