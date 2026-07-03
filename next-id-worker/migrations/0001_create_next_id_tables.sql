CREATE TABLE IF NOT EXISTS projects (
    key_hash TEXT PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS project_counters (
    key_hash TEXT NOT NULL,
    track TEXT NOT NULL,
    next_id INTEGER NOT NULL,
    PRIMARY KEY (key_hash, track),
    FOREIGN KEY (key_hash) REFERENCES projects(key_hash)
);
