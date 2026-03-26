# logs2obs API Reference

## Base URL

- **Local Development:** `http://localhost:8080`
- **Docker Compose (API service):** `http://localhost:8080`
- **Production:** `https://api.logs2obs.example.com` (configure per deployment)

## Authentication

logs2obs supports two authentication methods:

### 1. API Key Authentication

Send the API key in the `X-Api-Key` header. Suitable for agents, CI/CD pipelines, and service-to-service calls.

```bash
curl -H "X-Api-Key: ls_your_api_key_here" http://localhost:8080/api/v1/logs
```

**Key format:** `ls_` prefix + 32 alphanumeric characters (e.g., `ls_a1b2c3d4e5f6...`)

### 2. JWT Bearer Token

Send a JWT token in the `Authorization: Bearer {token}` header. Suitable for web UI and user-initiated queries.

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." http://localhost:8080/api/v1/query/natural
```

**Note:** JWT tokens are issued by Cognito (AWS), Entra ID (Azure), or Firebase Auth (GCP). The `sub` claim maps to `tenantId`.

---

## Ingest Endpoints

### POST /api/v1/logs

Ingest log entries via REST/JSON.

**Auth Required:** API Key or JWT  
**Rate Limit:** `tenant-ingest` (1000 tokens, refill 500/sec)

**Request Body:**

```json
{
  "entries": [
    {
      "sourceId": "string",           // Required: service/app name (e.g., "payment-service")
      "logType": "string",            // Required: Application | Error | Network | OS | Metric | Audit | Custom
      "level": "string",              // Required: Trace | Debug | Info | Warn | Error | Fatal
      "environment": "string",        // Required: dev | staging | prod | test
      "category": "string",           // Required: exception | request | event | metric
      "timestamp": "string",          // Required: ISO 8601 timestamp (e.g., "2026-03-23T14:32:00Z")
      "message": "string",            // Required: log message
      "stackTrace": "string",         // Optional: exception stack trace
      "traceId": "string",            // Optional: distributed trace ID
      "spanId": "string",             // Optional: span ID within trace
      "hostname": "string",           // Optional: host/pod name
      "ipAddress": "string",          // Optional: source IP
      "http": {                       // Optional: HTTP context
        "method": "string",
        "path": "string",
        "statusCode": 0,
        "durationMs": 0,
        "userAgent": "string",
        "clientIp": "string",
        "requestBytes": 0,
        "responseBytes": 0
      },
      "metric": {                     // Optional: metric payload
        "metricName": "string",
        "value": 0.0,
        "unit": "string",             // ms | bytes | percent | count | rps | errors/s
        "dimensions": { "key": "value" }
      },
      "tags": { "key": "value" },     // Optional: indexed tags (searchable)
      "metadata": { "key": "value" }  // Optional: non-indexed metadata
    }
  ]
}
```

**Response (200 OK):**

```json
{
  "accepted": 1,
  "rejected": 0,
  "requestId": "550e8400-e29b-41d4-a716-446655440000"
}
```

**Error Codes:**
- `400 Bad Request` — Invalid JSON or validation failure
- `401 Unauthorized` — Missing or invalid API key/JWT
- `413 Payload Too Large` — Request body >10 MB
- `429 Too Many Requests` — Rate limit exceeded
- `500 Internal Server Error` — Server error (check logs)

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/logs \
  -H "X-Api-Key: ls_your_api_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "entries": [{
      "sourceId": "my-svc",
      "logType": "Error",
      "level": "Error",
      "environment": "dev",
      "category": "exception",
      "timestamp": "2026-03-23T10:00:00Z",
      "message": "NullRef in PaymentProcessor",
      "tags": {"orderId": "ORD-123"}
    }]
  }'
```

---

### POST /api/v1/logs/metrics

Ingest metrics (convenience endpoint for metric-only payloads).

**Auth Required:** API Key or JWT  
**Rate Limit:** `tenant-ingest`

**Request Body:**

```json
{
  "entries": [
    {
      "sourceId": "string",
      "environment": "string",
      "timestamp": "string",
      "metricName": "string",
      "value": 0.0,
      "unit": "string",
      "dimensions": { "key": "value" }
    }
  ]
}
```

**Response:** Same as `/api/v1/logs`

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/logs/metrics \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "entries": [{
      "sourceId": "api-gateway",
      "environment": "prod",
      "timestamp": "2026-03-23T14:32:00Z",
      "metricName": "request_duration_ms",
      "value": 245.3,
      "unit": "ms",
      "dimensions": {"endpoint": "/api/users", "method": "GET"}
    }]
  }'
```

---

## Query Endpoints

### POST /api/v1/query/sql

Execute a SQL query against logs2obs data.

**Auth Required:** API Key or JWT  
**Rate Limit:** `tenant-query` (100 requests/min)

**Request Body:**

```json
{
  "sql": "string",                    // Required: SQL query (SELECT only, no DML/DDL)
  "async": false,                     // Optional: true = return queryId immediately, false = wait for results
  "confirmCostIfAboveUsd": 0.05       // Optional: require user confirmation if estimated cost exceeds this value
}
```

**Response (200 OK) — Sync Mode:**

```json
{
  "queryId": "qry_01HZ...",
  "status": "Completed",
  "tier": "Hot",
  "results": {
    "columns": ["sourceId", "count"],
    "rows": [
      ["payment-service", 142],
      ["auth-service", 37]
    ]
  },
  "executionTimeMs": 87,
  "scannedBytes": 1048576
}
```

**Response (202 Accepted) — Async Mode:**

```json
{
  "queryId": "qry_01HZ...",
  "status": "Running"
}
```

**Response (409 Conflict) — Cost Confirmation Required:**

```json
{
  "queryId": "qry_01HZ...",
  "status": "PendingCostConfirmation",
  "estimatedCostUsd": 1.23,
  "estimatedScannedGb": 450,
  "message": "This query will scan 450 GB and cost ~$1.23. Confirm to proceed."
}
```

**Error Codes:**
- `400 Bad Request` — Invalid SQL (e.g., contains DROP, no LIMIT clause)
- `403 Forbidden` — SQL safety check failed (missing partition filter, CROSS JOIN without confirmation)
- `500 Internal Server Error` — Query execution failure

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/query/sql \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "sql": "SELECT sourceid, COUNT(*) FROM logs WHERE logtype='\''Error'\'' AND year='\''2026'\'' AND month='\''03'\'' AND day='\''23'\'' GROUP BY 1 LIMIT 20",
    "async": true,
    "confirmCostIfAboveUsd": 0.05
  }'
```

---

### POST /api/v1/query/natural

Execute a natural language query. AI translates the question to SQL, validates safety, and executes.

**Auth Required:** API Key or JWT  
**Rate Limit:** `tenant-query`

**Request Body:**

```json
{
  "question": "string",               // Required: natural language question (e.g., "How many fatal errors per service yesterday?")
  "environment": "string"             // Optional: filter to specific environment (dev | staging | prod)
}
```

**Response (200 OK):**

```json
{
  "queryId": "qry_01HZ...",
  "sql": "SELECT sourceid AS service, COUNT(*) AS fatal_errors FROM logs WHERE logtype='Error' AND level='Fatal' AND environment='prod' AND year='2026' AND month='03' AND day='22' GROUP BY 1 ORDER BY 2 DESC LIMIT 50",
  "explanation": "Counting Fatal errors per service on 2026-03-22 in prod environment.",
  "tier": "Hot",
  "results": {
    "columns": ["service", "fatal_errors"],
    "rows": [
      ["payment-service", 142],
      ["auth-service", 37]
    ]
  },
  "graphSpec": {
    "type": "HorizontalBarChart",
    "vegaLiteSpec": { ... },
    "chartJsConfig": { ... }
  },
  "safetyWarnings": []
}
```

**Error Codes:**
- `400 Bad Request` — AI returned unparseable response
- `403 Forbidden` — AI-generated SQL failed safety validation
- `500 Internal Server Error` — AI service unavailable

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/query/natural \
  -H "Authorization: Bearer YOUR_JWT" \
  -H "Content-Type: application/json" \
  -d '{"question": "How many fatal errors per service yesterday?"}'
```

---

### GET /api/v1/query/{queryId}/results

Get results of an async query.

**Auth Required:** API Key or JWT  
**Path Parameter:** `queryId` (string)

**Response (200 OK):**

```json
{
  "queryId": "qry_01HZ...",
  "status": "Completed",
  "tier": "Warm",
  "results": {
    "columns": ["timestamp", "message"],
    "rows": [...]
  },
  "executionTimeMs": 3421
}
```

**Response (202 Accepted) — Still Running:**

```json
{
  "queryId": "qry_01HZ...",
  "status": "Running",
  "message": "Query is still executing. Poll again in a few seconds."
}
```

**Example:**

```bash
curl http://localhost:8080/api/v1/query/qry_01HZ.../results \
  -H "X-Api-Key: ls_key"
```

---

## Graph Endpoints

### POST /api/v1/graphs/render

Render a graph from query results. Auto-suggests chart type or uses specified type.

**Auth Required:** API Key or JWT  
**Rate Limit:** `tenant-query`

**Request Body:**

```json
{
  "queryId": "string",                // Required: ID of completed query
  "graphType": "string",              // Optional: Auto | LineChart | BarChart | HorizontalBarChart | AreaChart | Heatmap | DonutChart | ScatterPlot | Gauge | StackedAreaChart | Table
  "options": {                        // Optional: rendering options
    "theme": "dark",                  // dark | light
    "height": 400,
    "colorScheme": "category10"
  }
}
```

**Response (200 OK):**

```json
{
  "graphType": "HorizontalBarChart",
  "vegaLiteSpec": { ... },            // Vega-Lite JSON spec (render with vega-embed)
  "chartJsConfig": { ... },           // Chart.js config (render with Chart.js)
  "suggestedAlternatives": [
    "BarChart",
    "Table"
  ]
}
```

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/graphs/render \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{"queryId": "qry_01HZ...", "graphType": "Auto"}'
```

---

### POST /api/v1/graphs/suggest

Suggest chart types for a given query result schema (without rendering).

**Auth Required:** API Key or JWT  
**Rate Limit:** `tenant-query`

**Request Body:**

```json
{
  "columns": [
    {"name": "timestamp", "type": "timestamp"},
    {"name": "count", "type": "int64"}
  ],
  "rowCount": 100
}
```

**Response (200 OK):**

```json
{
  "suggestions": [
    {
      "graphType": "LineChart",
      "description": "Time Series Trend",
      "priority": 1
    },
    {
      "graphType": "AreaChart",
      "description": "Area Chart Over Time",
      "priority": 2
    }
  ]
}
```

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/graphs/suggest \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "columns": [
      {"name": "timestamp", "type": "timestamp"},
      {"name": "error_count", "type": "int64"}
    ],
    "rowCount": 144
  }'
```

---

## Pull Job Endpoints

### GET /api/v1/pull-jobs

List all pull jobs for the authenticated tenant.

**Auth Required:** API Key or JWT

**Response (200 OK):**

```json
[
  {
    "jobId": "pj_01HZ...",
    "name": "prod-alb-logs",
    "sourceType": "AwsS3",
    "schedule": "0 */5 * * * ?",
    "enabled": true,
    "lastRunAt": "2026-03-23T14:00:00Z",
    "nextRunAt": "2026-03-23T14:05:00Z"
  }
]
```

**Example:**

```bash
curl http://localhost:8080/api/v1/pull-jobs \
  -H "X-Api-Key: ls_key"
```

---

### POST /api/v1/pull-jobs

Create a new pull job.

**Auth Required:** API Key or JWT

**Request Body:**

```json
{
  "name": "string",
  "sourceType": "string",             // AwsS3 | AzureBlob | Gcs | Http
  "schedule": "string",               // Cron expression (e.g., "0 */5 * * * ?")
  "config": {
    "bucketName": "string",
    "prefix": "string",
    "fileFormat": "string",           // W3C | JSON | Syslog | CloudWatch
    "logType": "string",
    "environment": "string",
    "sourceId": "string"
  }
}
```

**Response (201 Created):**

```json
{
  "jobId": "pj_01HZ...",
  "name": "prod-alb-logs",
  "status": "created"
}
```

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/pull-jobs \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "prod-alb-logs",
    "sourceType": "AwsS3",
    "schedule": "0 */5 * * * ?",
    "config": {
      "bucketName": "my-alb-logs",
      "prefix": "alb/prod/",
      "fileFormat": "W3C",
      "logType": "Network",
      "environment": "prod",
      "sourceId": "alb-prod"
    }
  }'
```

---

### PUT /api/v1/pull-jobs/{jobId}

Update an existing pull job.

**Auth Required:** API Key or JWT  
**Path Parameter:** `jobId` (string)

**Request Body:** Same as POST `/api/v1/pull-jobs`

**Response (200 OK):**

```json
{
  "jobId": "pj_01HZ...",
  "status": "updated"
}
```

---

### DELETE /api/v1/pull-jobs/{jobId}

Delete a pull job.

**Auth Required:** API Key or JWT  
**Path Parameter:** `jobId` (string)

**Response (204 No Content)**

**Example:**

```bash
curl -X DELETE http://localhost:8080/api/v1/pull-jobs/pj_01HZ... \
  -H "X-Api-Key: ls_key"
```

---

## Replay Endpoints

### POST /api/v1/replay

Start a replay job to reprocess historical logs from Parquet archives.

**Auth Required:** API Key or JWT  
**Rate Limit:** `tenant-query`

**Request Body:**

```json
{
  "from": "string",                   // ISO 8601 timestamp (e.g., "2026-03-01T00:00:00Z")
  "to": "string",                     // ISO 8601 timestamp
  "options": {
    "reindexSearch": true,            // Re-index to OpenSearch
    "reprocessAlerts": false,         // Re-evaluate alert rules
    "reparseFiles": false             // Re-parse raw files (slow; only if schema changed)
  }
}
```

**Response (202 Accepted):**

```json
{
  "jobId": "rpl_01HZ...",
  "status": "started",
  "estimatedDurationMinutes": 12
}
```

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "from": "2026-03-01T00:00:00Z",
    "to": "2026-03-10T23:59:59Z",
    "options": {"reindexSearch": true, "reprocessAlerts": false}
  }'
```

---

### GET /api/v1/replay/{jobId}

Get replay job status.

**Auth Required:** API Key or JWT  
**Path Parameter:** `jobId` (string)

**Response (200 OK):**

```json
{
  "jobId": "rpl_01HZ...",
  "status": "Running",
  "progress": 0.45,
  "processedEntries": 450000,
  "totalEntries": 1000000,
  "startedAt": "2026-03-23T14:00:00Z",
  "estimatedCompletionAt": "2026-03-23T14:12:00Z"
}
```

**Example:**

```bash
curl http://localhost:8080/api/v1/replay/rpl_01HZ... \
  -H "X-Api-Key: ls_key"
```

---

## Auth Endpoints

### POST /api/v1/auth/keys

Create a new API key for the authenticated user.

**Auth Required:** JWT Bearer Token (not API key — must be user-authenticated)

**Request Body:**

```json
{
  "description": "string",            // Optional: key description (e.g., "CI pipeline key")
  "expiresInDays": 365                // Optional: expiration in days (default: 365, max: 3650)
}
```

**Response (201 Created):**

```json
{
  "keyId": "key_01HZ...",
  "apiKey": "ls_a1b2c3d4e5f6...",     // ONLY returned on creation — store securely
  "description": "CI pipeline key",
  "createdAt": "2026-03-23T14:32:00Z",
  "expiresAt": "2027-03-23T14:32:00Z"
}
```

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/auth/keys \
  -H "Authorization: Bearer YOUR_JWT" \
  -H "Content-Type: application/json" \
  -d '{"description": "My CI pipeline key", "expiresInDays": 365}'
```

---

### DELETE /api/v1/auth/keys/{keyId}

Revoke an API key.

**Auth Required:** JWT Bearer Token  
**Path Parameter:** `keyId` (string)

**Response (204 No Content)**

**Example:**

```bash
curl -X DELETE http://localhost:8080/api/v1/auth/keys/key_01HZ... \
  -H "Authorization: Bearer YOUR_JWT"
```

---

## Alert Endpoints

### GET /api/v1/alerts

List all alert rules for the authenticated tenant.

**Auth Required:** API Key or JWT

**Response (200 OK):**

```json
[
  {
    "alertId": "alr_01HZ...",
    "name": "Fatal Error Spike",
    "sql": "SELECT COUNT(*) FROM logs WHERE level='Fatal' AND environment='prod'",
    "threshold": 10,
    "enabled": true,
    "lastFiredAt": "2026-03-23T13:00:00Z"
  }
]
```

**Example:**

```bash
curl http://localhost:8080/api/v1/alerts \
  -H "X-Api-Key: ls_key"
```

---

### POST /api/v1/alerts

Create a new alert rule.

**Auth Required:** API Key or JWT

**Request Body:**

```json
{
  "name": "string",
  "sql": "string",                    // SQL query (must return single numeric value)
  "threshold": 0.0,
  "comparisonOperator": "string",     // GreaterThan | LessThan | Equal
  "evaluationIntervalMinutes": 5,
  "destinations": [
    {"type": "Slack", "webhookUrl": "https://hooks.slack.com/..."},
    {"type": "PagerDuty", "integrationKey": "..."}
  ]
}
```

**Response (201 Created):**

```json
{
  "alertId": "alr_01HZ...",
  "status": "created"
}
```

**Example:**

```bash
curl -X POST http://localhost:8080/api/v1/alerts \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Fatal Error Spike",
    "sql": "SELECT COUNT(*) FROM logs WHERE level='\''Fatal'\'' AND environment='\''prod'\'' AND year='\''2026'\'' AND month='\''03'\'' AND day='\''23'\''",
    "threshold": 10,
    "comparisonOperator": "GreaterThan",
    "evaluationIntervalMinutes": 5,
    "destinations": [{"type": "Slack", "webhookUrl": "https://hooks.slack.com/services/YOUR/WEBHOOK"}]
  }'
```

---

## Schema Endpoints

### GET /api/v1/schema/{sourceId}

Get the schema history for a specific source (service).

**Auth Required:** API Key or JWT  
**Path Parameter:** `sourceId` (string)

**Response (200 OK):**

```json
{
  "sourceId": "payment-service",
  "currentVersion": 3,
  "versions": [
    {
      "version": 3,
      "registeredAt": "2026-03-20T10:00:00Z",
      "fields": [
        {"name": "orderId", "type": "string", "indexed": true},
        {"name": "amount", "type": "double", "indexed": false}
      ]
    },
    {
      "version": 2,
      "registeredAt": "2026-03-15T10:00:00Z",
      "fields": [...]
    }
  ]
}
```

**Example:**

```bash
curl http://localhost:8080/api/v1/schema/payment-service \
  -H "X-Api-Key: ls_key"
```

---

## Error Codes Summary

| Code | Description |
|---|---|
| `400 Bad Request` | Invalid JSON, validation failure, or malformed SQL |
| `401 Unauthorized` | Missing or invalid API key/JWT |
| `403 Forbidden` | SQL safety check failed or insufficient permissions |
| `404 Not Found` | Query ID, pull job, or alert not found |
| `409 Conflict` | Cost confirmation required before query execution |
| `413 Payload Too Large` | Request body exceeds 10 MB |
| `429 Too Many Requests` | Rate limit exceeded (retry after X seconds) |
| `500 Internal Server Error` | Server error (check logs or contact support) |
| `503 Service Unavailable` | Dependency unavailable (e.g., OpenSearch down) |

---

## Rate Limiting

Rate limits are enforced **per tenant** using token-bucket (for burst) and sliding-window (for sustained) algorithms.

| Endpoint Group | Policy | Limit |
|---|---|---|
| `/api/v1/logs`, `/api/v1/logs/metrics` | `tenant-ingest` | 1000 tokens (burst), refill 500/sec |
| `/api/v1/query/*`, `/api/v1/graphs/*` | `tenant-query` | 100 requests/min (sliding window) |
| `/api/v1/alerts`, `/api/v1/pull-jobs` | `tenant-query` | 100 requests/min |
| `/api/v1/auth/keys` | No limit (requires JWT) | — |

**Rate Limit Response Headers:**

```
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 450
X-RateLimit-Reset: 1679587200
Retry-After: 30
```

**429 Response Body:**

```json
{
  "error": "RateLimitExceeded",
  "message": "Rate limit exceeded. Retry after 30 seconds.",
  "retryAfter": 30
}
```
