CREATE TABLE plans (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('active', 'archived')),
    max_devices INTEGER NOT NULL CHECK (max_devices BETWEEN 1 AND 100),
    defaults_json TEXT NOT NULL DEFAULT '{}' CHECK (json_valid(defaults_json))
);

ALTER TABLE licenses ADD COLUMN plan_id TEXT REFERENCES plans(id);
ALTER TABLE devices ADD COLUMN last_seen INTEGER;
CREATE INDEX licenses_expiry ON licenses(expires_at);
CREATE INDEX devices_last_seen ON devices(last_seen);

CREATE TABLE plan_features (
    plan_id TEXT NOT NULL REFERENCES plans(id),
    feature_key TEXT NOT NULL,
    enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
    PRIMARY KEY (plan_id, feature_key)
);

CREATE TABLE license_sessions (
    id TEXT PRIMARY KEY,
    license_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    created_at INTEGER NOT NULL,
    revoked_at INTEGER,
    FOREIGN KEY (license_id, device_id) REFERENCES devices(license_id, device) ON DELETE CASCADE
);
CREATE INDEX license_sessions_device ON license_sessions(license_id, device_id);
CREATE UNIQUE INDEX license_sessions_current ON license_sessions(license_id, device_id) WHERE revoked_at IS NULL;

CREATE TABLE release_channels (
    channel TEXT PRIMARY KEY CHECK (channel IN ('stable', 'test')),
    current_version TEXT,
    min_supported_version TEXT NOT NULL DEFAULT '0.0.0',
    maintenance INTEGER NOT NULL DEFAULT 0 CHECK (maintenance IN (0, 1))
);
INSERT INTO release_channels(channel) VALUES ('stable'), ('test');

CREATE TABLE releases (
    id TEXT PRIMARY KEY,
    version TEXT NOT NULL,
    channel TEXT NOT NULL REFERENCES release_channels(channel),
    manifest_url TEXT NOT NULL,
    checksum TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('ready', 'withdrawn')),
    created_at INTEGER NOT NULL,
    UNIQUE (channel, version)
);

CREATE TABLE feature_flags (
    feature_key TEXT NOT NULL,
    value_json TEXT NOT NULL CHECK (json_valid(value_json)),
    scope_type TEXT NOT NULL CHECK (scope_type IN ('global', 'plan', 'license')),
    scope_id TEXT NOT NULL DEFAULT '',
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (feature_key, scope_type, scope_id)
);

CREATE TABLE audit_logs (
    id TEXT PRIMARY KEY,
    actor TEXT NOT NULL,
    action TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    occurred_at INTEGER NOT NULL,
    metadata_json TEXT NOT NULL CHECK (json_valid(metadata_json))
);
CREATE INDEX audit_logs_time ON audit_logs(occurred_at DESC, id DESC);

CREATE TABLE system_settings (
    key TEXT PRIMARY KEY,
    value_json TEXT NOT NULL CHECK (json_valid(value_json)),
    updated_at INTEGER NOT NULL
);
