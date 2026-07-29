import type { DatabaseSync } from 'node:sqlite';

const migrations = [
  `
  CREATE TABLE runner_metadata (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
  ) STRICT;

  CREATE TABLE runs (
    run_id TEXT PRIMARY KEY,
    specification_hash TEXT NOT NULL,
    specification_json TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN (
      'accepted', 'queued', 'preparing_workspace', 'starting_runtime', 'starting_agent',
      'running', 'validating', 'collecting_artifacts', 'completed', 'failed', 'cancelled'
    )),
    last_event_sequence INTEGER NOT NULL DEFAULT 0 CHECK (last_event_sequence >= 0),
    accepted_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    terminal_at TEXT
  ) STRICT;

  CREATE TABLE run_queue (
    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL UNIQUE REFERENCES runs(run_id) ON DELETE CASCADE,
    enqueued_at TEXT NOT NULL
  ) STRICT;

  CREATE TABLE run_events (
    run_id TEXT NOT NULL REFERENCES runs(run_id) ON DELETE CASCADE,
    sequence INTEGER NOT NULL CHECK (sequence > 0),
    timestamp TEXT NOT NULL,
    type TEXT NOT NULL,
    state TEXT,
    summary TEXT NOT NULL,
    data_json TEXT,
    PRIMARY KEY (run_id, sequence)
  ) STRICT;

  CREATE TABLE run_artifacts (
    run_id TEXT NOT NULL REFERENCES runs(run_id) ON DELETE CASCADE,
    artifact_id TEXT NOT NULL,
    metadata_json TEXT NOT NULL,
    relative_location TEXT NOT NULL,
    PRIMARY KEY (run_id, artifact_id)
  ) STRICT;

  CREATE INDEX runs_state_index ON runs(state);
  CREATE INDEX runs_terminal_at_index ON runs(terminal_at);
  CREATE INDEX run_queue_run_id_index ON run_queue(run_id);
  `,
  `
  ALTER TABLE runs ADD COLUMN cancellation_requested_at TEXT;
  CREATE INDEX runs_active_page_index ON runs(accepted_at, run_id)
    WHERE terminal_at IS NULL;
  `,
  `
  ALTER TABLE runs ADD COLUMN agent_thread_id TEXT;
  `,
  `
  ALTER TABLE run_events ADD COLUMN protocol_version TEXT NOT NULL DEFAULT '1.0';
  `,
];

export function applyMigrations(database: DatabaseSync): void {
  const row = database.prepare('PRAGMA user_version').get() as { user_version: number };
  if (row.user_version > migrations.length)
    throw new Error(`Runner database schema ${row.user_version} is newer than this host supports.`);

  for (let version = row.user_version; version < migrations.length; version += 1) {
    database.exec('BEGIN IMMEDIATE');
    try {
      database.exec(migrations[version] ?? '');
      database.exec(`PRAGMA user_version = ${version + 1}`);
      database.exec('COMMIT');
    } catch (error) {
      database.exec('ROLLBACK');
      throw error;
    }
  }
}
