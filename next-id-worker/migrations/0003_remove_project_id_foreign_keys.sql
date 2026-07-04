ALTER TABLE project_members RENAME TO project_members_with_project_id_fk;

CREATE TABLE project_members (
    project_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    public_key TEXT NOT NULL,
    role TEXT NOT NULL CHECK(role IN ('admin', 'user')),
    PRIMARY KEY (project_id, user_id)
);

INSERT INTO project_members(project_id, user_id, display_name, public_key, role)
SELECT project_id, user_id, display_name, public_key, role
FROM project_members_with_project_id_fk;

DROP TABLE project_members_with_project_id_fk;

ALTER TABLE project_counters RENAME TO project_counters_with_project_id_fk;

CREATE TABLE project_counters (
    project_id TEXT NOT NULL,
    track TEXT NOT NULL,
    next_id INTEGER NOT NULL,
    PRIMARY KEY (project_id, track)
);

INSERT INTO project_counters(project_id, track, next_id)
SELECT project_id, track, next_id
FROM project_counters_with_project_id_fk;

DROP TABLE project_counters_with_project_id_fk;
