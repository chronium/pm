import {
  createHash,
  createPublicKey,
  randomBytes,
  timingSafeEqual,
  verify,
  type KeyObject,
} from 'node:crypto';

const crockford = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
const clientIdPattern = /^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$/;
const noncePattern = /^[A-Za-z0-9][A-Za-z0-9._-]{15,255}$/;

export interface SignedRequestValues {
  method: string;
  pathAndQuery: string;
  protocolVersion: string;
  timestamp: string;
  nonce: string;
  clientId: string;
  body: Buffer;
}

export function generatePairingCode(): string {
  let value = randomBytes(8).readBigUInt64BE() & ((1n << 60n) - 1n);
  let result = '';
  for (let index = 0; index < 12; index += 1) {
    result = crockford[Number(value & 31n)] + result;
    value >>= 5n;
  }
  return `${result.slice(0, 4)}-${result.slice(4, 8)}-${result.slice(8)}`;
}

export function normalizePairingCode(value: string): string {
  return value.toUpperCase().replaceAll('-', '').replaceAll(' ', '');
}

export function hashPairingCode(value: string): string {
  return sha256Hex(Buffer.from(normalizePairingCode(value), 'utf8'));
}

export function canonicalSignedRequest(values: SignedRequestValues): string {
  return [
    'pm-runner-auth-v1',
    values.method.toUpperCase(),
    values.pathAndQuery,
    values.protocolVersion,
    values.timestamp,
    values.nonce,
    values.clientId,
    sha256Hex(values.body),
  ].join('\n');
}

export function verifySignedRequest(
  values: SignedRequestValues,
  publicKey: string,
  signature: string,
): boolean {
  try {
    const key = importPublicKey(publicKey);
    const signatureBytes = base64UrlDecode(signature);
    return verify(
      'sha256',
      Buffer.from(canonicalSignedRequest(values), 'utf8'),
      { key, dsaEncoding: 'ieee-p1363' },
      signatureBytes,
    );
  } catch {
    return false;
  }
}

export function verifyRotationProof(
  runnerId: string,
  oldClientId: string,
  newClientId: string,
  newPublicKey: string,
  requestNonce: string,
  signature: string,
): boolean {
  try {
    const canonical = [
      'pm-runner-rotation-v1',
      runnerId,
      oldClientId,
      newClientId,
      newPublicKey,
      requestNonce,
    ].join('\n');
    return verify(
      'sha256',
      Buffer.from(canonical, 'utf8'),
      { key: importPublicKey(newPublicKey), dsaEncoding: 'ieee-p1363' },
      base64UrlDecode(signature),
    );
  } catch {
    return false;
  }
}

export function publicKeyFingerprint(publicKey: string): string {
  const key = importPublicKey(publicKey);
  const bytes = key.export({ type: 'spki', format: 'der' });
  return `sha256:${sha256Hex(bytes)}`;
}

export function validateClientIdentity(
  clientId: string,
  displayName: string,
  publicKey: string,
): void {
  if (!clientIdPattern.test(clientId)) throw new Error('Client ID is invalid.');
  if (
    displayName.length === 0 ||
    displayName.length > 512 ||
    displayName !== displayName.trim() ||
    /[\u0000-\u001f\u007f]/.test(displayName)
  )
    throw new Error('Client display name is invalid.');
  const key = importPublicKey(publicKey);
  if (key.asymmetricKeyType !== 'ec' || key.asymmetricKeyDetails?.namedCurve !== 'prime256v1')
    throw new Error('Client key must use P-256.');
}

export function validateNonce(value: string): boolean {
  return noncePattern.test(value);
}

export function fixedTimeTextEquals(left: string, right: string): boolean {
  const leftBytes = Buffer.from(left, 'utf8');
  const rightBytes = Buffer.from(right, 'utf8');
  return leftBytes.length === rightBytes.length && timingSafeEqual(leftBytes, rightBytes);
}

export function sha256Hex(value: Buffer): string {
  return createHash('sha256').update(value).digest('hex');
}

function importPublicKey(value: string): KeyObject {
  const bytes = base64UrlDecode(value);
  return createPublicKey({ key: bytes, format: 'der', type: 'spki' });
}

export function base64UrlDecode(value: string): Buffer {
  if (!/^[A-Za-z0-9_-]+$/.test(value)) throw new Error('Value is not base64url.');
  return Buffer.from(value, 'base64url');
}
