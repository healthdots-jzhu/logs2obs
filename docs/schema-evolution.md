# Schema Evolution

This guide explains how logs2obs handles schema changes over time without breaking existing Parquet files or queries.

---

## Why Schema Evolution Matters

Observability systems ingest logs for months or years. During this time:

- **New fields are added** to log entries (e.g., `kubernetes.pod_name`)
- **Field types evolve** (e.g., `user_id` changes from integer to UUID string)
- **Old fields are deprecated** (e.g., `legacy_trace_id` replaced by `trace_id`)

### The Challenge: Parquet Immutability

Parquet files are **immutable** once written. You cannot retroactively add columns to existing files. logs2obs solves this with:

1. **Schema versioning** — Each tenant has a schema history with semantic versioning
2. **Forward compatibility** — New readers can read old files (missing columns return NULL)
3. **Backward compatibility** — Old readers can read new files (extra columns are ignored)
4. **Schema merging on read** — DuckDB/Athena merge schemas via column union

---

## Schema Evolution Rules

| Change Type | Safe? | Action Required | Notes |
|-------------|-------|-----------------|-------|
| ✅ **Add optional field** | Yes | Increment minor version | New column, nullable. Old files return NULL for this column. |
| ✅ **Rename field (with alias)** | Yes | Add new field, mark old as deprecated for 2 versions | Keep both fields for migration period. |
| ⚠️ **Change type (widening)** | Conditional | Only int→long, float→double allowed | Parquet handles safely. String widening always safe. |
| ❌ **Remove required field** | No | Must deprecate for 2 versions first | Breaking change — requires major version bump. |
| ❌ **Change type (narrowing)** | No | Forbidden | long→int, double→float are breaking changes. |
| ❌ **Change type (incompatible)** | No | Forbidden | string→int, int→timestamp are breaking changes. |

---

## How to Add a New Optional Field

### Step 1: Register Schema Version via API

```bash
curl -X POST http://localhost:5000/api/v1/schema \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "version": "2.1.0",
    "fields": [
      {
        "name": "user_id",
        "type": "string",
        "nullable": false,
        "description": "User identifier"
      },
      {
        "name": "session_id",
        "type": "string",
        "nullable": false,
        "description": "Session identifier"
      },
      {
        "name": "feature_flag_enabled",
        "type": "bool",
        "nullable": true,
        "description": "NEW: Whether feature flag X is enabled for this request"
      }
    ]
  }'
```

### Step 2: Start Sending Logs with New Field

```bash
curl -X POST http://localhost:5000/api/v1/ingest \
  -H "X-Api-Key: ls_key" \
  -d '{
    "entries": [
      {
        "id": "01JGXYZ1234567890ABCDEFGH",
        "sourceId": "api-gateway",
        "message": "User login successful",
        "tags": {
          "user_id": "user-12345",
          "session_id": "sess-67890",
          "feature_flag_enabled": "true"
        }
      }
    ]
  }'
```

### Step 3: Existing Parquet Files Unaffected

Old Parquet files (written before schema v2.1.0) **do not have** the `feature_flag_enabled` column. When queried:

```sql
SELECT user_id, feature_flag_enabled FROM logs WHERE day='23';
```

**Result:**
- Rows from old files: `feature_flag_enabled` = NULL
- Rows from new files: `feature_flag_enabled` = true/false

This is **safe** because the field is nullable.

---

## How to Deprecate and Remove a Field

### Step 1: Mark Field as Deprecated (v2.2.0)

```bash
curl -X PATCH http://localhost:5000/api/v1/schema/tenant-abc/fields/legacy_trace_id \
  -H "X-Api-Key: ls_key" \
  -d '{
    "deprecated": true,
    "deprecationMessage": "Use trace_id instead. Removal scheduled for v3.0.0.",
    "replacedBy": "trace_id"
  }'
```

### Step 2: Migration Period (2 versions minimum)

During the migration period (e.g., v2.2.0 → v2.3.0):

- **Both fields exist** in the schema
- Logs can send **either or both** fields
- Worker writes **both fields** to Parquet (if present)
- Queries can reference **either field**

Example: Dual-write during migration:

```json
{
  "id": "01JGXYZ...",
  "message": "API request processed",
  "tags": {
    "legacy_trace_id": "abc123",
    "trace_id": "01JGXYZ1234567890ABCDEFGH"
  }
}
```

### Step 3: Remove Field (v3.0.0 — Major Version)

After 2 versions (e.g., v2.2.0 and v2.3.0 have passed):

```bash
curl -X DELETE http://localhost:5000/api/v1/schema/tenant-abc/fields/legacy_trace_id \
  -H "X-Api-Key: ls_key"
```

**Breaking change:** Queries referencing `legacy_trace_id` will fail. Schema version bumps to v3.0.0.

---

## Schema Inference Mode

If you **do not** manually register a schema, logs2obs uses **automatic schema inference** from the first 100 log entries:

### How It Works

1. Worker receives first batch of logs for a new `sourceId`
2. `SchemaInferenceEngine.InferSchema()` inspects the `tags` dictionary
3. For each tag key, infers type based on value parsing:
   - All values parse as `bool` → field type = `bool`
   - All values parse as `long` → field type = `int64`
   - All values parse as `double` → field type = `double`
   - All values parse as `DateTimeOffset` → field type = `timestamp`
   - Otherwise → field type = `string`
4. Schema version `1.0.0` is auto-registered

### Example: Inferred Schema

**Log entry:**
```json
{
  "tags": {
    "user_id": "12345",
    "latency_ms": "142",
    "success": "true",
    "timestamp_utc": "2026-03-23T10:15:30Z"
  }
}
```

**Inferred schema:**
```json
{
  "version": "1.0.0",
  "fields": [
    { "name": "user_id", "type": "string", "nullable": true },
    { "name": "latency_ms", "type": "int64", "nullable": true },
    { "name": "success", "type": "bool", "nullable": true },
    { "name": "timestamp_utc", "type": "timestamp", "nullable": true }
  ]
}
```

### When to Use Manual vs Inferred Schema

| Use Manual Schema | Use Inferred Schema |
|-------------------|---------------------|
| Production systems with strict schema contracts | Exploratory logs, ad-hoc data ingestion |
| Need to enforce required fields | Schema flexibility is important |
| Need precise type control (e.g., `decimal` vs `double`) | Rapid prototyping |
| Multi-tenant systems with per-tenant schemas | Single-tenant or hobby projects |

---

## How to Register a Manual Schema Version via API

```bash
curl -X POST http://localhost:5000/api/v1/schema \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "version": "2.3.0",
    "fields": [
      {
        "name": "user_id",
        "type": "string",
        "nullable": false,
        "description": "User identifier",
        "indexed": true
      },
      {
        "name": "latency_ms",
        "type": "double",
        "nullable": true,
        "description": "Request latency in milliseconds"
      },
      {
        "name": "feature_flags",
        "type": "json",
        "nullable": true,
        "description": "JSON map of feature flag states"
      }
    ]
  }'
```

**Response:**
```json
{
  "tenantId": "tenant-abc",
  "version": "2.3.0",
  "registeredAt": "2026-03-23T14:22:10Z",
  "fieldCount": 3
}
```

---

## How to View Schema History

```bash
GET /api/v1/schema/tenant-abc
```

**Response:**
```json
{
  "tenantId": "tenant-abc",
  "currentVersion": "2.3.0",
  "versions": [
    {
      "version": "1.0.0",
      "registeredAt": "2026-01-10T08:00:00Z",
      "fieldCount": 5,
      "isInferred": true
    },
    {
      "version": "2.0.0",
      "registeredAt": "2026-02-15T12:30:00Z",
      "fieldCount": 7,
      "isInferred": false,
      "changes": ["Added: session_id", "Added: environment"]
    },
    {
      "version": "2.1.0",
      "registeredAt": "2026-03-01T09:45:00Z",
      "fieldCount": 8,
      "isInferred": false,
      "changes": ["Added: feature_flag_enabled (nullable)"]
    },
    {
      "version": "2.2.0",
      "registeredAt": "2026-03-10T11:00:00Z",
      "fieldCount": 9,
      "isInferred": false,
      "changes": ["Deprecated: legacy_trace_id", "Added: trace_id"]
    },
    {
      "version": "2.3.0",
      "registeredAt": "2026-03-23T14:22:10Z",
      "fieldCount": 9,
      "isInferred": false,
      "changes": ["Changed: latency_ms type int64→double"]
    }
  ]
}
```

---

## Parquet Schema Merging Behavior

When querying across multiple Parquet files with **different schemas**, DuckDB and Athena perform **column union** merging:

### Example: Schema Merge

**File 1 (written with schema v1.0.0):**
```
Columns: id, timestamp, message, user_id, session_id
```

**File 2 (written with schema v2.1.0):**
```
Columns: id, timestamp, message, user_id, session_id, feature_flag_enabled
```

**Query:**
```sql
SELECT id, user_id, feature_flag_enabled FROM logs WHERE day='23';
```

**Result:**
- Rows from File 1: `feature_flag_enabled` = NULL (column doesn't exist)
- Rows from File 2: `feature_flag_enabled` = true/false (column exists)

### Key Rules

1. **Column union:** Merged schema = union of all columns from all files
2. **Type compatibility:** If a column has different types across files, query fails (e.g., `user_id` is `int64` in file 1 and `string` in file 2)
3. **Nullable enforcement:** All merged columns are treated as nullable (even if some files have non-null constraints)

---

## Best Practices

1. **Always increment version numbers** when changing schema (use semantic versioning: MAJOR.MINOR.PATCH)
2. **Use minor version bumps** for backward-compatible changes (adding optional fields)
3. **Use major version bumps** for breaking changes (removing fields, incompatible type changes)
4. **Deprecate before removing** — keep old fields for at least 2 versions
5. **Test schema changes in dev** before rolling out to production
6. **Document schema changes** in the `description` field and in your team's changelog
7. **Use inferred schemas for prototyping** — switch to manual schemas for production

---

## Troubleshooting

### "Column type mismatch" error

**Cause:** A field has different types in different Parquet files (e.g., `user_id` was `int64`, now `string`).

**Fix:** Either:
- Reprocess old files via replay to convert to new type (major version bump required)
- Use type casting in queries: `CAST(user_id AS VARCHAR)`

### Query returns NULL for new field in old data

**Expected behavior:** Old Parquet files don't have the new field. Queries return NULL. This is safe if the field is nullable.

### Schema version conflict error

**Cause:** Trying to register a schema version that already exists with different fields.

**Fix:** Increment the version number.

---

## API Reference

### Register Schema Version
```
POST /api/v1/schema
```

### Get Schema History
```
GET /api/v1/schema/{tenantId}
```

### Get Specific Schema Version
```
GET /api/v1/schema/{tenantId}/versions/{version}
```

### Deprecate Field
```
PATCH /api/v1/schema/{tenantId}/fields/{fieldName}
```

### Delete Field (Breaking Change)
```
DELETE /api/v1/schema/{tenantId}/fields/{fieldName}
```
