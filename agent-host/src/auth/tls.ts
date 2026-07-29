import { readFileSync, lstatSync } from 'node:fs';
import { createSecureContext, type SecureContextOptions } from 'node:tls';
import { X509Certificate } from 'node:crypto';

export interface TlsMaterial {
  options: SecureContextOptions;
  fingerprint: string;
}

export function loadTlsMaterial(certificatePath: string, keyPath: string): TlsMaterial {
  const keyStats = lstatSync(keyPath);
  if (keyStats.isSymbolicLink() || !keyStats.isFile())
    throw new Error('TLS private key must be a regular file.');
  if ((keyStats.mode & 0o077) !== 0)
    throw new Error('TLS private key must be accessible only by its owner.');
  const certificate = readFileSync(certificatePath);
  const key = readFileSync(keyPath);
  const options: SecureContextOptions = {
    cert: certificate,
    key,
    minVersion: 'TLSv1.2',
  };
  createSecureContext(options);
  const parsed = new X509Certificate(certificate);
  return {
    options,
    fingerprint: `sha256:${parsed.fingerprint256.replaceAll(':', '').toLowerCase()}`,
  };
}

export function certificateFingerprint(certificatePath: string): string {
  const parsed = new X509Certificate(readFileSync(certificatePath));
  return `sha256:${parsed.fingerprint256.replaceAll(':', '').toLowerCase()}`;
}
