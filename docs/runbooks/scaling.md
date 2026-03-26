# Runbook: Scaling logs2obs

## Overview

This runbook provides operational guidelines for scaling logs2obs components (Worker pods, OpenSearch shards, rate limits) based on production load patterns.

---

## 1. When to Scale Worker Pods

### Triggers

Scale up Worker pods when **any** of these conditions persist for **>5 minutes**:

| Metric | Threshold | Action |
|--------|-----------|--------|
| `logs2obs_queue_depth` | > 10,000 messages | Scale up workers |
| `logs2obs_processing_latency_p99` | > 30 seconds | Scale up workers |
| `logs2obs_worker_cpu_usage` | > 80% | Scale up workers |
| `logs2obs_worker_memory_usage` | > 85% | Scale up workers |

Scale down when queue depth < 1,000 for >15 minutes **and** CPU < 40%.

---

### Worker Scaling Formula

Calculate required worker pods using this formula:

```
pods = ceil(target_throughput / (ConsumerCount × BatchSize × RecvRate))
```

**Variables:**
- `target_throughput`: Desired logs processed per minute
- `ConsumerCount`: Number of parallel consumers per worker (default: 4)
- `BatchSize`: Messages per batch (default: 100)
- `RecvRate`: Batches received per minute per consumer (default: 5)

---

### Example Calculation

**Scenario:** You need to process **50,000 logs/minute** during peak traffic.

**Given:**
- `ConsumerCount` = 4
- `BatchSize` = 100
- `RecvRate` = 5 batches/min/consumer

**Calculation:**
```
Throughput per pod = ConsumerCount × BatchSize × RecvRate
                   = 4 × 100 × 5
                   = 2,000 logs/min/pod

Required pods = ceil(50,000 / 2,000)
              = ceil(25)
              = 25 pods
```

**Recommendation:** Deploy **25 Worker pods** to handle 50k logs/min with headroom.

---

### Tuning Parameters

If scaling linearly becomes expensive, adjust these parameters:

| Parameter | Current | Increase To | Effect |
|-----------|---------|-------------|--------|
| `ConsumerCount` | 4 | 8 | Doubles throughput per pod (⚠️ increases CPU) |
| `BatchSize` | 100 | 200 | Reduces I/O overhead, increases latency |
| `RecvRate` | 5 | 10 | Faster batch retrieval (⚠️ requires faster backend) |

**Example:** With `ConsumerCount=8, BatchSize=200, RecvRate=10`:
```
Throughput per pod = 8 × 200 × 10 = 16,000 logs/min/pod
Required pods = ceil(50,000 / 16,000) = 4 pods
```

**Trade-off:** Fewer pods (lower cost) but higher per-pod resource usage and blast radius.

---

## 2. Horizontal Pod Autoscaler (HPA) Configuration

### ECS Service Auto-Scaling Policy (AWS)

Use target tracking scaling based on SQS queue depth:

```json
{
  "PolicyName": "logs2obs-worker-scaling",
  "ServiceNamespace": "ecs",
  "ResourceId": "service/logs2obs-cluster/logs2obs-worker",
  "ScalableDimension": "ecs:service:DesiredCount",
  "PolicyType": "TargetTrackingScaling",
  "TargetTrackingScalingPolicyConfiguration": {
    "TargetValue": 100.0,
    "CustomizedMetricSpecification": {
      "MetricName": "ApproximateNumberOfMessagesVisible",
      "Namespace": "AWS/SQS",
      "Dimensions": [
        {
          "Name": "QueueName",
          "Value": "logs2obs-storage-writer"
        }
      ],
      "Statistic": "Average"
    },
    "ScaleInCooldown": 300,
    "ScaleOutCooldown": 60
  },
  "MinCapacity": 2,
  "MaxCapacity": 50
}
```

**Explanation:**
- **Target:** Keep average queue depth at ~100 messages per worker
- **Scale out:** Add pods within 60 seconds when queue depth spikes
- **Scale in:** Remove pods after 5 minutes of sustained low load
- **Limits:** Min 2 pods (high availability), max 50 pods (cost guard)

---

### Kubernetes HPA (for non-AWS deployments)

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: logs2obs-worker
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: logs2obs-worker
  minReplicas: 2
  maxReplicas: 50
  metrics:
  - type: External
    external:
      metric:
        name: rabbitmq_queue_messages
        selector:
          matchLabels:
            queue: storage-writer
      target:
        type: AverageValue
        averageValue: "100"
  behavior:
    scaleUp:
      stabilizationWindowSeconds: 60
      policies:
      - type: Percent
        value: 100
        periodSeconds: 60
    scaleDown:
      stabilizationWindowSeconds: 300
      policies:
      - type: Pods
        value: 1
        periodSeconds: 120
```

---

## 3. OpenSearch Shard Scaling

### When to Add Shards

**Trigger:** Primary shard size exceeds **30 GB** (recommended threshold for search performance).

**Check Shard Sizes:**
```bash
curl -X GET "http://localhost:9200/_cat/shards/logs2obs-*?v&h=index,shard,prirep,store&s=store:desc"
```

**Example Output:**
```
index                      shard prirep store
logs2obs-2026.03.24        0     p      42gb   ← Exceeds 30 GB, needs reindex
logs2obs-2026.03.23        0     p      28gb
logs2obs-2026.03.22        0     p      25gb
```

---

### Reindex with More Shards

**Step 1: Create new index with more shards**

```bash
curl -X PUT "http://localhost:9200/logs2obs-2026.03.24-v2" \
  -H "Content-Type: application/json" \
  -d '{
    "settings": {
      "number_of_shards": 5,
      "number_of_replicas": 1
    },
    "mappings": {
      "properties": {
        "tenantId": { "type": "keyword" },
        "timestamp": { "type": "date" },
        "level": { "type": "keyword" },
        "message": { "type": "text" }
      }
    }
  }'
```

**Step 2: Reindex data**

```bash
curl -X POST "http://localhost:9200/_reindex" \
  -H "Content-Type: application/json" \
  -d '{
    "source": {
      "index": "logs2obs-2026.03.24"
    },
    "dest": {
      "index": "logs2obs-2026.03.24-v2"
    }
  }'
```

**Step 3: Monitor reindex progress**

```bash
curl -X GET "http://localhost:9200/_tasks?detailed=true&actions=*reindex"
```

**Step 4: Swap alias**

```bash
curl -X POST "http://localhost:9200/_aliases" \
  -H "Content-Type: application/json" \
  -d '{
    "actions": [
      { "remove": { "index": "logs2obs-2026.03.24", "alias": "logs2obs-current" } },
      { "add": { "index": "logs2obs-2026.03.24-v2", "alias": "logs2obs-current" } }
    ]
  }'
```

**Step 5: Delete old index**

```bash
curl -X DELETE "http://localhost:9200/logs2obs-2026.03.24"
```

---

### Shard Count Guidelines

| Daily Log Volume | Recommended Shards | Reasoning |
|------------------|-------------------|-----------|
| < 10 GB | 1 | Single shard sufficient |
| 10–50 GB | 3 | Balanced distribution |
| 50–150 GB | 5 | Parallel query performance |
| 150–500 GB | 10 | High-throughput workloads |
| > 500 GB | 15+ | Contact support for tuning |

**Rule of Thumb:** Each primary shard should be **20–30 GB** for optimal performance.

---

## 4. Increasing Tenant Rate Limits Without Restart

### Current Limit Check

```bash
curl -X GET "http://localhost:8080/api/v1/tenants/tenant_01HZ.../rate-limit" \
  -H "Authorization: Bearer YOUR_JWT"
```

Response:
```json
{
  "tenantId": "tenant_01HZ...",
  "requestsPerMinute": 600,
  "burstCapacity": 50,
  "currentUsage": 234
}
```

---

### Update Limit in DynamoDB

```bash
aws dynamodb update-item \
  --table-name logs2obs-tenants \
  --key '{"tenantId": {"S": "tenant_01HZ..."}}' \
  --update-expression "SET rateLimitConfig.requestsPerMinute = :rpm, rateLimitConfig.burstCapacity = :burst" \
  --expression-attribute-values '{
    ":rpm": {"N": "1200"},
    ":burst": {"N": "100"}
  }' \
  --return-values ALL_NEW
```

**Effect:** Changes propagate within **60 seconds** (rate limit cache TTL).

---

### Verify Updated Limit

```bash
# Wait 60 seconds for cache refresh
sleep 60

curl -X GET "http://localhost:8080/api/v1/tenants/tenant_01HZ.../rate-limit" \
  -H "Authorization: Bearer YOUR_JWT"
```

Response:
```json
{
  "tenantId": "tenant_01HZ...",
  "requestsPerMinute": 1200,
  "burstCapacity": 100,
  "currentUsage": 234
}
```

---

## 5. Key Metrics to Monitor in Grafana

### Grafana Dashboard Panels

#### Panel 1: Queue Depth
```
logs2obs_queue_depth{queue="storage-writer"}
```
**Alert:** > 10,000 messages for >5 minutes

---

#### Panel 2: Worker Processing Latency (P99)
```
histogram_quantile(0.99, logs2obs_processing_latency_seconds_bucket)
```
**Alert:** > 30 seconds for >5 minutes

---

#### Panel 3: Duplicate Rate
```
rate(logs2obs_duplicate_skip_total[5m]) / rate(logs2obs_ingestion_total[5m])
```
**Normal:** < 1% (duplicates from retries)  
**Alert:** > 10% (possible client bug or replay without idempotency key rotation)

---

#### Panel 4: OpenSearch Indexing Rate
```
rate(opensearch_indexing_total[5m])
```
**Expected:** Should match ingestion rate within 10%  
**Alert:** 50% drop indicates OpenSearch bottleneck

---

#### Panel 5: Athena Query Cost (USD)
```
sum(logs2obs_query_cost_usd) by (tenantId)
```
**Budget Guard:** Alert if daily cost > $100 per tenant (adjust threshold per contract)

---

#### Panel 6: Worker CPU Usage
```
avg(container_cpu_usage_seconds_total{container="logs2obs-worker"}) by (pod)
```
**Alert:** > 80% sustained for >5 minutes

---

#### Panel 7: Worker Memory Usage
```
container_memory_usage_bytes{container="logs2obs-worker"} / container_spec_memory_limit_bytes
```
**Alert:** > 85% (risk of OOM kill)

---

### CloudWatch Alarms (AWS)

**Alarm 1: High Queue Depth**
```bash
aws cloudwatch put-metric-alarm \
  --alarm-name logs2obs-queue-depth-high \
  --metric-name ApproximateNumberOfMessagesVisible \
  --namespace AWS/SQS \
  --statistic Average \
  --period 300 \
  --evaluation-periods 2 \
  --threshold 10000 \
  --comparison-operator GreaterThanThreshold \
  --dimensions Name=QueueName,Value=logs2obs-storage-writer \
  --alarm-actions arn:aws:sns:us-east-1:123456789012:ops-alerts
```

**Alarm 2: Worker P99 Latency**
```bash
aws cloudwatch put-metric-alarm \
  --alarm-name logs2obs-worker-latency-p99 \
  --metric-name ProcessingLatencyP99 \
  --namespace Logs2Obs/Worker \
  --statistic Maximum \
  --period 300 \
  --evaluation-periods 2 \
  --threshold 30000 \
  --comparison-operator GreaterThanThreshold \
  --alarm-actions arn:aws:sns:us-east-1:123456789012:ops-alerts
```

**Alarm 3: DLQ Not Empty**
```bash
aws cloudwatch put-metric-alarm \
  --alarm-name logs2obs-dlq-not-empty \
  --metric-name ApproximateNumberOfMessagesVisible \
  --namespace AWS/SQS \
  --statistic Average \
  --period 60 \
  --evaluation-periods 1 \
  --threshold 1 \
  --comparison-operator GreaterThanOrEqualToThreshold \
  --dimensions Name=QueueName,Value=logs2obs-storage-writer-dlq \
  --alarm-actions arn:aws:sns:us-east-1:123456789012:ops-alerts
```

---

## 6. Proactive Scaling Recommendations

### Scenario: Upcoming Traffic Spike (Black Friday, Product Launch)

**1. Pre-scale workers 30 minutes before event:**
```bash
aws ecs update-service \
  --cluster logs2obs-cluster \
  --service logs2obs-worker \
  --desired-count 50
```

**2. Increase rate limits for critical tenants:**
```bash
aws dynamodb batch-write-item --request-items file://rate-limit-increases.json
```

**3. Monitor dashboard every 5 minutes during event**

**4. Post-event: scale down gradually over 2 hours** (not immediately, to handle delayed log delivery)

---

### Scenario: New Tenant Onboarding (Large Enterprise)

**Expected Load:** 100,000 logs/min from new tenant

**Capacity Planning:**
- **Workers:** `ceil(100,000 / 2,000) = 50 pods` (add 20% buffer → **60 pods**)
- **OpenSearch:** Provision **10 shards** per daily index
- **S3:** Enable S3 Transfer Acceleration for faster uploads
- **Rate Limit:** Set to **2,000 requests/min** (2× expected average)

---

## 7. Cost Optimization Tips

### Reduce Worker Costs

1. **Use Spot Instances (AWS)** for Worker pods (they can tolerate interruptions)
2. **Scale to zero overnight** if ingestion is predictable (e.g., business hours only)
3. **Batch larger** (`BatchSize=500` instead of 100) to reduce per-message overhead

---

### Reduce OpenSearch Costs

1. **Enable ILM (Index Lifecycle Management):**
   - Hot tier: 7 days
   - Warm tier: 30 days (cheaper instance types)
   - Cold tier: 90 days (S3-backed, ultra-low cost)
   - Delete: 365 days
2. **Use UltraWarm nodes** for infrequently accessed indices

---

### Reduce Athena Query Costs

1. **Always include partition filters** (year/month/day) to limit data scanned
2. **Use `LIMIT` clause** on exploratory queries
3. **Create materialized views** for frequently run dashboards (see [Materialized Views Guide](../materialized-views.md))
4. **Pre-aggregate common queries** (e.g., error rates, P99 latency) and store in DynamoDB

---

## Next Steps

- See [Incident Response Runbook](./incident-response.md) for troubleshooting high load issues
- See [Security Guide](../security.md) for rate limiting configuration
- See [Grafana Dashboard](../../infra/grafana/dashboards/logs2obs.json) for pre-built panels
