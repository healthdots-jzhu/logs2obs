# Materialized Views

This guide explains how **materialized views** in logs2obs provide sub-millisecond dashboard reads by pre-aggregating frequently queried data.

---

## Why Materialized Views Matter

### The Problem: Expensive Live Aggregations

Dashboard queries like "Error rate per minute" run **expensive aggregations** on OpenSearch:

```sql
SELECT date_trunc('minute', timestamp) AS bucket, COUNT(*) 
FROM logs 
WHERE level='Error' 
GROUP BY bucket
```

**Cost:**
- **Latency:** 500ms–2s per query (scans millions of docs)
- **Compute:** High CPU/memory usage on OpenSearch cluster
- **Concurrent users:** 10 dashboards refreshing every 5s = 120 queries/minute = cluster overload

---

### The Solution: Pre-aggregated Materialized Views

**Materialized views (matviews)** pre-compute aggregations in the background and store results in **Redis** for sub-millisecond reads:

| Metric | Live OpenSearch Aggregation | Materialized View (Redis) |
|--------|----------------------------|----------------------------|
| **Latency** | 500ms–2s | <5ms |
| **Compute cost** | High (every query scans data) | Low (read from cache) |
| **Freshness** | Real-time | 1–5 minutes (configurable) |
| **Scalability** | Limited (cluster capacity) | Unlimited (Redis read replicas) |

**Use case:** Dashboards, monitoring screens, public status pages — anything that queries the same aggregation repeatedly.

---

## Standard Prebuilt Materialized Views

logs2obs includes **3 standard matviews** out of the box (defined in `Logs2Obs.Core.MatViews.StandardMatViews`):

### 1. `error_rate_per_minute`

**Description:** Count of Error/Fatal log entries per service per minute.

**Aggregation:**
```sql
SELECT
  date_trunc('minute', timestamp) AS minute_bucket,
  sourceid AS service,
  COUNT(*) AS error_count
FROM logs
WHERE logtype = 'Error' AND level IN ('Error', 'Fatal')
  AND tenantid = '{tenantId}'
  AND timestamp >= NOW() - INTERVAL '2' MINUTE
GROUP BY 1, 2
```

**Refresh cadence:** Every 60 seconds

**Retention:** 1,440 minutes (24 hours)

**Storage:** Redis key `matview:error_rate_per_minute:{tenantId}`

**Suggested graph type:** `AreaChart`

**Use case:** Real-time error monitoring dashboard, alert thresholds (e.g., "alert if error_count > 100 in last minute").

---

### 2. `latency_p99_per_service`

**Description:** P50/P95/P99 latency per service per 5-minute bucket.

**Aggregation:**
```sql
SELECT
  date_trunc('minute', timestamp) - INTERVAL MOD(MINUTE(timestamp), 5) MINUTE AS bucket,
  sourceid AS service,
  APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.50) AS p50_ms,
  APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.95) AS p95_ms,
  APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.99) AS p99_ms
FROM logs
WHERE logtype = 'Metric' AND category = 'http-latency'
  AND tenantid = '{tenantId}'
  AND timestamp >= NOW() - INTERVAL '10' MINUTE
GROUP BY 1, 2
```

**Refresh cadence:** Every 300 seconds (5 minutes)

**Retention:** 2,880 minutes (48 hours)

**Storage:** Redis key `matview:latency_p99_per_service:{tenantId}`

**Suggested graph type:** `LineChart`

**Use case:** SLO dashboards (e.g., "P99 latency < 200ms"), performance monitoring, capacity planning.

---

### 3. `log_volume_by_type`

**Description:** Total log entries per log type per minute.

**Aggregation:**
```sql
SELECT
  date_trunc('minute', timestamp) AS minute_bucket,
  logtype,
  COUNT(*) AS entry_count
FROM logs
WHERE tenantid = '{tenantId}'
  AND timestamp >= NOW() - INTERVAL '2' MINUTE
GROUP BY 1, 2
```

**Refresh cadence:** Every 60 seconds

**Retention:** 1,440 minutes (24 hours)

**Storage:** Redis key `matview:log_volume_by_type:{tenantId}`

**Suggested graph type:** `StackedAreaChart`

**Use case:** Ingestion monitoring (track log volume trends), cost estimation (logs per day), anomaly detection (sudden spike in volume).

---

## How to Query a Materialized View via API

### API Endpoint

```
GET /api/v1/matviews/{viewName}?tenantId={tenantId}
```

### Example: Get Error Rate Per Minute

```bash
curl -X GET "http://localhost:5000/api/v1/matviews/error_rate_per_minute?tenantId=tenant-abc" \
  -H "X-Api-Key: ls_key"
```

**Response:**
```json
{
  "viewName": "error_rate_per_minute",
  "tenantId": "tenant-abc",
  "refreshedAt": "2026-03-23T14:22:00Z",
  "freshnessSeconds": 15,
  "data": [
    {
      "minute_bucket": "2026-03-23T14:20:00Z",
      "service": "api-gateway",
      "error_count": 42
    },
    {
      "minute_bucket": "2026-03-23T14:21:00Z",
      "service": "api-gateway",
      "error_count": 38
    },
    {
      "minute_bucket": "2026-03-23T14:20:00Z",
      "service": "checkout",
      "error_count": 15
    },
    {
      "minute_bucket": "2026-03-23T14:21:00Z",
      "service": "checkout",
      "error_count": 19
    }
  ],
  "suggestedGraphType": "AreaChart"
}
```

---

### Example: Get P99 Latency Per Service

```bash
curl -X GET "http://localhost:5000/api/v1/matviews/latency_p99_per_service?tenantId=tenant-abc" \
  -H "X-Api-Key: ls_key"
```

**Response:**
```json
{
  "viewName": "latency_p99_per_service",
  "tenantId": "tenant-abc",
  "refreshedAt": "2026-03-23T14:20:00Z",
  "freshnessSeconds": 180,
  "data": [
    {
      "bucket": "2026-03-23T14:15:00Z",
      "service": "api-gateway",
      "p50_ms": 23.4,
      "p95_ms": 87.2,
      "p99_ms": 142.5
    },
    {
      "bucket": "2026-03-23T14:15:00Z",
      "service": "checkout",
      "p50_ms": 45.1,
      "p95_ms": 156.8,
      "p99_ms": 312.4
    }
  ],
  "suggestedGraphType": "LineChart"
}
```

---

## Refresh Cadence and Freshness

### How Refresh Works

1. **Background worker** (`MatViewRefreshWorker`) subscribes to `ls-matview-refresh` queue
2. Every N seconds (per matview), a refresh job is triggered
3. Worker executes the matview SQL against OpenSearch
4. Results are serialized to JSON and stored in Redis with TTL
5. Redis key: `matview:{viewName}:{tenantId}`

### Refresh Cadence

| Matview | Refresh Interval | Reasoning |
|---------|------------------|-----------|
| `error_rate_per_minute` | 60 seconds | Errors need near-real-time visibility |
| `latency_p99_per_service` | 300 seconds | Latency trends are stable over 5-min windows |
| `log_volume_by_type` | 60 seconds | Volume spikes need quick detection |

### Freshness Guarantees

**Freshness** = Time since last refresh.

Example:
- Matview last refreshed at `14:20:00`
- Current time: `14:22:15`
- Freshness = **135 seconds**

**API response includes `freshnessSeconds` field:**
```json
{
  "refreshedAt": "2026-03-23T14:20:00Z",
  "freshnessSeconds": 135
}
```

**Client behavior:**
- If `freshnessSeconds < 2 × RefreshInterval` → data is **fresh** (green status)
- If `freshnessSeconds >= 2 × RefreshInterval` → data is **stale** (yellow warning)
- If `freshnessSeconds >= 4 × RefreshInterval` → fallback to **live query** (see below)

---

## Fallback Behavior: Stale → Live Query

If a matview is **stale or missing**, the API **transparently falls back** to a live OpenSearch aggregation:

### Example: Matview Stale

**Request:**
```bash
GET /api/v1/matviews/error_rate_per_minute?tenantId=tenant-abc
```

**Scenario:** Redis key `matview:error_rate_per_minute:tenant-abc` is missing or expired.

**Fallback behavior:**
1. API detects missing/stale matview
2. Executes live OpenSearch aggregation (SQL from `MatViewDefinition.Sql`)
3. Returns result with `isFallback: true` flag

**Response:**
```json
{
  "viewName": "error_rate_per_minute",
  "tenantId": "tenant-abc",
  "refreshedAt": null,
  "freshnessSeconds": null,
  "isFallback": true,
  "fallbackReason": "Matview not found in Redis — executed live query",
  "data": [ ... ],
  "queryLatencyMs": 842
}
```

**Benefits:**
- **No data loss** — clients always get results (even if matview is down)
- **Transparent degradation** — dashboards continue working at slower latency
- **Self-healing** — next refresh populates Redis again

---

## How to Register a Custom Materialized View

If you need a matview not included in the standard set, register it via API:

### API Endpoint

```
POST /api/v1/matviews
```

### Request Body

```json
{
  "tenantId": "tenant-abc",
  "name": "http_status_codes_per_hour",
  "description": "Count of HTTP status codes per hour for last 7 days",
  "sql": "SELECT date_trunc('hour', timestamp) AS hour_bucket, tags->>'http_status' AS status_code, COUNT(*) AS request_count FROM logs WHERE logtype = 'Metric' AND category = 'http-request' AND tenantid = '{tenantId}' AND timestamp >= NOW() - INTERVAL '7' DAY GROUP BY 1, 2",
  "refreshIntervalSeconds": 3600,
  "retentionMinutes": 10080,
  "storageTarget": "Redis",
  "suggestedGraphType": "BarChart"
}
```

### Response

```json
{
  "tenantId": "tenant-abc",
  "name": "http_status_codes_per_hour",
  "version": "1.0.0",
  "registeredAt": "2026-03-23T15:00:00Z",
  "nextRefreshAt": "2026-03-23T16:00:00Z"
}
```

### SQL Placeholder: `{tenantId}`

The `{tenantId}` placeholder is **automatically replaced** at query execution time:

```sql
-- Registered SQL template
WHERE tenantid = '{tenantId}' AND ...

-- Actual executed SQL
WHERE tenantid = 'tenant-abc' AND ...
```

This allows a single matview definition to work for all tenants.

---

## Redis Storage Format

### Key Pattern

```
matview:{viewName}:{tenantId}
```

**Examples:**
- `matview:error_rate_per_minute:tenant-abc`
- `matview:latency_p99_per_service:tenant-xyz`
- `matview:http_status_codes_per_hour:tenant-abc`

### Value Structure

Stored as **JSON string**:

```json
{
  "viewName": "error_rate_per_minute",
  "tenantId": "tenant-abc",
  "refreshedAt": "2026-03-23T14:22:00Z",
  "data": [
    { "minute_bucket": "2026-03-23T14:20:00Z", "service": "api-gateway", "error_count": 42 },
    { "minute_bucket": "2026-03-23T14:21:00Z", "service": "api-gateway", "error_count": 38 }
  ],
  "rowCount": 2
}
```

### TTL

TTL = `RetentionMinutes × 60` (converted to seconds)

**Example:**
- `error_rate_per_minute` has `RetentionMinutes = 1440` (24 hours)
- Redis TTL = `1440 × 60 = 86,400 seconds` (1 day)

After TTL expires, the key is **automatically deleted** by Redis.

---

## Monitoring Materialized Views

### View All Registered Matviews

```bash
GET /api/v1/matviews?tenantId=tenant-abc
```

**Response:**
```json
{
  "tenantId": "tenant-abc",
  "views": [
    {
      "name": "error_rate_per_minute",
      "refreshIntervalSeconds": 60,
      "lastRefreshedAt": "2026-03-23T14:22:00Z",
      "nextRefreshAt": "2026-03-23T14:23:00Z",
      "status": "Healthy"
    },
    {
      "name": "latency_p99_per_service",
      "refreshIntervalSeconds": 300,
      "lastRefreshedAt": "2026-03-23T14:20:00Z",
      "nextRefreshAt": "2026-03-23T14:25:00Z",
      "status": "Healthy"
    },
    {
      "name": "log_volume_by_type",
      "refreshIntervalSeconds": 60,
      "lastRefreshedAt": "2026-03-23T14:10:00Z",
      "nextRefreshAt": "2026-03-23T14:11:00Z",
      "status": "Stale"
    }
  ]
}
```

### Matview Health Status

| Status | Condition | Action |
|--------|-----------|--------|
| **Healthy** | `freshnessSeconds < 2 × RefreshInterval` | None — matview is up to date |
| **Stale** | `freshnessSeconds >= 2 × RefreshInterval` | Warning — check refresh worker logs |
| **Missing** | Redis key doesn't exist | Alert — matview never refreshed (check worker) |

---

### Telemetry Metrics

```promql
# Matview refresh latency (seconds)
histogram_quantile(0.99, rate(logs2obs_matview_refresh_duration_seconds_bucket[5m]))

# Matview refresh error rate
rate(logs2obs_matview_refresh_errors_total[5m])

# Matview fallback rate (live queries due to stale data)
rate(logs2obs_matview_fallback_total[5m])
```

---

## Performance Characteristics

| Metric | Materialized View | Live OpenSearch Query |
|--------|-------------------|----------------------|
| **Read latency** | <5ms (Redis GET) | 500ms–2s (aggregation) |
| **Throughput** | 100k+ QPS (Redis) | 10–50 QPS (OpenSearch) |
| **Freshness** | 1–5 minutes (configurable) | Real-time |
| **Memory cost** | ~1 KB per matview per tenant | None (computed on demand) |
| **Compute cost** | Low (refresh every N minutes) | High (every query scans data) |

**Rule of thumb:** Use matviews for dashboards queried >10 times/minute. Use live queries for ad-hoc analysis.

---

## Troubleshooting

### Matview Always Returns `isFallback: true`

**Possible causes:**
1. MatViewRefreshWorker not running
2. Redis connection failure
3. Matview SQL syntax error

**Fix:**
- Check worker logs: `kubectl logs -l app=logs2obs-worker | grep MatViewRefresh`
- Test Redis connection: `redis-cli PING`
- Test matview SQL manually via `/api/v1/query` endpoint

---

### Matview Data is Stale (High `freshnessSeconds`)

**Possible causes:**
1. Refresh interval too long
2. OpenSearch aggregation query is slow
3. Worker overwhelmed (high queue depth)

**Fix:**
- Reduce `refreshIntervalSeconds` (e.g., 60s → 30s)
- Optimize matview SQL (add indexes, reduce time range)
- Scale worker pods: `kubectl scale deployment logs2obs-worker --replicas=5`

---

### High Memory Usage in Redis

**Possible causes:**
1. Too many matviews registered
2. Retention too long (e.g., 7 days of per-minute data)
3. Large result sets (e.g., 10,000 rows per matview)

**Fix:**
- Delete unused matviews: `DELETE /api/v1/matviews/{viewName}`
- Reduce `retentionMinutes` (e.g., 1440 → 720)
- Add `LIMIT` clause to matview SQL (e.g., `LIMIT 1000`)

---

## API Reference

### Query Matview
```
GET /api/v1/matviews/{viewName}?tenantId={tenantId}
```

### List All Matviews
```
GET /api/v1/matviews?tenantId={tenantId}
```

### Register Custom Matview
```
POST /api/v1/matviews
```

### Update Matview Refresh Interval
```
PATCH /api/v1/matviews/{viewName}
```

### Delete Matview
```
DELETE /api/v1/matviews/{viewName}?tenantId={tenantId}
```

### Force Refresh Matview (Admin)
```
POST /api/v1/matviews/{viewName}/refresh?tenantId={tenantId}
```

---

## Summary

- **Materialized views** pre-aggregate frequently queried data for <5ms dashboard reads
- **3 standard matviews** included: `error_rate_per_minute`, `latency_p99_per_service`, `log_volume_by_type`
- **Stored in Redis** with configurable TTL (e.g., 24 hours)
- **Background refresh** every N seconds (e.g., 60s for errors, 300s for latency)
- **Transparent fallback** to live OpenSearch query if matview is stale or missing
- **Register custom matviews** via API for tenant-specific aggregations
- **Monitor health** via `/api/v1/matviews` endpoint and telemetry metrics
