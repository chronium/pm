CREATE TABLE project_invitations (
    invitation_id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    role TEXT NOT NULL CHECK (role IN ('admin', 'user')),
    created_by_user_id TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    consumed_at INTEGER,
    consumed_by_user_id TEXT,
    revoked_at INTEGER,
    CHECK ((consumed_at IS NULL) = (consumed_by_user_id IS NULL))
);

CREATE INDEX idx_project_invitations_active
ON project_invitations(project_id, expires_at)
WHERE consumed_at IS NULL AND revoked_at IS NULL;
