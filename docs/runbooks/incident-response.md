# Runbook: Incident Response

## Overview

This runbook provides step-by-step procedures for responding to common production incidents in logs2obs. Follow these procedures during on-call escalations.

---

## 1. Dead Letter Queue (DLQ) Investigation

### Scenario: DLQ Has Messages

**Alert:** CloudWatch alarm `logs2obs-dlq-not-empty` triggers when DLQ depth > 0.

---

### Step 1: List DLQ Contents

```bash
aws sqs receive-message \
  --queue-url https://sqs.us-east-1.amazonaws.com/123456789012/logs2obs-storage-writer-dlq \
  --max-number-of-messages 10 \
  --attribute-names All \
  --message-attribute-names All
```

**Output:**
```json
{
  "Messages": [
    {
      "MessageId": "abc123...",
      "ReceiptHandle": "AQEB...",
      "Body": "{\"id\":\"01HZABC...\",\"sourceId\":\"my-service\",\"logType\":\"Error\",\"level\":\"Error\",\"timestamp\":\"2026-03-24T10:00:00Z\",\"message\":\"NullRef in PaymentProcessor\"}",
      "Attributes": {
        "SentTimestamp": "1711270800000",
        "ApproximateReceiveCount": "5",
        "ApproximateFirstReceiveTimestamp": "1711270800000"
      },
      "MessageAttributes": {
        "FailureReason": {
          "StringValue": "OpenSearch indexing failed: ConnectionTimeout",
          "DataType": "String"
        }
      }
    }
  ]
}
```

---

### Step 2: Inspect Failed Message

**Decode the Body:**
```bash
echo '{"id":"01HZABC...","sourceId":"my-service",...}' | jq .
```

**Check Failure Metadata:**
- `ApproximateReceiveCount`: Number of processing attempts (max: 5 before DLQ)
- `MessageAttributes.FailureReason`: Exception message from Worker

---

### Step 3: Identify Failure Cause

| Failure Reason | Root Cause | Remediation |
|----------------|------------|-------------|
| `OpenSearch indexing failed: ConnectionTimeout` | OpenSearch overloaded or network issue | Scale OpenSearch cluster, check security group rules |
| `Parquet write failed: S3 access denied` | IAM permissions issue | Verify Worker ECS task role has `s3:PutObject` permission |
| `Idempotency store unavailable: Redis connection failed` | Redis cluster down | Check ElastiCache cluster status, restart if needed |
| `Schema validation failed: Unknown field 'unknown_field'` | Client sending invalid schema | Contact tenant, update schema registry |
| `Deserialization failed: Invalid JSON` | Corrupted message | Skip message (replay from Parquet if critical) |

---

### Step 4: Fix Root Cause

**Example: OpenSearch Connection Timeout**

1. **Check OpenSearch Cluster Health:**
   ```bash
   curl -X GET "http://localhost:9200/_cluster/health?pretty"
   ```

2. **If Yellow/Red Status:**
   - Increase OpenSearch cluster size
   - Check for unassigned shards: `curl -X GET "http://localhost:9200/_cat/shards?v&h=index,shard,prirep,state,unassigned.reason"`
   - Force allocate unassigned shards if needed

3. **If Green Status:**
   - Check network connectivity from Worker ECS task to OpenSearch
   - Verify security group allows inbound 443 from Worker security group

---

### Step 5: Replay Messages from DLQ

**Option A: Replay Single Message (for investigation)**

```bash
# 1. Receive message (with ReceiptHandle for deletion)
aws sqs receive-message \
  --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer-dlq \
  --max-number-of-messages 1 > dlq-message.json

# 2. Extract body
BODY=$(jq -r '.Messages[0].Body' dlq-message.json)

# 3. Send to main queue
aws sqs send-message \
  --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer \
  --message-body "$BODY"

# 4. Delete from DLQ (only after successful replay)
RECEIPT_HANDLE=$(jq -r '.Messages[0].ReceiptHandle' dlq-message.json)
aws sqs delete-message \
  --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer-dlq \
  --receipt-handle "$RECEIPT_HANDLE"
```

---

**Option B: Bulk Replay All DLQ Messages (after root cause fixed)**

Use the `logs2obs-dlq-replay` Lambda function (if deployed):

```bash
aws lambda invoke \
  --function-name logs2obs-dlq-replay \
  --payload '{"dlqUrl":"https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer-dlq","targetQueueUrl":"https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer","maxMessages":1000}' \
  response.json

cat response.json
```

**Lambda Logic (pseudocode):**
```csharp
while (dlqDepth > 0) {
    var messages = ReceiveBatch(dlqUrl, maxMessages: 10);
    foreach (var msg in messages) {
        SendMessage(targetQueueUrl, msg.Body);
        DeleteMessage(dlqUrl, msg.ReceiptHandle);
    }
}
```

---

### Step 6: Purge DLQ (Use with EXTREME Caution)

**⚠️ WARNING:** This **permanently deletes** all messages in the DLQ. Only use if messages are unrecoverable and you have backup (Parquet).

```bash
aws sqs purge-queue \
  --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer-dlq
```

**Confirmation Prompt:**
```
Are you sure you want to purge all messages in logs2obs-storage-writer-dlq? (yes/no): yes
```

---

### Common DLQ Failure Causes & Remediation

| Symptom | Cause | Fix |
|---------|-------|-----|
| DLQ fills up rapidly (>1000 msg/min) | Systemic failure (e.g., OpenSearch down) | Stop ingestion, fix backend, replay |
| DLQ has <10 messages/hour | Transient network issues | Safe to replay after 1 hour |
| Same message ID appears repeatedly | Duplicate message without idempotency key | Update client to include `id` field |
| All messages from one tenant | Tenant-specific schema issue | Contact tenant, update schema |

---

## 2. OpenSearch Recovery

### Scenario: Index Corrupted or Accidentally Deleted

**Symptoms:**
- Query returns 404: `index_not_found_exception`
- Dashboard shows missing data for specific date range
- `curl -X GET "http://localhost:9200/_cat/indices?v"` shows missing index

---

### Step 1: Confirm Data Loss

```bash
curl -X GET "http://localhost:9200/logs2obs-2026.03.24/_count"
```

**Expected:** `{"count": 1234567}`  
**Actual:** `{"error":{"type":"index_not_found_exception"}}`

---

### Step 2: Trigger Replay from Parquet

Use the Replay API to re-index from S3 Parquet files:

```bash
curl -X POST http://localhost:8080/api/v1/replay \
  -H "X-Api-Key: ls_your_key" \
  -H "Content-Type: application/json" \
  -d '{
    "from": "2026-03-24T00:00:00Z",
    "to": "2026-03-24T23:59:59Z",
    "options": {
      "reindexSearch": true,
      "reprocessAlerts": false,
      "reparseFiles": false
    }
  }'
```

**Response:**
```json
{
  "replayJobId": "replay_01HZ...",
  "status": "queued",
  "estimatedDurationMinutes": 15,
  "estimatedCostUsd": 2.34
}
```

---

### Step 3: Monitor Replay Progress

```bash
curl -X GET http://localhost:8080/api/v1/replay/replay_01HZ... \
  -H "X-Api-Key: ls_your_key"
```

**Response:**
```json
{
  "replayJobId": "replay_01HZ...",
  "status": "in_progress",
  "progress": {
    "totalFiles": 120,
    "processedFiles": 45,
    "totalLogs": 1234567,
    "reindexedLogs": 456789,
    "percentComplete": 37.0,
    "estimatedTimeRemainingMinutes": 9
  }
}
```

---

### Step 4: Verify Re-Indexing Completion

```bash
curl -X GET "http://localhost:9200/logs2obs-2026.03.24/_count"
```

**Expected:** `{"count": 1234567}` (matches original count)

---

### Step 5: Check Index Health

```bash
curl -X GET "http://localhost:9200/_cluster/health/logs2obs-2026.03.24?pretty"
```

**Expected:**
```json
{
  "cluster_name": "logs2obs",
  "status": "green",
  "number_of_nodes": 3,
  "number_of_data_nodes": 3,
  "active_primary_shards": 5,
  "active_shards": 10,
  "relocating_shards": 0,
  "initializing_shards": 0,
  "unassigned_shards": 0
}
```

---

### Replay Cost Considerations

| Date Range | Estimated Cost | Duration |
|------------|---------------|----------|
| 1 day | $2–5 | 15 min |
| 1 week | $10–30 | 90 min |
| 1 month | $50–150 | 6 hours |

**Cost Breakdown:**
- S3 GET requests: $0.0004 per 1,000 requests
- OpenSearch indexing: compute time (proportional to log volume)
- Worker CPU time: ECS Fargate task hours

---

## 3. Worker Crash Recovery

### At-Least-Once Delivery Guarantees

logs2obs uses **SQS visibility timeout** to ensure no data loss on Worker crashes:

1. Worker receives message from SQS (visibility timeout: 5 minutes)
2. Worker processes log entry
3. Worker ACKs message (deletes from queue)
4. **If Worker crashes before ACK:** Message becomes visible again after 5 minutes

**Result:** Message is reprocessed by another Worker (or same Worker after restart).

---

### Idempotency Prevents Duplicates

Even if a message is reprocessed, idempotency ensures no duplicates:

1. Worker checks Redis: `EXISTS {tenantId}:idempotency:{logId}`
2. **If exists:** Skip processing (increment `logs2obs_duplicate_skip_total` metric)
3. **If not exists:** Process log, write to Parquet/OpenSearch, set Redis key

**Redis Key TTL:** `HotRetentionDays + 1` (default: 8 days)

---

### Steps to Verify Data Integrity After Crash

**1. Check for Stuck Messages (Visibility Timeout Expired)**

```bash
aws sqs get-queue-attributes \
  --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer \
  --attribute-names ApproximateNumberOfMessagesNotVisible
```

**Expected:** 0 (no in-flight messages stuck)  
**If >0:** Wait 5 minutes for visibility timeout to expire, then check again.

---

**2. Check Duplicate Rate Spike**

```bash
curl -X GET "http://localhost:9090/api/v1/query?query=rate(logs2obs_duplicate_skip_total[5m])" | jq .
```

**Normal:** < 1% duplicate rate  
**After crash:** 5–10% spike for 5–10 minutes (reprocessed messages)  
**If >20%:** Investigate possible idempotency key collision or Redis failure

---

**3. Compare Ingestion vs Indexed Counts**

```bash
# Total ingested (from Prometheus)
curl "http://localhost:9090/api/v1/query?query=sum(logs2obs_ingestion_total)"

# Total indexed (from OpenSearch)
curl -X GET "http://localhost:9200/logs2obs-*/_count"
```

**Expected:** Counts match within 1% (accounting for async indexing delay)

---

## 4. High Error Rate Triage

### Decision Tree

```
┌─────────────────────────────────────────────────────────────┐
│  High Error Rate Alert (>5% of ingestion failing)           │
└────────────────────┬────────────────────────────────────────┘
                     │
        ┌────────────▼────────────┐
        │ Check CloudWatch Logs   │
        │ for exception details   │
        └────────────┬────────────┘
                     │
        ┌────────────▼────────────┐
        │ Exception Type?         │
        └────────────┬────────────┘
                     │
        ┌────────────┼────────────┬────────────────────┐
        │            │            │                    │
  ┌─────▼─────┐ ┌───▼───┐ ┌─────▼─────┐ ┌───────────▼────────┐
  │OpenSearch │ │ Redis │ │   Parquet │ │ Schema Validation  │
  │ Timeout   │ │ Down  │ │ Write Err │ │ Failed             │
  └─────┬─────┘ └───┬───┘ └─────┬─────┘ └───────────┬────────┘
        │           │           │                    │
  ┌─────▼─────┐ ┌───▼───┐ ┌─────▼─────┐ ┌───────────▼────────┐
  │ Scale OS  │ │Restart│ │ Check IAM │ │ Update Schema Reg  │
  │ + Reindex │ │ Redis │ │ Policy    │ │ + Retry            │
  └───────────┘ └───────┘ └───────────┘ └────────────────────┘
```

---

### Step 1: Check CloudWatch Logs for Exception Details

```bash
aws logs tail /aws/ecs/logs2obs-worker --follow --since 5m | grep -i "exception"
```

**Common Exceptions:**

| Exception | Cause | Fix |
|-----------|-------|-----|
| `OpenSearchException: ConnectionTimeout` | OpenSearch overloaded | Scale cluster, see [Scaling Runbook](./scaling.md) |
| `RedisConnectionException` | ElastiCache unavailable | Check cluster status, restart if needed |
| `Amazon.S3.AmazonS3Exception: Access Denied` | IAM policy missing | Add `s3:PutObject` to Worker task role |
| `SchemaValidationException: Unknown field` | Client schema mismatch | Update schema registry or reject logs |

---

### Step 2: Check DLQ Depth (Is It Growing?)

```bash
aws sqs get-queue-attributes \
  --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer-dlq \
  --attribute-names ApproximateNumberOfMessages
```

**If depth > 100 and growing:**
- **Systemic failure** (backend service down)
- **Action:** Stop ingestion, fix backend, replay DLQ

**If depth < 50 and stable:**
- **Transient failures** (network blips, retries)
- **Action:** Monitor; auto-retry will handle

---

### Step 3: Check OpenSearch Health

```bash
curl -X GET "http://localhost:9200/_cluster/health?pretty"
```

**Status: Yellow**
- **Cause:** Replica shards not assigned (low node count)
- **Impact:** Queries slower, no data loss
- **Action:** Add data nodes or reduce replica count

**Status: Red**
- **Cause:** Primary shards unassigned (critical)
- **Impact:** Some indices unavailable
- **Action:** Force allocate shards, see [OpenSearch Recovery](#2-opensearch-recovery)

---

### Step 4: Check Redis Connectivity

```bash
redis-cli -h logs2obs-cache.abc123.ng.0001.use1.cache.amazonaws.com PING
```

**Expected:** `PONG`  
**If timeout:** ElastiCache cluster down; restart or failover to replica.

---

## 5. Emergency Procedures

### Enable Maintenance Mode (Return 503 from ALB)

**Use Case:** Critical backend failure; need to stop all ingestion immediately.

**Step 1: Update ALB Target Group Health Check**

```bash
aws elbv2 modify-target-group \
  --target-group-arn arn:aws:elasticloadbalancing:us-east-1:123456789012:targetgroup/logs2obs-api/abc123 \
  --health-check-path /health/maintenance
```

**Step 2: Deploy Maintenance Mode Response**

Update API to return 503 on `/health/maintenance`:

```csharp
app.MapGet("/health/maintenance", () => Results.StatusCode(503));
```

**Result:** ALB marks all targets unhealthy → returns 503 to all clients.

---

### Scale Down Workers to Stop Processing

**Use Case:** Need to halt processing (e.g., data corruption investigation) but keep API accepting logs (queued for later).

```bash
aws ecs update-service \
  --cluster logs2obs-cluster \
  --service logs2obs-worker \
  --desired-count 0
```

**Effect:** Workers stop processing messages; messages accumulate in SQS.

**To Resume:**
```bash
aws ecs update-service \
  --cluster logs2obs-cluster \
  --service logs2obs-worker \
  --desired-count 10
```

---

### Drain Queue Gracefully

**Use Case:** Maintenance window; need to process all pending messages before shutdown.

**Step 1: Stop New Ingestion (API maintenance mode)**

**Step 2: Monitor Queue Depth**

```bash
watch -n 5 'aws sqs get-queue-attributes --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer --attribute-names ApproximateNumberOfMessages'
```

**Step 3: Wait for Depth → 0 (typically 10–30 minutes)**

**Step 4: Scale Down Workers**

```bash
aws ecs update-service --cluster logs2obs-cluster --service logs2obs-worker --desired-count 0
```

---

### Emergency Stop (No Graceful Drain)

**Use Case:** Critical data corruption; must stop immediately.

```bash
# 1. Stop ingestion (ALB maintenance mode)
# 2. Scale workers to 0
aws ecs update-service --cluster logs2obs-cluster --service logs2obs-worker --desired-count 0

# 3. Purge queue (⚠️ loses in-flight data)
aws sqs purge-queue --queue-url https://sqs.us-east-1.amazonaws.com/.../logs2obs-storage-writer
```

---

## 6. Escalation Contacts

| Severity | Contact | SLA |
|----------|---------|-----|
| **P0** (Service down) | DevOps Lead + Slack #logs2obs-incidents | 15 min |
| **P1** (Degraded) | On-call Engineer | 1 hour |
| **P2** (DLQ messages) | On-call Engineer | 4 hours |
| **P3** (Non-urgent) | Backlog + Weekly Review | Next sprint |

---

## 7. Post-Incident Checklist

After resolving an incident:

- [ ] Write post-mortem in `incidents/YYYY-MM-DD.md`
- [ ] Update runbook with new learnings
- [ ] Create Jira tickets for preventative fixes
- [ ] Review CloudWatch alarms (add new alarms if gaps found)
- [ ] Update Grafana dashboard (add new panels if needed)
- [ ] Notify affected tenants (if SLA breached)
- [ ] Schedule blameless post-mortem meeting (within 48 hours)

---

## Next Steps

- See [Scaling Runbook](./scaling.md) for proactive scaling before incidents
- See [Security Guide](../security.md) for security incident procedures
- See [Replay Guide](../replay-guide.md) for data recovery procedures
