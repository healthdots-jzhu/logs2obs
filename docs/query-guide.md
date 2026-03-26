# Query Guide

This guide explains how logs2obs routes queries across storage tiers, SQL best practices, and query optimization techniques.

---

## Tier Routing

logs2obs uses a **three-tier storage architecture** to optimize cost and performance:

| Tier | Storage | Query Engine | Data Age | Latency | Use Case |
|------|---------|-------------|----------|---------|----------|
| **Hot** | OpenSearch | OpenSearch | Last 24h | <200ms | Full-text search, real-time dashboards, aggregations |
| **Warm** | S3 Parquet | DuckDB / Athena | 1–30 days | <5s | Recent analytical queries, time-series analysis |
| **Cold** | S3 Parquet (Glacier) | Athena | >30 days | <30s | Historical analysis, compliance queries, audits |
| **CrossTier** | Multiple | Fan-out | Spans tiers | Varies | Queries spanning multiple retention windows |

### How Routing Works

The `QueryTierRouter` applies **6 routing rules** in order:

1. **Full-text search** → Always Hot (OpenSearch is the only engine supporting full-text)
2. **Entirely within hot window** (last N days) → Hot
3. **Entirely within warm window** (N–M days) → Warm
4. **Entirely in cold storage** (>M days) → Cold (with latency warning)
5. **Spans warm + cold** → CrossTier fan-out
6. **Spans hot + warm** → CrossTier fan-out

### Routing Examples

#### Example 1: Full-text search
```json
{
  "query": "ERROR failed to connect",
  "from": "2026-03-22T00:00:00Z",
  "to": "2026-03-23T23:59:59Z"
}
```
**Result:** `Hot` — Full-text search requires OpenSearch regardless of time range.

---

#### Example 2: Last 24 hours
```json
{
  "query": "SELECT * FROM logs WHERE level='Error' AND timestamp >= NOW() - INTERVAL '24' HOUR LIMIT 100"
}
```
**Result:** `Hot` — Data within hot retention window (default: 1 day).

---

#### Example 3: Last 7 days
```json
{
  "query": "SELECT COUNT(*) FROM logs WHERE timestamp BETWEEN '2026-03-16' AND '2026-03-23'"
}
```
**Result:** `Warm` — Entirely within warm window (1–30 days).

---

#### Example 4: Historical audit (90+ days ago)
```json
{
  "query": "SELECT * FROM logs WHERE timestamp BETWEEN '2025-12-01' AND '2025-12-31'"
}
```
**Result:** `Cold` — Data beyond warm cutoff. Warning: "Query may take 30–120s".

---

#### Example 5: Last 45 days (spans warm + cold)
```json
{
  "query": "SELECT sourceid, COUNT(*) FROM logs WHERE timestamp >= NOW() - INTERVAL '45' DAY GROUP BY sourceid"
}
```
**Result:** `CrossTier` — Queries both warm (last 30 days) and cold (days 31–45) in parallel via `Task.WhenAll`, merges results.

---

## SQL Best Practices

### 1. Always Include Partition Filters

Parquet files are partitioned by `year/month/day/hour`. **Always** include partition filters to avoid full table scans:

```sql
-- ✅ GOOD: Partition filter on year/month/day
SELECT * FROM logs
WHERE year='2026' AND month='03' AND day='23'
  AND level='Error'
LIMIT 100;

-- ❌ BAD: No partition filter → scans all Parquet files
SELECT * FROM logs WHERE level='Error' LIMIT 100;
```

#### Partition Filter Reference

| Time Range | Partition Filter |
|------------|------------------|
| **Today** | `AND year='2026' AND month='03' AND day='23'` |
| **Yesterday** | `AND year='2026' AND month='03' AND day='22'` |
| **This week (Mon-Sun)** | `AND year='2026' AND month='03' AND day >= '17' AND day <= '23'` |
| **This month** | `AND year='2026' AND month='03'` |
| **Last 7 days (crossing month boundary)** | `AND ((year='2026' AND month='03' AND day >= '17') OR (year='2026' AND month='02' AND day >= '24'))` |

---

### 2. Always Include LIMIT Clause

Unbounded queries can return millions of rows and exhaust memory:

```sql
-- ✅ GOOD
SELECT * FROM logs WHERE year='2026' AND month='03' LIMIT 1000;

-- ❌ BAD: No LIMIT → may return 10M+ rows
SELECT * FROM logs WHERE year='2026' AND month='03';
```

---

### 3. Use APPROX_PERCENTILE for Percentile Calculations

For large datasets, approximate percentiles are **100x faster** than exact percentiles:

```sql
-- ✅ GOOD: Fast approximate p99
SELECT APPROX_PERCENTILE(latency_ms, 0.99) AS p99
FROM logs
WHERE year='2026' AND month='03';

-- ❌ SLOW: Exact percentile requires full sort
SELECT PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY latency_ms)
FROM logs WHERE year='2026' AND month='03';
```

---

### 4. Prefer Column Projections Over SELECT *

Parquet is columnar — reading fewer columns is faster and cheaper:

```sql
-- ✅ GOOD: Only reads 3 columns
SELECT timestamp, sourceid, message FROM logs WHERE ...;

-- ❌ WASTEFUL: Reads all 20+ columns
SELECT * FROM logs WHERE ...;
```

---

## Anti-Patterns

| Anti-Pattern | Why It's Bad | Fix |
|--------------|--------------|-----|
| No partition filter | Scans all files in S3 → minutes of latency, high cost | Add `year/month/day` filters |
| No `LIMIT` clause | May return 10M+ rows → OOM crash | Always include `LIMIT` |
| Exact percentiles | Full dataset sort → 100x slower | Use `APPROX_PERCENTILE` |
| `SELECT *` | Reads all columns unnecessarily | List only required columns |
| Unbounded `WHERE` | e.g., `level='Error'` with no time bound → full scan | Add time range filter |

---

## Cost Estimation Flow

For expensive queries (e.g., cold tier, >10M rows), logs2obs returns a **cost estimate** before execution:

### Step 1: Submit Query
```bash
curl -X POST http://localhost:5000/api/v1/query \
  -H "X-Api-Key: ls_key" \
  -d '{"sql":"SELECT COUNT(*) FROM logs WHERE year=2025"}'
```

### Step 2: Receive PendingCostConfirmation Response
```json
{
  "status": "PendingCostConfirmation",
  "estimatedCostUsd": 2.45,
  "estimatedLatencySeconds": 35,
  "estimatedRowsScanned": 15000000,
  "confirmationToken": "conf_abc123"
}
```

### Step 3: Confirm or Cancel
```bash
# Confirm and execute
curl -X POST http://localhost:5000/api/v1/query/confirm \
  -H "X-Api-Key: ls_key" \
  -d '{"token":"conf_abc123"}'

# Or cancel
curl -X DELETE http://localhost:5000/api/v1/query/conf_abc123 \
  -H "X-Api-Key: ls_key"
```

---

## Full-Text Search Syntax

Full-text search is powered by OpenSearch and uses **query DSL passthrough**. Wrap your search string in `SEARCH()`:

```sql
-- Simple term search
SELECT * FROM logs WHERE SEARCH('ERROR database timeout') LIMIT 50;

-- Boolean operators
SELECT * FROM logs WHERE SEARCH('"connection refused" AND (mysql OR postgres)') LIMIT 50;

-- Wildcard search
SELECT * FROM logs WHERE SEARCH('user_id:123*') LIMIT 50;

-- Phrase search
SELECT * FROM logs WHERE SEARCH('"Out of memory" AND level:Fatal') LIMIT 50;
```

Supported OpenSearch query DSL operators: `AND`, `OR`, `NOT`, `"exact phrase"`, `field:value`, `wildcard*`.

---

## Natural Language Query Examples

logs2obs translates natural language to SQL via GitHub Models (or Ollama locally). Examples:

| Natural Language | Generated SQL |
|------------------|---------------|
| "Show me all errors from last hour" | `SELECT * FROM logs WHERE level='Error' AND timestamp >= NOW() - INTERVAL '1' HOUR LIMIT 100` |
| "Count errors by service today" | `SELECT sourceid, COUNT(*) FROM logs WHERE level='Error' AND year='2026' AND month='03' AND day='23' GROUP BY sourceid` |
| "P99 latency for checkout service this week" | `SELECT APPROX_PERCENTILE(latency_ms, 0.99) FROM logs WHERE sourceid='checkout' AND timestamp >= DATE_TRUNC('week', NOW())` |
| "Top 10 slowest API calls yesterday" | `SELECT endpoint, MAX(latency_ms) FROM logs WHERE logtype='Metric' AND day='22' GROUP BY endpoint ORDER BY 2 DESC LIMIT 10` |
| "Error rate trend last 7 days" | `SELECT DATE_TRUNC('day', timestamp) AS day, COUNT(*) FROM logs WHERE level='Error' AND timestamp >= NOW() - INTERVAL '7' DAY GROUP BY day ORDER BY day` |
| "Find logs with trace ID abc123" | `SELECT * FROM logs WHERE traceid='abc123' LIMIT 50` |
| "Show me database connection failures" | `SELECT * FROM logs WHERE SEARCH('"connection refused" AND database') LIMIT 100` |
| "How many logs per tenant today?" | `SELECT tenantid, COUNT(*) FROM logs WHERE year='2026' AND month='03' AND day='23' GROUP BY tenantid` |
| "Average response time per endpoint this month" | `SELECT endpoint, AVG(latency_ms) FROM logs WHERE logtype='Metric' AND month='03' GROUP BY endpoint` |
| "Show me all Fatal logs from last 30 days" | `SELECT * FROM logs WHERE level='Fatal' AND timestamp >= NOW() - INTERVAL '30' DAY LIMIT 200` |

---

## DuckDB vs Athena Differences

| Feature | DuckDB (Local Dev) | Athena (Production) |
|---------|-------------------|---------------------|
| **Execution** | In-process embedded DB | AWS serverless query service |
| **Latency** | <1s | 2–30s (includes queue time) |
| **Cost** | Free | $5 per TB scanned |
| **Concurrency** | Single query at a time | Unlimited parallel queries |
| **S3 Access** | Via MinIO local object store | Direct S3 access |
| **SQL Dialect** | PostgreSQL-like | Presto SQL |
| **Date Functions** | `NOW()`, `DATE_TRUNC` | Same, but `CURRENT_TIMESTAMP` preferred |

### Local Dev Quirks

1. **Connection limit:** DuckDB locks the database file — only one query engine instance at a time.
2. **No partitioning enforcement:** Partition filters are not enforced — queries without filters still work (but are slow).
3. **Memory limits:** Default 1 GB memory limit — large aggregations may fail. Increase via `SET memory_limit='4GB';`.

---

## Query API Reference

### Execute SQL Query
```bash
POST /api/v1/query
{
  "sql": "SELECT * FROM logs WHERE year='2026' LIMIT 100",
  "tenantId": "tenant-abc"
}
```

### Execute Natural Language Query
```bash
POST /api/v1/query/natural
{
  "prompt": "Show me all errors from the checkout service today",
  "tenantId": "tenant-abc"
}
```

### Full-Text Search
```bash
POST /api/v1/search
{
  "query": "connection timeout",
  "from": "2026-03-23T00:00:00Z",
  "to": "2026-03-23T23:59:59Z",
  "limit": 50
}
```

---

## Summary

- **Use partition filters** (`year/month/day`) on every query
- **Always include `LIMIT`** to prevent unbounded result sets
- **Hot tier** for full-text search and real-time queries (<24h)
- **Warm tier** for analytical queries (1–30 days)
- **Cold tier** for historical analysis (>30 days)
- **Confirm cost estimates** for expensive cold tier queries
- Use **natural language** for ad-hoc exploration; use **SQL** for dashboards and automation
