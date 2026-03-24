-- Metadata store tables (created on demand by adapter, but pre-create common ones)
CREATE TABLE IF NOT EXISTS metadata_tenants (
    key TEXT PRIMARY KEY,
    value JSONB NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS metadata_pull_jobs (
    key TEXT PRIMARY KEY,
    value JSONB NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS metadata_saved_queries (
    key TEXT PRIMARY KEY,
    value JSONB NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS metadata_alert_rules (
    key TEXT PRIMARY KEY,
    value JSONB NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Schema registry
CREATE TABLE IF NOT EXISTS schema_registry (
    id SERIAL PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    version INT NOT NULL,
    fields JSONB NOT NULL,
    registered_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE (tenant_id, version)
);

CREATE INDEX IF NOT EXISTS idx_schema_registry_tenant ON schema_registry(tenant_id);
