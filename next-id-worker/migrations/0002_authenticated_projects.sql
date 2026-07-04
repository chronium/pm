ALTER TABLE projects ADD COLUMN project_id TEXT;
ALTER TABLE projects ADD COLUMN recovery_key_hash TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS idx_projects_project_id
ON projects(project_id)
WHERE project_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS project_members (
    project_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    public_key TEXT NOT NULL,
    role TEXT NOT NULL CHECK(role IN ('admin', 'user')),
    PRIMARY KEY (project_id, user_id)
);

CREATE TABLE IF NOT EXISTS request_nonces (
    user_id TEXT NOT NULL,
    nonce TEXT NOT NULL,
    timestamp INTEGER NOT NULL,
    PRIMARY KEY (user_id, nonce)
);

ALTER TABLE project_counters RENAME TO legacy_project_counters;

CREATE TABLE IF NOT EXISTS project_counters (
    project_id TEXT NOT NULL,
    track TEXT NOT NULL,
    next_id INTEGER NOT NULL,
    PRIMARY KEY (project_id, track)
);
