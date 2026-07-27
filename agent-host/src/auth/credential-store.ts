import { existsSync, chmodSync, lstatSync } from 'node:fs';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fixedTimeTextEquals, publicKeyFingerprint, validateClientIdentity } from './crypto.js';

export interface PairedClient {
  clientId: string;
  displayName: string;
  publicKey: string;
  fingerprint: string;
  pairedAt: string;
  rotatedAt: string | null;
}

export interface PairingChallenge {
  codeHash: string;
  expiresAt: string;
  failedAttempts: number;
  maximumAttempts: number;
}

export type PairResult =
  | { disposition: 'paired'; client: PairedClient }
  | { disposition: 'invalid' }
  | { disposition: 'expired' }
  | { disposition: 'locked' }
  | { disposition: 'already_paired' };

interface ClientRow {
  client_id: string;
  display_name: string;
  public_key: string;
  fingerprint: string;
  paired_at: string;
  rotated_at: string | null;
}

export class CredentialStore {
  private readonly database: DatabaseSync;

  constructor(
    dataRoot: string,
    private readonly now: () => Date = () => new Date(),
  ) {
    const databasePath = join(dataRoot, 'credentials.sqlite');
    const existed = existsSync(databasePath);
    if (existed) assertSecureFile(databasePath);
    this.database = new DatabaseSync(databasePath);
    if (!existed) chmodSync(databasePath, 0o600);
    this.database.exec('PRAGMA journal_mode = WAL');
    this.database.exec('PRAGMA synchronous = FULL');
    this.database.exec('PRAGMA busy_timeout = 5000');
    applyCredentialMigrations(this.database);
  }

  close(): void {
    this.database.close();
  }

  getClient(): PairedClient | undefined {
    const row = this.database
      .prepare(
        `SELECT client_id, display_name, public_key, fingerprint, paired_at, rotated_at
         FROM paired_client WHERE slot = 1`,
      )
      .get() as ClientRow | undefined;
    return row === undefined ? undefined : toClient(row);
  }

  createChallenge(codeHash: string, expiresAt: Date, maximumAttempts = 5): void {
    if (this.getClient() !== undefined) throw new Error('A PM client is already paired.');
    if (!/^[0-9a-f]{64}$/.test(codeHash)) throw new Error('Pairing code hash is invalid.');
    if (!Number.isSafeInteger(maximumAttempts) || maximumAttempts <= 0)
      throw new Error('Maximum pairing attempts must be positive.');
    this.database
      .prepare(
        `INSERT INTO pairing_challenge (slot, code_hash, expires_at, failed_attempts, maximum_attempts)
         VALUES (1, ?, ?, 0, ?)
         ON CONFLICT(slot) DO UPDATE SET code_hash = excluded.code_hash,
           expires_at = excluded.expires_at, failed_attempts = 0,
           maximum_attempts = excluded.maximum_attempts`,
      )
      .run(codeHash, expiresAt.toISOString(), maximumAttempts);
  }

  pair(
    codeHash: string,
    identity: Omit<PairedClient, 'fingerprint' | 'pairedAt' | 'rotatedAt'>,
  ): PairResult {
    validateClientIdentity(identity.clientId, identity.displayName, identity.publicKey);
    return this.transaction(() => {
      if (this.getClient() !== undefined) return { disposition: 'already_paired' };
      const challenge = this.database
        .prepare(
          `SELECT code_hash, expires_at, failed_attempts, maximum_attempts
           FROM pairing_challenge WHERE slot = 1`,
        )
        .get() as
        | {
            code_hash: string;
            expires_at: string;
            failed_attempts: number;
            maximum_attempts: number;
          }
        | undefined;
      if (challenge === undefined) return { disposition: 'invalid' };
      if (challenge.failed_attempts >= challenge.maximum_attempts) return { disposition: 'locked' };
      if (Date.parse(challenge.expires_at) <= this.now().getTime()) {
        this.database.prepare('DELETE FROM pairing_challenge WHERE slot = 1').run();
        return { disposition: 'expired' };
      }
      if (!fixedTimeTextEquals(challenge.code_hash, codeHash)) {
        this.database
          .prepare(
            'UPDATE pairing_challenge SET failed_attempts = failed_attempts + 1 WHERE slot = 1',
          )
          .run();
        return {
          disposition:
            challenge.failed_attempts + 1 >= challenge.maximum_attempts ? 'locked' : 'invalid',
        };
      }

      const timestamp = this.now().toISOString();
      const client: PairedClient = {
        ...identity,
        fingerprint: publicKeyFingerprint(identity.publicKey),
        pairedAt: timestamp,
        rotatedAt: null,
      };
      this.database
        .prepare(
          `INSERT INTO paired_client
           (slot, client_id, display_name, public_key, fingerprint, paired_at, rotated_at)
           VALUES (1, ?, ?, ?, ?, ?, NULL)`,
        )
        .run(
          client.clientId,
          client.displayName,
          client.publicKey,
          client.fingerprint,
          client.pairedAt,
        );
      this.database.prepare('DELETE FROM pairing_challenge WHERE slot = 1').run();
      return { disposition: 'paired', client };
    });
  }

  useNonce(clientId: string, nonce: string, expiresAt: Date): boolean {
    return this.transaction(() => {
      this.database
        .prepare('DELETE FROM request_nonces WHERE expires_at <= ?')
        .run(this.now().toISOString());
      try {
        this.database
          .prepare('INSERT INTO request_nonces (client_id, nonce, expires_at) VALUES (?, ?, ?)')
          .run(clientId, nonce, expiresAt.toISOString());
        return true;
      } catch (error) {
        if (String(error).includes('UNIQUE constraint failed')) return false;
        throw error;
      }
    });
  }

  rotateClient(
    expectedClientId: string,
    identity: Omit<PairedClient, 'fingerprint' | 'pairedAt' | 'rotatedAt'>,
  ): PairedClient | undefined {
    validateClientIdentity(identity.clientId, identity.displayName, identity.publicKey);
    return this.transaction(() => {
      const current = this.getClient();
      if (current === undefined || current.clientId !== expectedClientId) return undefined;
      const rotated: PairedClient = {
        ...identity,
        fingerprint: publicKeyFingerprint(identity.publicKey),
        pairedAt: current.pairedAt,
        rotatedAt: this.now().toISOString(),
      };
      this.database
        .prepare(
          `UPDATE paired_client SET client_id = ?, display_name = ?, public_key = ?,
             fingerprint = ?, rotated_at = ? WHERE slot = 1`,
        )
        .run(
          rotated.clientId,
          rotated.displayName,
          rotated.publicKey,
          rotated.fingerprint,
          rotated.rotatedAt,
        );
      this.database.prepare('DELETE FROM request_nonces').run();
      return rotated;
    });
  }

  revokeClient(expectedClientId?: string): boolean {
    return this.transaction(() => {
      const current = this.getClient();
      if (
        current === undefined ||
        (expectedClientId !== undefined && current.clientId !== expectedClientId)
      )
        return false;
      this.database.prepare('DELETE FROM paired_client WHERE slot = 1').run();
      this.database.prepare('DELETE FROM request_nonces').run();
      this.database.prepare('DELETE FROM pairing_challenge').run();
      return true;
    });
  }

  private transaction<T>(operation: () => T): T {
    this.database.exec('BEGIN IMMEDIATE');
    try {
      const result = operation();
      this.database.exec('COMMIT');
      return result;
    } catch (error) {
      this.database.exec('ROLLBACK');
      throw error;
    }
  }
}

function applyCredentialMigrations(database: DatabaseSync): void {
  const version = (database.prepare('PRAGMA user_version').get() as { user_version: number })
    .user_version;
  if (version > 1) throw new Error(`Credential database schema ${version} is unsupported.`);
  if (version === 1) return;
  database.exec('BEGIN IMMEDIATE');
  try {
    database.exec(`
      CREATE TABLE paired_client (
        slot INTEGER PRIMARY KEY CHECK (slot = 1),
        client_id TEXT NOT NULL UNIQUE,
        display_name TEXT NOT NULL,
        public_key TEXT NOT NULL,
        fingerprint TEXT NOT NULL,
        paired_at TEXT NOT NULL,
        rotated_at TEXT
      ) STRICT;
      CREATE TABLE pairing_challenge (
        slot INTEGER PRIMARY KEY CHECK (slot = 1),
        code_hash TEXT NOT NULL,
        expires_at TEXT NOT NULL,
        failed_attempts INTEGER NOT NULL CHECK (failed_attempts >= 0),
        maximum_attempts INTEGER NOT NULL CHECK (maximum_attempts > 0)
      ) STRICT;
      CREATE TABLE request_nonces (
        client_id TEXT NOT NULL,
        nonce TEXT NOT NULL,
        expires_at TEXT NOT NULL,
        PRIMARY KEY (client_id, nonce)
      ) STRICT;
      CREATE INDEX request_nonces_expiry_index ON request_nonces(expires_at);
    `);
    database.exec('PRAGMA user_version = 1');
    database.exec('COMMIT');
  } catch (error) {
    database.exec('ROLLBACK');
    throw error;
  }
}

function toClient(row: ClientRow): PairedClient {
  return {
    clientId: row.client_id,
    displayName: row.display_name,
    publicKey: row.public_key,
    fingerprint: row.fingerprint,
    pairedAt: row.paired_at,
    rotatedAt: row.rotated_at,
  };
}

function assertSecureFile(path: string): void {
  const stats = lstatSync(path);
  if (stats.isSymbolicLink() || !stats.isFile())
    throw new Error('Credential database must be a regular file.');
  if ((stats.mode & 0o077) !== 0)
    throw new Error('Credential database must be accessible only by its owner.');
}
