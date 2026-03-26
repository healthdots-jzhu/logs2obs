# Replay Guide

This guide explains how to use the **Replay** feature to reprocess historical log data for backfills, re-indexing, and data recovery.

---

## What is Replay?

**Replay** reads Parquet files from S3/MinIO for a specified time range and reprocesses them through the logs2obs pipeline. This allows you to:

- Apply new alert rules to historical data
- Re-index logs after OpenSearch schema changes
- Re-run normalization logic after bug fixes
- Restore data after accidental deletion
- Migrate data to new storage formats

---

## When to Use Replay

### Use Case 1: Backfill New Alert Rules Against Historical Data

**Scenario:** You create a new alert rule on March 23, but need to check if it would have fired in the past 30 days.

**Solution:** Replay the last 30 days with `reprocessAlerts: true`:

```bash
curl -X POST http://localhost:5000/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "from": "2026-02-21T00:00:00Z",
    "to": "2026-03-23T23:59:59Z",
    "options": {
      "reindexSearch": false,
      "reprocessAlerts": true,
      "reparseFiles": false
    }
  }'
```

**Result:** Alert evaluator processes historical logs. If conditions match, alerts are triggered (with `isHistorical: true` flag).

---

### Use Case 2: Re-index After OpenSearch Schema Change

**Scenario:** You added a new field `kubernetes.pod_name` to the OpenSearch index template. Existing logs don't have this field indexed.

**Solution:** Replay logs to re-index with the new schema:

```bash
curl -X POST http://localhost:5000/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "from": "2026-03-01T00:00:00Z",
    "to": "2026-03-23T23:59:59Z",
    "options": {
      "reindexSearch": true,
      "reprocessAlerts": false,
      "reparseFiles": false
    }
  }'
```

**Result:** Parquet files are read, logs are re-indexed into OpenSearch with the new field.

---

### Use Case 3: Re-run Normalization After Bug Fix

**Scenario:** The Worker had a bug that incorrectly parsed `latency_ms` values (e.g., parsed `"142ms"` as string instead of extracting the numeric value `142`). You fixed the bug and need to reprocess logs.

**Solution:** Replay with `reparseFiles: true`:

```bash
curl -X POST http://localhost:5000/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "from": "2026-03-20T00:00:00Z",
    "to": "2026-03-23T23:59:59Z",
    "options": {
      "reindexSearch": true,
      "reprocessAlerts": false,
      "reparseFiles": true
    }
  }'
```

**Result:** Parquet files are re-parsed with the fixed logic, then re-indexed into OpenSearch and re-written to Parquet (with corrected values).

---

### Use Case 4: Restore Data After Accidental Index Deletion

**Scenario:** You accidentally deleted the OpenSearch index `logs-tenant-abc-2026-03-22`.

**Solution:** Replay that day's logs from Parquet:

```bash
curl -X POST http://localhost:5000/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "from": "2026-03-22T00:00:00Z",
    "to": "2026-03-22T23:59:59Z",
    "options": {
      "reindexSearch": true,
      "reprocessAlerts": false,
      "reparseFiles": false
    }
  }'
```

**Result:** Parquet files for March 22 are read and re-indexed into OpenSearch, fully restoring the lost data.

---

### Use Case 5: Apply Schema Migration to Re-generate Parquet Files

**Scenario:** You changed the schema (e.g., added a new field `feature_flag_enabled`) and want to regenerate Parquet files with the new schema for a specific time range.

**Solution:** Replay with `reparseFiles: true` and `reindexSearch: true`:

```bash
curl -X POST http://localhost:5000/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "from": "2026-03-15T00:00:00Z",
    "to": "2026-03-20T23:59:59Z",
    "options": {
      "reindexSearch": true,
      "reprocessAlerts": false,
      "reparseFiles": true
    }
  }'
```

**Result:** Old Parquet files are re-read, parsed with the new schema, re-written to Parquet, and re-indexed into OpenSearch.

---

## How Replay Works

### Step-by-Step Flow

```
┌──────────────────────────────────────────────────────────────────┐
│ 1. User starts replay via API                                   │
│    POST /api/v1/replay                                          │
│    → ReplayService creates ReplayJob, publishes ReplayStartedEvent│
└──────────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────────┐
│ 2. Puller scans S3/MinIO for Parquet files in time range       │
│    → S3PathBuilder.BuildLogPath(tenantId, date, batchId)        │
│    → List all matching files: tenant-abc/2026/03/22/*/*.parquet │
└──────────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────────┐
│ 3. Puller reads each Parquet file                               │
│    → ParquetReader.ReadAsync(stream)                             │
│    → Deserialize LogEntry records                                │
└──────────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────────┐
│ 4. Puller publishes logs to message queue                       │
│    → If reindexSearch=true: publish to ls-search-indexer        │
│    → If reprocessAlerts=true: publish to ls-alert-evaluator     │
│    → If reparseFiles=true: publish to ls-storage-writer         │
└──────────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────────┐
│ 5. Worker processes messages                                    │
│    → StorageWriterWorker: Re-writes to Parquet (if reparseFiles)│
│    → SearchIndexerWorker: Re-indexes to OpenSearch (if reindex) │
│    → AlertEvaluatorWorker: Evaluates alerts (if reprocessAlerts)│
└──────────────────────────────────────────────────────────────────┘
                            ↓
┌──────────────────────────────────────────────────────────────────┐
│ 6. ReplayService monitors progress                              │
│    → Polls processedCount, errorCount from metadata store       │
│    → Updates ReplayJob status (Queued → Running → Completed)    │
│    → Publishes ReplayCompletedEvent when done                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## Starting a Replay via API

### API Endpoint

```
POST /api/v1/replay
```

### Request Body

```json
{
  "tenantId": "tenant-abc",
  "from": "2026-03-01T00:00:00Z",
  "to": "2026-03-10T23:59:59Z",
  "options": {
    "reindexSearch": true,
    "reprocessAlerts": false,
    "reparseFiles": false,
    "overrideParser": null,
    "maxParallelFiles": 4
  }
}
```

### Response

```json
{
  "jobId": "01JGYZABC1234567890DEFGHIJ",
  "tenantId": "tenant-abc",
  "from": "2026-03-01T00:00:00Z",
  "to": "2026-03-10T23:59:59Z",
  "status": "Queued",
  "createdAt": "2026-03-23T14:30:00Z",
  "estimatedFiles": 2400,
  "estimatedEntries": 12000000
}
```

### Full cURL Example

```bash
curl -X POST http://localhost:5000/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "tenant-abc",
    "from": "2026-03-01T00:00:00Z",
    "to": "2026-03-10T23:59:59Z",
    "options": {
      "reindexSearch": true,
      "reprocessAlerts": false,
      "reparseFiles": false
    }
  }'
```

---

## ReplayOptions Reference

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `reindexSearch` | `bool` | `true` | Re-index logs into OpenSearch. Use for schema changes or index recovery. |
| `reprocessAlerts` | `bool` | `false` | Re-evaluate alert rules against historical logs. Use for backfilling alerts. |
| `reparseFiles` | `bool` | `false` | Re-run log parsers and re-write Parquet files. Use after parser bug fixes or schema migrations. |
| `overrideParser` | `string?` | `null` | Use a specific parser version (e.g., `"syslog-v2"`). If null, uses current parser. |
| `maxParallelFiles` | `int` | `4` | Number of Parquet files to process in parallel. Increase for faster replay (higher memory usage). |

### Option Combinations

| Scenario | `reindexSearch` | `reprocessAlerts` | `reparseFiles` |
|----------|----------------|-------------------|----------------|
| **Re-index after schema change** | ✅ | ❌ | ❌ |
| **Backfill alert rules** | ❌ | ✅ | ❌ |
| **Fix parser bug + re-index** | ✅ | ❌ | ✅ |
| **Restore deleted index** | ✅ | ❌ | ❌ |
| **Re-generate Parquet files** | ❌ | ❌ | ✅ |
| **Full reprocess (all steps)** | ✅ | ✅ | ✅ |

---

## Monitoring Replay Progress

### Check Replay Job Status

```bash
GET /api/v1/replay/{jobId}
```

**Response:**
```json
{
  "jobId": "01JGYZABC1234567890DEFGHIJ",
  "tenantId": "tenant-abc",
  "from": "2026-03-01T00:00:00Z",
  "to": "2026-03-10T23:59:59Z",
  "status": "Running",
  "createdAt": "2026-03-23T14:30:00Z",
  "startedAt": "2026-03-23T14:30:15Z",
  "completedAt": null,
  "processedCount": 3200000,
  "errorCount": 12,
  "estimatedTotalEntries": 12000000,
  "progressPercent": 26.7,
  "estimatedCompletionAt": "2026-03-23T16:45:00Z",
  "throughputEntriesPerSecond": 8500
}
```

### Job Status Values

| Status | Description |
|--------|-------------|
| `Queued` | Replay job created, waiting to start |
| `Running` | Puller is reading Parquet files and publishing to queues |
| `Completed` | All files processed successfully |
| `Failed` | Job failed (check `errorMessage` field) |
| `Cancelled` | Job was cancelled by user |

### Monitoring Telemetry

```promql
# Replay progress (entries/second)
rate(logs2obs_replay_entries_processed_total{job_id="01JGYZABC..."}[5m])

# Replay error rate
rate(logs2obs_replay_errors_total{job_id="01JGYZABC..."}[5m])

# Estimated completion time (minutes remaining)
(logs2obs_replay_total_entries{job_id="01JGYZABC..."} - logs2obs_replay_processed_entries{job_id="01JGYZABC..."}) 
/ rate(logs2obs_replay_entries_processed_total{job_id="01JGYZABC..."}[5m]) / 60
```

---

## Cost Considerations

### Parquet Reads from S3

- **GET requests:** $0.0004 per 1,000 requests
- **Data transfer:** Free (within same region)
- **Example:** Reading 10,000 Parquet files = $0.004 (negligible)

### OpenSearch Re-indexing

- **Compute cost:** Proportional to document count
- **Example:** Re-indexing 100M logs = ~2 hours on r6g.2xlarge instance = ~$0.50
- **Index storage:** Same as original data (no additional cost)

### DuckDB Processing (Local Dev)

- **Cost:** Free (in-memory or local disk)
- **Limitation:** Single-threaded Parquet reads (slower than Athena)

### Athena Processing (Production)

- **Query cost:** $5 per TB scanned
- **Example:** Replaying 100 GB of Parquet files = $0.50

**Optimization tips:**
1. Use partition filters to minimize Parquet files scanned
2. Set `maxParallelFiles` appropriately (higher = faster but more memory)
3. Run replay during off-peak hours to reduce impact on live workloads

---

## How to Cancel a Running Replay

```bash
DELETE /api/v1/replay/{jobId}
```

**Response:**
```json
{
  "jobId": "01JGYZABC1234567890DEFGHIJ",
  "status": "Cancelled",
  "cancelledAt": "2026-03-23T15:00:00Z",
  "processedCount": 5000000,
  "errorCount": 20
}
```

**Notes:**
- Cancel is **best-effort** — messages already published to queues will still be processed
- Partial data (entries processed before cancellation) **remains in OpenSearch/Parquet**
- To roll back, you must manually delete the affected data (e.g., delete OpenSearch index and re-restore from backup)

---

## Troubleshooting

### Replay Job Stuck in "Queued" Status

**Possible causes:**
1. Puller service not running
2. No Parquet files found for the time range
3. S3/MinIO access denied

**Fix:**
- Check Puller logs: `kubectl logs -l app=logs2obs-puller`
- Verify S3 path: List files for the tenant and date range via AWS CLI or MinIO client
- Check IAM permissions: Puller needs `s3:GetObject` on the bucket

---

### High Error Rate During Replay

**Possible causes:**
1. Corrupted Parquet files
2. Schema incompatibility (old files use deprecated fields)
3. OpenSearch index capacity exhausted

**Fix:**
- Check error logs: `GET /api/v1/replay/{jobId}/errors`
- For schema errors: Add `reparseFiles: true` to re-normalize data
- For OpenSearch capacity: Scale up OpenSearch cluster or reduce `maxParallelFiles`

---

### Replay Slow (Low Throughput)

**Possible causes:**
1. `maxParallelFiles` too low (default: 4)
2. Worker consumer count too low
3. OpenSearch indexing bottleneck

**Fix:**
- Increase `maxParallelFiles` to 8–16 (if memory allows)
- Scale Worker pods: `kubectl scale deployment logs2obs-worker --replicas=10`
- Scale OpenSearch: Add data nodes or increase instance size

---

## API Reference

### Start Replay
```
POST /api/v1/replay
```

### Get Replay Status
```
GET /api/v1/replay/{jobId}
```

### List All Replay Jobs (Admin)
```
GET /api/v1/replay?tenantId={tenantId}&status={status}
```

### Cancel Replay
```
DELETE /api/v1/replay/{jobId}
```

### Get Replay Errors
```
GET /api/v1/replay/{jobId}/errors?page=1&pageSize=50
```

---

## Summary

- **Replay** reprocesses Parquet files from S3/MinIO for a specified time range
- **5 use cases:** Backfill alerts, re-index after schema change, fix parser bugs, restore deleted data, schema migration
- **ReplayOptions:** `reindexSearch`, `reprocessAlerts`, `reparseFiles` control what happens during replay
- **Cost:** Parquet reads are cheap ($0.0004/1000 files); OpenSearch re-indexing has compute cost
- **Monitor progress** via `GET /api/v1/replay/{jobId}` or telemetry counters
- **Cancel anytime** via `DELETE /api/v1/replay/{jobId}` (best-effort cancellation)
