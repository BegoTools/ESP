CREATE TABLE licenses (
    id TEXT PRIMARY KEY,
    code_hash TEXT NOT NULL UNIQUE,
    label TEXT NOT NULL,
    device_limit INTEGER NOT NULL CHECK (device_limit BETWEEN 1 AND 100),
    created_at INTEGER NOT NULL,
    expires_at INTEGER,
    duration_seconds INTEGER,
    revoked INTEGER NOT NULL DEFAULT 0,
    CHECK (expires_at IS NOT NULL OR duration_seconds > 0)
);

CREATE TABLE devices (
    license_id TEXT NOT NULL REFERENCES licenses(id) ON DELETE CASCADE,
    device TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    activated_at INTEGER NOT NULL,
    PRIMARY KEY (license_id, device)
);
