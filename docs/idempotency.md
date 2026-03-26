# Idempotency

This guide explains how logs2obs ensures **exactly-once delivery** for log entries to prevent duplicate data in dashboards and alerts.

---

## Why Exactly-Once Delivery Matters

In distributed observability systems, **duplicate log entries** cause serious problems:

- **False alert counts:** An error alert fires 3 times instead of once
- **Inflated dashboards:** Error rates show 300% instead of 100%
- **Wasted storage:** Same log entry stored multiple times in Parquet and OpenSearch
- **Incorrect billing:** Duplicate ingestion charges

### Common Causes of Duplicates

1. **Client retries:** Network timeout → client resends the same log batch
2. **Message queue retries:** Worker crashes → SQS/RabbitMQ redelivers messages
3. **Worker restarts:** Pod restart mid-batch → logs re-processed on startup
4. **Distributed tracing:** Same span logged by multiple services

logs2obs uses **UUIDv7-based idempotency** to guarantee exactly-once processing.

---

## UUIDv7 as Idempotency Key

Every log entry has a unique `Id` field (type: UUIDv7). This ID serves as the **idempotency key** for deduplication.

### UUIDv7 Structure

```
01JGXYZ1234567890ABCDEFGH
│       │
│       └─ Random suffix (80 bits)
└─────────────────────── Timestamp prefix (48 bits, millisecond precision)
```

**Key properties:**
- **Time-sortable:** IDs generated later have lexicographically higher values
- **Monotonic ordering:** Within the same millisecond, IDs are sequential
- **Globally unique:** 80-bit random suffix ensures no collisions across distributed systems
- **Timestamp extraction:** First 48 bits encode Unix timestamp (ms) — can extract creation time without database lookup

### Benefits Over UUIDv4

| Feature | UUIDv4 (Random) | UUIDv7 (Time-ordered) |
|---------|-----------------|------------------------|
| **Sortability** | ❌ Random order | ✅ Time-ordered |
| **Index efficiency** | ❌ Poor B-tree performance | ✅ Excellent B-tree locality |
| **Time extraction** | ❌ No timestamp | ✅ Embedded timestamp |
| **Collision resistance** | ✅ 122 bits random | ✅ 80 bits random (sufficient) |

---

## How the Redis Idempotency Store Works

logs2obs uses **Redis** as the idempotency store with atomic check-and-set semantics.

### Key Format

```
idem:{tenantId}:{entryId}
```

**Example:**
```
idem:tenant-abc:01JGXYZ1234567890ABCDEFGH
```

### TTL Calculation

Idempotency keys are stored for:

```
TTL = HotRetentionDays + 1 day
```

**Rationale:**
- Hot tier (OpenSearch) stores logs for `HotRetentionDays` (default: 1 day)
- After data ages out of hot tier, duplicates are no longer a concern (warm/cold tiers have dedup at write time)
- +1 day buffer ensures overlap during tier transitions

**Example:** If `HotRetentionDays = 1`, idempotency keys are stored for **2 days**.

### Atomic Check-and-Set with Redis

```csharp
// IIdempotencyStore implementation (RedisIdempotencyStore)
public async Task<bool> CheckAndSetAsync(string key, TimeSpan ttl, CancellationToken ct)
{
    var redisKey = $"idem:{_tenantId}:{key}";
    
    // SET NX (set if not exists) — atomic operation
    var wasSet = await _redis.StringSetAsync(redisKey, "1", ttl, when: When.NotExists);
    
    return wasSet; // true = first time seen; false = duplicate
}
```

**Flow:**
1. Worker receives log entry with `Id = 01JGXYZ...`
2. Worker calls `CheckAndSetAsync("01JGXYZ...", ttl: 2 days)`
3. Redis executes `SET NX` atomically:
   - If key doesn't exist → set it, return `true` → **process the log**
   - If key exists → return `false` → **skip the log as duplicate**

---

## Deduplication at Each Layer

logs2obs applies deduplication at **three layers** to ensure robustness:

### Layer 1: Redis (First-Seen Gate)

**When:** Worker receives log entry from message queue

**How:** Check `idem:{tenantId}:{entryId}` in Redis via `SET NX`

**Result:**
- ✅ First time seen → Set Redis key, proceed to Layers 2 & 3
- ❌ Duplicate → Increment `idem_skipped_total` counter, return early

**Code location:** `Logs2Obs.Worker/StorageWriterWorker.cs` and `SearchIndexerWorker.cs`

---

### Layer 2: OpenSearch (Upsert via `_id`)

**When:** Indexing log entry into OpenSearch

**How:** Set OpenSearch document `_id` field to the log entry's `Id` (UUIDv7)

**Result:**
- First write → Document created
- Duplicate write → Document **updated** (upsert semantics) — idempotent

**Code:**
```csharp
await _searchIndexer.IndexAsync(new LogDocument
{
    Id       = entry.Id, // UUIDv7 — becomes OpenSearch _id
    Message  = entry.Message,
    // ...
}, ct);
```

**Note:** OpenSearch dedup is a **fallback** — duplicates should be caught at Layer 1 (Redis). Layer 2 prevents corruption if Redis check is bypassed (e.g., Redis outage + retry).

---

### Layer 3: Parquet Batch Deduplication

**When:** Flushing in-memory batch to Parquet file

**How:** Before writing, dedup entries in the batch by `Id` using a `HashSet<string>`

**Result:** Within a single batch, if the same `Id` appears twice (e.g., due to queue redelivery during flush window), only the first occurrence is written.

**Code:**
```csharp
var uniqueEntries = batch
    .GroupBy(e => e.Id)
    .Select(g => g.First()) // Keep first occurrence only
    .ToList();

await _parquetWriter.WriteAsync(uniqueEntries, ct);
```

**Note:** This protects against duplicates **within a batch**. Duplicates **across batches** are caught by Redis (Layer 1).

---

## What Happens on Duplicate Entry

When a duplicate log entry is detected (Layer 1: Redis):

1. Worker **skips processing** — entry is not written to Parquet or OpenSearch
2. Telemetry counter `idem_skipped_total` is **incremented** (tagged by `tenant_id`)
3. Debug log emitted: `"Duplicate entry skipped: {entryId}"`

### Monitoring Duplicate Rate

```promql
# Duplicate entry rate (entries/second)
rate(logs2obs_idem_skipped_total[5m])

# Duplicate percentage (%)
100 * rate(logs2obs_idem_skipped_total[5m]) / rate(logs2obs_ingest_entries[5m])
```

**Expected duplicate rate:** <0.1% in healthy systems. Higher rates indicate:
- Client retry storms (check client timeout config)
- Message queue redelivery issues (check visibility timeout)
- Worker crash loops (check pod restart frequency)

---

## Client-Side Best Practices

### 1. Always Generate `Id` Before Sending

**❌ BAD:** Let the server auto-generate the ID

```json
POST /api/v1/ingest
{
  "entries": [
    {
      "sourceId": "api-gateway",
      "message": "User login"
    }
  ]
}
```

**Problem:** If the request times out and the client retries, the server generates a **different ID** on each attempt → duplicates are not detected.

---

**✅ GOOD:** Client generates UUIDv7 before sending

```json
POST /api/v1/ingest
{
  "entries": [
    {
      "id": "01JGXYZ1234567890ABCDEFGH",
      "sourceId": "api-gateway",
      "message": "User login"
    }
  ]
}
```

**Benefit:** On retry, the client sends the **same ID** → idempotency check catches duplicate → log is processed exactly once.

---

### 2. Use the Same `Id` on Retry

```javascript
// ✅ GOOD: Generate ID once, reuse on retry
const logEntry = {
  id: generateUuidV7(), // Generate once
  sourceId: "checkout",
  message: "Payment processed"
};

let success = false;
let retries = 0;

while (!success && retries < 3) {
  try {
    await sendToLogs2Obs(logEntry); // Same ID on each retry
    success = true;
  } catch (err) {
    retries++;
    await sleep(1000 * retries); // Exponential backoff
  }
}
```

---

### 3. If Client Lacks UUIDv7 Library, Server Auto-Generates (Lossy Idempotency)

If the client **cannot** generate UUIDv7 (e.g., legacy system), the server will auto-generate the ID:

```json
POST /api/v1/ingest
{
  "entries": [
    {
      "sourceId": "legacy-app",
      "message": "System startup"
    }
  ]
}
```

**Server response:**
```json
{
  "accepted": 1,
  "rejected": 0,
  "batchId": "01JGYABC...",
  "generatedIds": ["01JGXYZ1234567890ABCDEFGH"]
}
```

**Trade-off:**
- ✅ Still works — logs are ingested
- ❌ Retry idempotency is lost — client retries will create duplicates
- ⚠️ Use only for non-critical logs or fire-and-forget scenarios

---

### 4. Libraries Supporting UUIDv7

| Language | Library | Function |
|----------|---------|----------|
| **C# / .NET** | Built-in (.NET 9+) | `Guid.CreateVersion7()` |
| **Python** | `uuid-utils` | `uuid_utils.uuid7()` |
| **JavaScript** | `uuid` (v9+) | `uuidv7()` |
| **Go** | `github.com/google/uuid` | `uuid.NewV7()` |
| **Java** | `java.util.UUID` (JDK 22+) | `UUID.v7()` |
| **Rust** | `uuid` crate | `Uuid::now_v7()` |

---

## Idempotency in Distributed Tracing

When multiple services log the same trace, they should use **different log entry IDs** but share the same `traceId`:

```json
// Service A: API Gateway
{
  "id": "01JGXYZ1111111111AAAAAAA",
  "traceId": "trace-abc123",
  "sourceId": "api-gateway",
  "message": "Request received"
}

// Service B: Checkout Service
{
  "id": "01JGXYZ2222222222BBBBBBB",
  "traceId": "trace-abc123",
  "sourceId": "checkout",
  "message": "Payment processed"
}
```

**Key points:**
- Each service generates its **own unique `id`** (different UUIDv7)
- All logs share the same **`traceId`** for correlation
- Idempotency works per log entry (by `id`), not per trace

---

## Troubleshooting

### High Duplicate Rate (>1%)

**Possible causes:**
1. **Client retry storms:** Check client timeout settings (should be >5s)
2. **Message queue visibility timeout too short:** Increase SQS visibility timeout to >30s
3. **Worker crash loops:** Check pod restart logs and memory limits
4. **Redis outage:** Check Redis connection health

**Fix:** Investigate telemetry counters:
```promql
# Group duplicates by tenant
sum by (tenant_id) (rate(logs2obs_idem_skipped_total[5m]))
```

---

### Duplicate Logs Still Appearing in Dashboards

**Possible causes:**
1. **Client not sending `id` field:** Check ingestion payloads — ensure `id` is present
2. **Redis dedup bypassed:** Check if `UseIdempotency` config is disabled
3. **OpenSearch `_id` not set:** Check indexer code — ensure `_id = entry.Id`

**Fix:**
- Enable debug logging to trace idempotency checks
- Query OpenSearch for duplicate `_id` values:
  ```json
  GET /logs-tenant-abc-*/_search
  {
    "aggs": {
      "duplicates": {
        "terms": {
          "field": "_id",
          "min_doc_count": 2
        }
      }
    }
  }
  ```

---

### Redis Key Expiration Too Short

**Symptom:** Logs ingested >2 days ago are re-ingested and flagged as duplicates.

**Cause:** TTL calculation error or incorrect `HotRetentionDays` config.

**Fix:**
- Verify TTL: `TTL IDEM:tenant-abc:01JGXYZ...` (should be HotRetentionDays + 1 day in seconds)
- Check config: `appsettings.json` → `"HotRetentionDays": 1`

---

## API Reference

### Check Idempotency Status

```bash
GET /api/v1/idempotency/{tenantId}/{entryId}
```

**Response:**
```json
{
  "entryId": "01JGXYZ1234567890ABCDEFGH",
  "firstSeenAt": "2026-03-23T10:15:30Z",
  "ttlSeconds": 172800,
  "expiresAt": "2026-03-25T10:15:30Z"
}
```

---

### Force Clear Idempotency Key (Admin Only)

```bash
DELETE /api/v1/idempotency/{tenantId}/{entryId}
```

**Use case:** Testing, or recovering from accidental key corruption.

---

## Summary

- **UUIDv7** provides time-ordered, globally unique IDs with embedded timestamps
- **Redis idempotency store** uses `SET NX` for atomic check-and-set with TTL = HotRetentionDays + 1
- **Three-layer deduplication:** Redis (first-seen gate), OpenSearch (upsert by `_id`), Parquet (batch dedup)
- **Always generate `Id` on the client** before sending — reuse the same ID on retry
- **Monitor duplicate rate** via `idem_skipped_total` telemetry counter (should be <0.1%)
- **Auto-generated IDs** work but lose idempotency on retry — use only for non-critical logs
