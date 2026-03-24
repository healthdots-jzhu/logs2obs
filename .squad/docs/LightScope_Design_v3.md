# LightScope: Lightweight Observability & Log Intelligence Service
### Design v3.0 — .NET 10 | Cloud-Agnostic | AI-Powered | Production-Grade

> **Changelog from v2.0:**
> - Upgraded to **.NET 10** throughout
> - Added **Schema Evolution Strategy** with `ISchemaRegistry`
> - Added **Exactly-Once / Idempotency** strategy
> - Added **API-Layer Backpressure** with token-bucket rate limiting
> - Added **Query Cost Guardrails** with pre-execution estimation
> - Added **OpenSearch ILM / Indexing Strategy**
> - Added **AI Safety Layer** (`ISqlSafetyValidator`, prompt logging)
> - Strengthened **Multi-Tenancy Isolation** (per-tenant workgroups, index strategy)
> - Added **Self-Observability** (OpenTelemetry, health endpoints, internal metrics)
> - Added **Replay / Reprocessing Service**
> - Added **Materialized Views / Aggregations**
> - Added **Per-Tenant Data Retention Policies**
> - Added **Stream Processing option** (Kafka/Redpanda)
> - Added comprehensive **Coding Agent Rules** section (MediatR, Polly, Serilog, DTO/Domain separation)

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [What is gRPC and When to Use It](#2-what-is-grpc-and-when-to-use-it)
3. [Messaging: SNS Fanout & SQS Queue Design](#3-messaging-sns-fanout--sqs-queue-design)
4. [Cloud-Agnostic Design: Adapter & Plugin Patterns](#4-cloud-agnostic-design-adapter--plugin-patterns)
5. [Local Development with Docker (No Cloud Required)](#5-local-development-with-docker-no-cloud-required)
6. [Core Design Principles](#6-core-design-principles)
7. [Data Model & Schema Design](#7-data-model--schema-design)
8. [Schema Evolution Strategy](#8-schema-evolution-strategy)
9. [Exactly-Once & Idempotency Strategy](#9-exactly-once--idempotency-strategy)
10. [API Design](#10-api-design)
11. [API-Layer Backpressure & Rate Limiting](#11-api-layer-backpressure--rate-limiting)
12. [Log Ingestion Pipeline with Async/Multi-Thread Detail](#12-log-ingestion-pipeline-with-asyncmulti-thread-detail)
13. [External Log Pulling (S3 & Others)](#13-external-log-pulling-s3--others)
14. [Storage Strategy & OpenSearch ILM](#14-storage-strategy--opensearch-ilm)
15. [Query Tier Routing: Hot vs Warm vs Cold](#15-query-tier-routing-hot-vs-warm-vs-cold)
16. [Query Engine, Cost Guardrails & Replay](#16-query-engine-cost-guardrails--replay)
17. [Graph Support & Visualization Engine](#17-graph-support--visualization-engine)
18. [AI-Powered Queries: GitHub Models API + Safety Layer](#18-ai-powered-queries-github-models-api--safety-layer)
19. [Authentication: API Key + JWT/Cognito Dual Strategy](#19-authentication-api-key--jwtcognito-dual-strategy)
20. [Multi-Tenancy: Strong Isolation Model](#20-multi-tenancy-strong-isolation-model)
21. [Self-Observability: OpenTelemetry & Health Endpoints](#21-self-observability-opentelemetry--health-endpoints)
22. [Materialized Views & Aggregations](#22-materialized-views--aggregations)
23. [Stream Processing Option (Kafka/Redpanda)](#23-stream-processing-option-kafkaredpanda)
24. [Security](#24-security)
25. [Project Structure](#25-project-structure)
26. [Configuration & Deployment](#26-configuration--deployment)
27. [Coding Agent Rules & Conventions](#27-coding-agent-rules--conventions)
28. [Implementation Phases](#28-implementation-phases)
29. [Full Markdown Documentation (Phase 14)](#29-full-markdown-documentation-phase-14)

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              PRODUCERS / SOURCES                                │
│  App Logs │ Error Logs │ Network/OS │ Metrics │ S3 │ Cloud Logs │ Kafka │ HTTP  │
└──────┬────┴──────┬──────┴──────┬─────┴────┬───┴────┴──────┬─────┴───────┴──────┘
       │           │             │          │               │
       ▼           ▼             ▼          ▼               │
┌──────────────────────────────────────────────────────┐    │
│           LightScope.Api  (.NET 10)                   │◄───┘
│   ASP.NET Core 10 — Minimal APIs + gRPC endpoints     │
│                                                       │
│  ┌───────────────────────┐  ┌────────────────────┐    │
│  │  Token-Bucket Rate    │  │  Auth Middleware    │    │
│  │  Limiter (per-tenant) │  │  (ApiKey │ JWT)     │    │
│  └──────────┬────────────┘  └────────────────────┘    │
│  ┌──────────▼──────────────────────────────────────┐  │
│  │  MediatR Command Pipeline                        │  │
│  │  IngestLogsCommand → IngestLogsHandler           │  │
│  └──────────────────────┬───────────────────────────┘  │
└─────────────────────────┼──────────────────────────────┘
                          │  IMessageBus.PublishAsync
                          ▼
┌──────────────────────────────────────────────────────────────┐
│            IMessageBus (Cloud-Agnostic Abstraction)           │
│  AWS: SNS → SQS fanout   │  Azure: Service Bus Topics        │
│  GCP: Pub/Sub            │  Local: RabbitMQ exchanges         │
│  Stream: Kafka/Redpanda  │                                    │
│                                                              │
│  [2 Topics] → [8 Queues + 4 DLQs] (see Section 3)           │
└───────────────────────────┬──────────────────────────────────┘
                            │ Per-consumer queues
          ┌─────────────────┼────────────────┬─────────────────┐
          ▼                 ▼                ▼                 ▼
  [storage-writer]  [search-indexer] [alert-evaluator] [pull-job-events]
          │                 │                │
          ▼                 ▼                ▼
┌─────────────────────────────────────────────────────────────┐
│         LightScope.Worker  (.NET 10 Worker Service)          │
│  IIdempotencyStore check → normalize → validate → route      │
│  Bounded Channel<LogEntry> pipelines (backpressure)          │
│  Parallel Parquet batching │ Bulk OpenSearch indexing         │
│  Polly retry policies on all external calls                  │
└─────────────────────┬───────────────────────────────────────┘
                      │
        ┌─────────────┼──────────────┐
        ▼             ▼              ▼
┌──────────────┐ ┌──────────┐ ┌──────────────────┐
│ IObjectStore │ │ISearchIdx│ │  IMetadataStore   │
│ S3/Blob/GCS/ │ │OpenSearch│ │  DynamoDB/Cosmos/ │
│ MinIO        │ │Elastic/  │ │  Firestore/PgSQL  │
│              │ │Meili     │ │                   │
│  ILM Policy  │ │ILM Policy│ │ ISchemaRegistry   │
└──────┬───────┘ └────┬─────┘ │ IIdempotencyStore │
       │              │       └──────────────────┘
       ▼              │
┌──────────────────┐  │
│  IQueryEngine    │◄─┘
│  Athena/Synapse/ │
│  BigQuery/DuckDB │
│  + Cost Guard    │
│  + Replay Svc    │
└────────┬─────────┘
         │
         ▼
┌──────────────────────────────────────────────────────────┐
│       LightScope.QueryEngine  (.NET 10)                   │
│  QueryTierRouter → Hot │ Warm │ Cold │ Cross-Tier          │
│  SQL Safety Validator (ISqlSafetyValidator)               │
│  Cost Estimator → user confirmation for large scans       │
│  AI: NL→SQL (GitHub Models / Ollama) + safety layer       │
│  Graph Engine: Vega-Lite + Chart.js specs                 │
│  Materialized Views refresh engine                        │
│  Alert evaluation + IReplayService                        │
└──────────────────────────────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│  Self-Observability: OpenTelemetry → Prometheus / OTEL    │
│  /health/ready │ /health/live │ /metrics                  │
│  Internal: ingestion_rate, queue_lag, processing_latency  │
└──────────────────────────────────────────────────────────┘
```

---

## 2. What is gRPC and When to Use It

### What is gRPC?

**gRPC** (Google Remote Procedure Call) is a high-performance, open-source RPC framework using **HTTP/2** transport and **Protocol Buffers (Protobuf)** for binary serialization. Compared to REST (JSON over HTTP/1.1):

- **Bidirectional streaming** — client and server stream simultaneously
- **Binary serialization** — Protobuf payloads are 3–10× smaller and faster to parse than JSON
- **Strongly typed contracts** — `.proto` files generate client/server code in any language
- **Multiplexed connections** — many concurrent requests share one HTTP/2 connection

### gRPC vs REST

| Aspect | REST (JSON/HTTP 1.1) | gRPC (Protobuf/HTTP 2) |
|--------|---------------------|----------------------|
| Payload | JSON (~verbose) | Protobuf (binary, compact) |
| Speed | Moderate | 3–10× faster serialization |
| Streaming | SSE / WebSockets (bolted on) | Native: unary, server, client, bidirectional |
| Contract | OpenAPI (optional) | `.proto` (required, code-generated) |
| Browser support | Native | Requires gRPC-Web proxy |
| Best for | Public APIs, browser clients | Internal services, high-throughput agents |

### gRPC Use Cases in LightScope

| Use Case | Why gRPC |
|----------|---------|
| **High-throughput log agents** | 50k+ entries/sec; Protobuf saves 60–70% payload vs JSON |
| **Real-time metric streaming** | Client-streaming RPC pushes continuous samples on one connection |
| **Internal service calls** | Worker ↔ QueryEngine typed RPC with generated clients |
| **APM SDKs** | Generated gRPC client ships with .NET/Java/Go SDKs |

### gRPC Streaming Modes (all implemented in LightScope)

```protobuf
// protos/log_ingestion.proto
syntax = "proto3";
package lightscope.v1;

service LogIngestion {
  // Unary: one batch request → one response
  rpc SendBatch(BatchRequest) returns (SendResponse);

  // Client streaming: agent streams entries → server acks when done
  rpc StreamLogs(stream LogEntryProto) returns (SendResponse);

  // Bidirectional: agent streams, server streams back acks + backpressure signals
  rpc StreamWithAck(stream LogEntryProto) returns (stream AckResponse);
}

message LogEntryProto {
  string id              = 1;
  string source_id       = 2;
  string log_type        = 3;
  string level           = 4;
  string environment     = 5;
  string category        = 6;
  int64  timestamp_unix_ms = 7;
  string message         = 8;
  optional string trace_id    = 9;
  optional string stack_trace = 10;
  map<string, string> tags    = 11;
  optional MetricProto metric = 12;
  uint32 schema_version  = 13;  // ← Added for schema evolution
}

message AckResponse {
  string batch_id  = 1;
  int32  accepted  = 2;
  int32  rejected  = 3;
  bool   throttled = 4;  // ← Backpressure signal
  int32  retry_after_ms = 5;
}
```

**Client streaming .NET 10 example (66% smaller payload vs JSON):**

```csharp
// Agent SDK: 10,000 entries/sec over one persistent HTTP/2 connection
using var call = client.StreamWithAck(cancellationToken: ct);

// Reader: process server acks + respect backpressure signals
var readerTask = Task.Run(async () =>
{
    await foreach (var ack in call.ResponseStream.ReadAllAsync(ct))
    {
        if (ack.Throttled)
        {
            _logger.LogWarning("Server throttling — backing off {Ms}ms", ack.RetryAfterMs);
            await Task.Delay(ack.RetryAfterMs, ct);
        }
    }
});

// Writer: stream log entries
await foreach (var entry in _localBuffer.ReadAllAsync(ct))
{
    await call.RequestStream.WriteAsync(ToProto(entry), ct);
    // Protobuf ~120 bytes vs JSON ~350 bytes — 66% reduction
}

await call.RequestStream.CompleteAsync();
await readerTask;
```

---

## 3. Messaging: SNS Fanout & SQS Queue Design

### 3.1 The Fanout Pattern

When a log entry arrives at the API, multiple independent consumers need it:
1. **StorageWriter** — writes Parquet to object store
2. **SearchIndexer** — indexes to OpenSearch for real-time search
3. **AlertEvaluator** — evaluates active alert rules in real time
4. **MaterializedViewRefresher** — updates pre-aggregated views

The API publishes once to **SNS**; SNS fans out copies to independent **SQS queues** — no direct coupling between publisher and consumers.

```
  API publishes once ──►  SNS: lightscope-ingest  (Standard, ~unlimited throughput)
                                    │
              ┌─────────────────────┼────────────────────┬───────────────────────┐
              ▼                     ▼                    ▼                       ▼
    SQS: storage-writer   SQS: search-indexer   SQS: alert-evaluator  SQS: matview-refresh
    (+ DLQ)               (+ DLQ)               (+ DLQ)               (+ DLQ)
         │                     │                     │                      │
    Worker: Parquet       Worker: OpenSearch     QueryEngine:          QueryEngine:
    batch write           bulk index             alert rules           aggregation update
```

### 3.2 Complete Queue Topology

#### SNS Topics — 2 Total

| Topic | Type | Purpose |
|-------|------|---------|
| `lightscope-ingest` | Standard | All incoming log/metric entries — the main fanout hub |
| `lightscope-system-events` | Standard | Internal events: job complete, crawler trigger, alert fired, replay started |

**Why Standard (not FIFO)?**
FIFO SNS/SQS caps at 3,000 msg/sec with batching — insufficient for a logging platform targeting millions/min. Logs carry their own `timestamp`; delivery order is irrelevant. Standard queues support effectively unlimited throughput.

#### SQS Queues — 8 Main + 4 DLQs = 12 Total

| Queue | Type | SNS Subscription | Consumer | DLQ | Max Receive Count |
|-------|------|-----------------|---------|-----|-------------------|
| `ls-storage-writer` | Standard | `lightscope-ingest` | Worker | `ls-storage-writer-dlq` | 3 |
| `ls-search-indexer` | Standard | `lightscope-ingest` | Worker | `ls-search-indexer-dlq` | 3 |
| `ls-alert-evaluator` | Standard | `lightscope-ingest` | QueryEngine | `ls-alert-evaluator-dlq` | 3 |
| `ls-matview-refresh` | Standard | `lightscope-ingest` | QueryEngine | `ls-matview-refresh-dlq` | 3 |
| `ls-pull-job-events` | Standard | `lightscope-system-events` | Puller | `ls-pull-job-events-dlq` | 3 |
| `ls-replay-events` | Standard | `lightscope-system-events` | Worker | `ls-replay-events-dlq` | 3 |
| `ls-report-scheduler` | Standard | `lightscope-system-events` | QueryEngine | `ls-report-scheduler-dlq` | 3 |
| `ls-idempotency-expire` | Standard | `lightscope-system-events` | Worker | `ls-idempotency-expire-dlq` | 3 |

**Total: 2 SNS Topics + 8 SQS Queues + 8 DLQs = 18 resources**

#### Queue Configuration

```json
{
  "QueueName": "ls-storage-writer",
  "Attributes": {
    "VisibilityTimeout": "60",
    "MessageRetentionPeriod": "86400",
    "ReceiveMessageWaitTimeSeconds": "20",
    "RedrivePolicy": {
      "deadLetterTargetArn": "arn:aws:sqs:...:ls-storage-writer-dlq",
      "maxReceiveCount": "3"
    }
  }
}
```

**Long polling** (`ReceiveMessageWaitTimeSeconds: 20`) cuts empty-receive costs by up to 95%.

#### SNS Subscription Filter Policies (per subscriber)

```json
// ls-alert-evaluator subscription: only Error/Fatal entries (cost optimization)
{
  "logLevel":  ["Error", "Fatal"],
  "logType":   ["Application", "Network", "OS"]
}

// ls-matview-refresh subscription: Metric type only
{
  "logType":   ["Metric"]
}

// ls-search-indexer subscription: all types (no filter)
// ls-storage-writer subscription: all types (no filter)
```

### 3.3 Cloud-Agnostic Messaging Abstraction

```csharp
// LightScope.Core/Messaging/IMessageBus.cs
public interface IMessageBus
{
    Task PublishAsync<T>(string topic, T message,
        MessageAttributes? attributes = null, CancellationToken ct = default);

    IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(
        string queue, CancellationToken ct = default);

    Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default);
    Task NegativeAcknowledgeAsync(string receiptHandle, CancellationToken ct = default);
    Task<QueueMetrics> GetQueueMetricsAsync(string queue, CancellationToken ct = default);
}

public sealed record QueueMetrics(
    long ApproximateMessageCount,
    long MessagesInFlight,
    long OldestMessageAgeSeconds);

// Implementations registered by provider key:
// "AwsSns"    → AwsSnsMessageBus + AwsSqsSubscriber
// "AzureSB"   → AzureServiceBusMessageBus
// "GcpPubSub" → GcpPubSubMessageBus
// "RabbitMQ"  → RabbitMqMessageBus  (local)
// "Kafka"     → KafkaMessageBus     (stream processing option)
// "InProcess" → InProcessChannelMessageBus (unit tests)
```

---

## 4. Cloud-Agnostic Design: Adapter & Plugin Patterns

### 4.1 Hexagonal Architecture (Ports & Adapters)

The core domain (`LightScope.Core`) has **zero** cloud SDK references. All cloud-specific implementations live in `LightScope.Adapters.*` and are wired at startup via provider key.

```
┌──────────────────────────────────────────────────────────────┐
│                LightScope.Core (Domain)                       │
│  Pure C# interfaces — no AWS/Azure/GCP/cloud SDK references  │
│                                                               │
│  IObjectStore        ISearchIndexer      IMetadataStore       │
│  IQueryEngine        IMessageBus         IPullConnector        │
│  ISecretStore        IScheduler          IGraphDataProvider    │
│  ISchemaRegistry     IIdempotencyStore   IAiService            │
│  ISqlSafetyValidator IReplayService      IMatViewEngine        │
│  ITelemetryExporter                                           │
└────────────────────────┬─────────────────────────────────────┘
                         │ implemented by
       ┌─────────────────┼──────────────────┬──────────────────┐
       ▼                 ▼                  ▼                  ▼
┌──────────────┐ ┌──────────────────┐ ┌──────────────┐ ┌─────────────┐
│ AWS Adapters │ │ Azure Adapters   │ │ GCP Adapters │ │Local Adapter│
│ S3           │ │ Blob Storage     │ │ GCS          │ │ MinIO       │
│ SNS/SQS      │ │ Service Bus      │ │ Pub/Sub      │ │ RabbitMQ    │
│ DynamoDB     │ │ CosmosDB         │ │ Firestore    │ │ PostgreSQL  │
│ Athena+Glue  │ │ Synapse          │ │ BigQuery     │ │ DuckDB      │
│ OpenSearch   │ │ Elastic on Azure │ │ Vertex AI    │ │ Meilisearch │
│ Cognito      │ │ Azure AD         │ │ Firebase Auth│ │ LocalJwt    │
│ SecrMgr      │ │ Key Vault        │ │ Secret Mgr   │ │ .env file   │
│ EventBridge  │ │ Timer Triggers   │ │ Scheduler    │ │ Quartz.NET  │
└──────────────┘ └──────────────────┘ └──────────────┘ └─────────────┘
```

### 4.2 All Core Interfaces

```csharp
// Storage
public interface IObjectStore
{
    Task WriteAsync(string key, Stream data, string contentType, CancellationToken ct = default);
    Task<Stream> ReadAsync(string key, CancellationToken ct = default);
    IAsyncEnumerable<ObjectInfo> ListAsync(string prefix, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task<ObjectMetadata> GetMetadataAsync(string key, CancellationToken ct = default);
}

// Search / Hot Tier
public interface ISearchIndexer
{
    Task IndexAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct = default);
    Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken ct = default);
    Task<AggregationResult> AggregateAsync(AggregationRequest request, CancellationToken ct = default);
    Task EnsureIndexAsync(string indexName, IndexSettings settings, CancellationToken ct = default);
    Task DeleteOldIndicesAsync(string pattern, int retentionDays, CancellationToken ct = default);
}

// Query Engine / Warm+Cold Tier
public interface IQueryEngine
{
    Task<QueryCostEstimate> EstimateCostAsync(string sql, CancellationToken ct = default);
    Task<QuerySubmitResult> SubmitAsync(string tenantId, string sql, CancellationToken ct = default);
    Task<QueryStatus> GetStatusAsync(string queryId, CancellationToken ct = default);
    Task<QueryResults> GetResultsAsync(string queryId, int pageSize, string? nextToken, CancellationToken ct = default);
    Task CancelAsync(string queryId, CancellationToken ct = default);
}

// Metadata / Config Store
public interface IMetadataStore
{
    Task<T?> GetAsync<T>(string table, string pk, string? sk = null, CancellationToken ct = default);
    Task PutAsync<T>(string table, T item, CancellationToken ct = default);
    Task DeleteAsync(string table, string pk, string? sk = null, CancellationToken ct = default);
    IAsyncEnumerable<T> QueryAsync<T>(string table, string pk, CancellationToken ct = default);
    Task<T?> UpdateAsync<T>(string table, string pk, string? sk, Func<T?, T> updater, CancellationToken ct = default);
}

// Schema Registry (NEW)
public interface ISchemaRegistry
{
    Task<SchemaVersion> GetCurrentAsync(string tenantId, CancellationToken ct = default);
    Task<SchemaVersion> RegisterAsync(string tenantId, SchemaDefinition schema, CancellationToken ct = default);
    Task<SchemaCompatibilityResult> CheckCompatibilityAsync(string tenantId, LogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<SchemaVersion>> GetHistoryAsync(string tenantId, CancellationToken ct = default);
}

// Idempotency Store (NEW)
public interface IIdempotencyStore
{
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
    Task MarkProcessedAsync(string id, TimeSpan ttl, CancellationToken ct = default);
    Task<long> PruneExpiredAsync(CancellationToken ct = default);
}

// Replay Service (NEW)
public interface IReplayService
{
    Task<ReplayJob> StartAsync(string tenantId, DateTimeOffset from, DateTimeOffset to,
        ReplayOptions options, CancellationToken ct = default);
    Task<ReplayJobStatus> GetStatusAsync(string replayJobId, CancellationToken ct = default);
    Task CancelAsync(string replayJobId, CancellationToken ct = default);
}

// Materialized View Engine (NEW)
public interface IMatViewEngine
{
    Task RefreshAsync(string tenantId, string viewName, CancellationToken ct = default);
    Task<MatViewResult> QueryAsync(string tenantId, string viewName, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task RegisterViewAsync(string tenantId, MatViewDefinition definition, CancellationToken ct = default);
}

// AI Service
public interface IAiService
{
    Task<NlQueryResult> TranslateToSqlAsync(string naturalLanguage, QueryContext ctx, CancellationToken ct = default);
    Task<IReadOnlyList<GraphSuggestion>> SuggestGraphsAsync(QueryResultSchema schema, string? intent, CancellationToken ct = default);
    Task<string> ExplainErrorAsync(string stackTrace, string? message, CancellationToken ct = default);
    Task<IReadOnlyList<AlertRuleSuggestion>> SuggestAlertsAsync(string serviceDescription, CancellationToken ct = default);
}

// SQL Safety Validator (NEW)
public interface ISqlSafetyValidator
{
    void Validate(string sql);                          // Throws SqlSafetyException on violation
    SqlSafetyReport Analyze(string sql);                // Returns warnings without throwing
}

// Secret Store
public interface ISecretStore
{
    Task<string> GetSecretAsync(string secretName, CancellationToken ct = default);
    Task SetSecretAsync(string secretName, string value, CancellationToken ct = default);
}

// Scheduler
public interface IScheduler
{
    Task ScheduleAsync(string jobId, string cronExpression,
        Func<CancellationToken, Task> handler, CancellationToken ct = default);
    Task UnscheduleAsync(string jobId, CancellationToken ct = default);
    Task TriggerNowAsync(string jobId, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledJob>> ListAsync(CancellationToken ct = default);
}
```

### 4.3 Provider Equivalence Map

| Capability | AWS | Azure | GCP | Local (Docker) |
|-----------|-----|-------|-----|---------------|
| Object Store | S3 | Blob Storage | GCS | MinIO |
| Message Bus | SNS + SQS | Service Bus | Pub/Sub | RabbitMQ |
| Stream Bus (opt.) | Kinesis / MSK | Event Hubs | Dataflow | Kafka / Redpanda |
| Search / Hot | OpenSearch | Elastic on Azure | Elastic on GCP | Meilisearch |
| Metadata | DynamoDB | CosmosDB | Firestore | PostgreSQL |
| Query Engine | Athena | Synapse | BigQuery | DuckDB |
| Idempotency | ElastiCache Redis | Azure Cache | Memorystore | Redis (Docker) |
| Schema Registry | Glue Schema Reg. | Azure Schema Reg. | Apicurio | PostgreSQL table |
| Secret Store | Secrets Manager | Key Vault | Secret Manager | `.env` / Vault |
| Auth | Cognito + API Key | Azure AD + API Key | Firebase + API Key | LocalJwt + API Key |
| Scheduler | EventBridge | Timer Triggers | Cloud Scheduler | Quartz.NET |
| Container | ECS/EKS | AKS | GKE | docker-compose |
| Telemetry | CloudWatch + OTEL | Azure Monitor + OTEL | Cloud Monitoring + OTEL | Prometheus + Grafana |

### 4.4 Provider Registration

```csharp
// LightScope.Api/Program.cs — .NET 10 Minimal API style
var provider = builder.Configuration["LightScope:Provider"]; // "AWS" | "Azure" | "GCP" | "Local"

builder.Services
    .AddLightScopeStorage(provider)
    .AddLightScopeSearch(provider)
    .AddLightScopeMessaging(provider)
    .AddLightScopeQuery(provider)
    .AddLightScopeMetadata(provider)
    .AddLightScopeIdempotency(provider)
    .AddLightScopeSchemaRegistry(provider)
    .AddLightScopeAi(provider)
    .AddLightScopeObservability(provider);
```

---

## 5. Local Development with Docker (No Cloud Required)

### 5.1 Local Simulators

| Cloud Service | Local Simulator | Notes |
|--------------|----------------|-------|
| S3 | MinIO | S3-compatible API |
| SNS + SQS | RabbitMQ with exchanges | Simpler than LocalStack |
| OpenSearch | OpenSearch official | Same binary as cloud |
| DynamoDB / CosmosDB | PostgreSQL | `IMetadataStore` adapter |
| Athena / BigQuery | DuckDB (in-process) | Reads MinIO Parquet natively |
| Redis (idempotency) | Redis (Docker) | Identical to cloud |
| Cognito | LocalJwtAuthHandler | Self-signed JWTs for dev |
| GitHub Models API | Ollama | OpenAI-compatible endpoint |
| Prometheus | Prometheus + Grafana | Self-observability |

### 5.2 docker-compose.yml

```yaml
version: "3.9"

services:
  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    ports: ["9000:9000", "9001:9001"]
    environment:
      MINIO_ROOT_USER: lightscope
      MINIO_ROOT_PASSWORD: lightscope123
    volumes: ["minio_data:/data"]
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 10s

  rabbitmq:
    image: rabbitmq:3-management
    ports: ["5672:5672", "15672:15672"]
    environment:
      RABBITMQ_DEFAULT_USER: lightscope
      RABBITMQ_DEFAULT_PASS: lightscope123
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s

  opensearch:
    image: opensearchproject/opensearch:2.11.0
    ports: ["9200:9200"]
    environment:
      discovery.type: single-node
      DISABLE_SECURITY_PLUGIN: "true"
      OPENSEARCH_JAVA_OPTS: "-Xms512m -Xmx512m"
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:9200/_cluster/health"]
      interval: 10s

  postgres:
    image: postgres:17
    ports: ["5432:5432"]
    environment:
      POSTGRES_DB: lightscope
      POSTGRES_USER: lightscope
      POSTGRES_PASSWORD: lightscope123
    volumes: ["postgres_data:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U lightscope"]
      interval: 10s

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s

  prometheus:
    image: prom/prometheus:latest
    ports: ["9090:9090"]
    volumes: ["./infra/prometheus.yml:/etc/prometheus/prometheus.yml"]

  grafana:
    image: grafana/grafana:latest
    ports: ["3000:3000"]
    environment:
      GF_SECURITY_ADMIN_PASSWORD: lightscope
    volumes: ["grafana_data:/var/lib/grafana"]

  ollama:
    image: ollama/ollama:latest
    ports: ["11434:11434"]
    volumes: ["ollama_data:/root/.ollama"]

  lightscope-api:
    build:
      context: ..
      dockerfile: docker/Dockerfile.api
    ports: ["5000:8080", "5001:8081"]
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      LightScope__Provider: Local
      LightScope__Messaging__Provider: RabbitMQ
      LightScope__Messaging__ConnectionString: amqp://lightscope:lightscope123@rabbitmq:5672
      LightScope__Storage__Provider: MinIO
      LightScope__Storage__Endpoint: http://minio:9000
      LightScope__Storage__AccessKey: lightscope
      LightScope__Storage__SecretKey: lightscope123
      LightScope__Search__Provider: OpenSearch
      LightScope__Search__Endpoint: http://opensearch:9200
      LightScope__Metadata__Provider: PostgreSQL
      LightScope__Metadata__ConnectionString: Host=postgres;Database=lightscope;Username=lightscope;Password=lightscope123
      LightScope__Idempotency__Provider: Redis
      LightScope__Idempotency__ConnectionString: redis:6379
      LightScope__Query__Provider: DuckDB
      LightScope__Query__MinioEndpoint: http://minio:9000
      LightScope__AI__Provider: Ollama
      LightScope__AI__Endpoint: http://ollama:11434
      LightScope__AI__Model: llama3.2:3b
      LightScope__Auth__Provider: Local
      LightScope__Telemetry__Endpoint: http://prometheus:9090
    depends_on:
      minio: { condition: service_healthy }
      rabbitmq: { condition: service_healthy }
      opensearch: { condition: service_healthy }
      postgres: { condition: service_healthy }
      redis: { condition: service_healthy }

  lightscope-worker:
    build: { context: .., dockerfile: docker/Dockerfile.worker }
    environment:
      DOTNET_ENVIRONMENT: Development
      LightScope__Provider: Local
      # same env vars as api
    depends_on: [lightscope-api, rabbitmq, minio, opensearch, redis]

  lightscope-puller:
    build: { context: .., dockerfile: docker/Dockerfile.puller }
    depends_on: [rabbitmq, minio]

  lightscope-queryengine:
    build: { context: .., dockerfile: docker/Dockerfile.queryengine }
    ports: ["5002:8080"]
    depends_on: [opensearch, postgres, redis]

volumes:
  minio_data:
  postgres_data:
  grafana_data:
  ollama_data:
```

### 5.3 DuckDB as Local Query Engine

```csharp
// LightScope.Adapters.Local/Query/DuckDbQueryEngine.cs
public class DuckDbQueryEngine : IQueryEngine
{
    private readonly DuckDBConnection _conn;

    public DuckDbQueryEngine(IOptions<DuckDbOptions> opts)
    {
        _conn = new DuckDBConnection("DataSource=:memory:");
        _conn.Open();
        // Configure S3-compatible access to MinIO
        _conn.Execute($"""
            INSTALL httpfs; LOAD httpfs;
            SET s3_endpoint='{opts.Value.MinioEndpoint}';
            SET s3_access_key_id='{opts.Value.AccessKey}';
            SET s3_secret_access_key='{opts.Value.SecretKey}';
            SET s3_use_ssl=false;
            SET s3_url_style='path';
            """);
    }

    public async Task<QueryCostEstimate> EstimateCostAsync(string sql, CancellationToken ct)
    {
        // DuckDB: EXPLAIN SELECT ... returns estimated rows/bytes
        var explain = await _conn.QueryAsync<string>($"EXPLAIN {sql}");
        return ParseExplainOutput(explain);
    }

    public async Task<QuerySubmitResult> SubmitAsync(string tenantId, string sql, CancellationToken ct)
    {
        var safeSql = InjectTenantFilter(sql, tenantId);
        // DuckDB queries: SELECT * FROM read_parquet('s3://lightscope-logs/logs/tenant=t1/**/*.parquet')
        var results = await _conn.QueryAsync<LogEntryRow>(safeSql, ct);
        var queryId = UuidV7.New().ToString();
        _resultCache[queryId] = results.ToList();
        return new QuerySubmitResult { QueryId = queryId, Status = QueryStatus.Succeeded };
    }
}
```

---

## 6. Core Design Principles

| Principle | Description |
|-----------|-------------|
| **Hexagonal Architecture** | Core domain has zero cloud SDK dependencies; all adapters implement interfaces |
| **Open Schema + Registry** | Schema-on-read via Parquet + `ISchemaRegistry`; backward-compatible evolution |
| **Exactly-Once Processing** | `IIdempotencyStore` deduplicates by `LogEntry.Id` (UUIDv7) |
| **Polyglot Ingestion** | REST, gRPC streaming, bulk file upload, pull-based connectors, Kafka |
| **Tier-Aware Querying** | Auto-routing to hot/warm/cold based on time range; cross-tier fan-out |
| **Cost-Guarded Queries** | Pre-execution cost estimation; tenant scan limits enforced |
| **AI-Augmented UX** | NL→SQL via GitHub Models API; graph type suggestion; SQL safety validation |
| **Self-Observability** | OpenTelemetry traces/metrics/logs; `/health/ready`, `/health/live`, `/metrics` |
| **Materialized Views** | Pre-aggregated error rates, latency percentiles for low-latency dashboards |
| **Replay Capability** | Re-process any time window from object store (for parser bugs, schema changes) |
| **Multi-Tenancy** | Per-tenant isolation at S3 prefix, OpenSearch index, query workgroup, rate limit |
| **Local-First Dev** | Full Docker stack, zero cloud dependency |
| **Plugin Connectors** | `IPullConnector` factory for extensible external source integration |
| **Dual Auth** | API Key (SHA-256 hash + Redis cache) + JWT Bearer both supported simultaneously |

---

## 7. Data Model & Schema Design

### 7.1 Canonical Log Entry (.NET 10 / C# 14)

```csharp
// LightScope.Core/Models/LogEntry.cs
public sealed record LogEntry
{
    // --- Identity ---
    public required string Id { get; init; }              // UUIDv7 (time-sortable, globally unique)
    public required string TenantId { get; init; }        // Always from auth context — never from payload
    public required string SourceId { get; init; }        // App/host/service name

    // --- Classification ---
    public required LogType LogType { get; init; }
    public required string Category { get; init; }        // "http-access" | "sql-error" | "cpu"
    public required LogLevel Level { get; init; }
    public required string Environment { get; init; }     // prod | staging | dev

    // --- Timing ---
    public required DateTimeOffset Timestamp { get; init; }     // Event time (UTC)
    public DateTimeOffset IngestedAt { get; init; } = DateTimeOffset.UtcNow;

    // --- Core Payload ---
    public required string Message { get; init; }
    public string? StackTrace { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }

    // --- Host/Network Context ---
    public string? Hostname { get; init; }
    public string? IpAddress { get; init; }
    public string? Region { get; init; }
    public string? AvailabilityZone { get; init; }

    // --- HTTP Context ---
    public HttpLogContext? Http { get; init; }

    // --- Performance Metrics ---
    public MetricPayload? Metric { get; init; }

    // --- Schema Versioning (NEW) ---
    public uint SchemaVersion { get; init; } = 1;         // Incremented on breaking schema changes

    // --- Flexible Extension ---
    public Dictionary<string, string>? Tags { get; init; }         // Indexed — searchable
    public Dictionary<string, object>? Metadata { get; init; }     // Non-indexed — arbitrary

    // --- Ingestion Provenance ---
    public required IngestionMode IngestionMode { get; init; }
    public string? PullSourceId { get; init; }
}

public sealed record HttpLogContext
{
    public string? Method { get; init; }
    public string? Path { get; init; }
    public int? StatusCode { get; init; }
    public long? DurationMs { get; init; }
    public string? UserAgent { get; init; }
    public string? ClientIp { get; init; }
    public long? RequestBytes { get; init; }
    public long? ResponseBytes { get; init; }
}

public sealed record MetricPayload
{
    public required string MetricName { get; init; }
    public required double Value { get; init; }
    public required string Unit { get; init; }    // ms | bytes | percent | count | rps | errors/s
    public Dictionary<string, string>? Dimensions { get; init; }
}

public enum LogType    { Application, Error, Network, OS, Metric, Audit, Custom }
public enum LogLevel   { Trace, Debug, Info, Warn, Error, Fatal }
public enum IngestionMode { Push, Pull, Agent, Replay }
```

### 7.2 DTO vs Domain Separation

```csharp
// LightScope.Core/DTOs/LogEntryDto.cs  ← API input (untrusted)
// LogEntryDto is what the caller sends; LogEntry is what the system uses.
// Conversion happens in the MediatR command handler, after auth context is applied.

public sealed class LogEntryDto
{
    public string? Id { get; set; }                     // Ignored if present — system generates
    public required string SourceId { get; set; }
    public required string LogType { get; set; }
    public required string Level { get; set; }
    public required string Environment { get; set; }
    public required string Category { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? Hostname { get; set; }
    public string? IpAddress { get; set; }
    public HttpLogContextDto? Http { get; set; }
    public MetricPayloadDto? Metric { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

// DtoMapper: LogEntryDto → LogEntry (enriches TenantId, generates Id, sets IngestedAt)
public static class DtoMapper
{
    public static LogEntry ToDomain(LogEntryDto dto, string tenantId, IngestionMode mode) =>
        new()
        {
            Id            = UuidV7.New().ToString(),   // Always system-generated
            TenantId      = tenantId,                  // Always from auth — never from DTO
            SourceId      = dto.SourceId,
            LogType       = Enum.Parse<LogType>(dto.LogType, ignoreCase: true),
            Level         = Enum.Parse<LogLevel>(dto.Level, ignoreCase: true),
            Environment   = dto.Environment,
            Category      = dto.Category,
            Timestamp     = dto.Timestamp,
            Message       = dto.Message,
            StackTrace    = dto.StackTrace,
            TraceId       = dto.TraceId,
            SpanId        = dto.SpanId,
            Hostname      = dto.Hostname,
            IpAddress     = dto.IpAddress,
            Http          = dto.Http?.ToDomain(),
            Metric        = dto.Metric?.ToDomain(),
            Tags          = dto.Tags,
            Metadata      = dto.Metadata,
            IngestedAt    = DateTimeOffset.UtcNow,
            IngestionMode = mode,
            SchemaVersion = SchemaVersions.Current
        };
}
```

### 7.3 Per-Tenant Settings (Data Retention + Limits)

```csharp
// LightScope.Core/Models/TenantSettings.cs
public sealed class TenantSettings
{
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public string Plan { get; init; } = "free";

    // Data Retention
    public int HotRetentionDays { get; init; } = 3;          // OpenSearch index TTL
    public int WarmRetentionDays { get; init; } = 90;         // S3 Standard
    public int ColdRetentionDays { get; init; } = 730;        // S3 Glacier
    public int TotalRetentionDays { get; init; } = 2555;      // S3 expiration

    // Rate Limits (per minute)
    public int MaxIngestPerMinute { get; init; } = 100_000;
    public int MaxQueriesPerMinute { get; init; } = 60;

    // Query Guardrails
    public int MaxQueryScanGb { get; init; } = 10;
    public int MaxQueryExecutionSeconds { get; init; } = 60;
    public bool RequireTimeFilter { get; init; } = true;
    public bool RequireLimit { get; init; } = true;

    // Storage (per-tenant isolation option)
    public string? DedicatedS3Prefix { get; init; }          // Optional per-tenant prefix override
    public string? DedicatedAthenaWorkgroup { get; init; }   // Optional per-tenant workgroup

    // AI
    public bool AiQueriesEnabled { get; init; } = true;
    public bool GraphSuggestionsEnabled { get; init; } = true;
}
```

### 7.4 S3 Partition Strategy

```
s3://lightscope-logs/
  logs/
    tenant={tenantId}/               ← Security partition (IAM prefix restriction)
      logtype={logType}/             ← Prune by log type
        env={environment}/           ← Prune by environment
          year={yyyy}/               ← Prune by year
            month={MM}/              ← Prune by month
              day={dd}/              ← Prune by day
                hour={HH}/           ← Prune by hour (finest granularity)
                  part-{uuid7}.parquet  ← ~128 MB, SNAPPY, single row group
```

**Partition pruning example:** A query for `logtype=Error, env=prod, year=2026, month=03, day=23` scans only files under that path — potentially 0.01% of total data at scale.

---

## 8. Schema Evolution Strategy

### 8.1 Why Schema Evolution Matters

Without a schema strategy:
- Adding a new field to `LogEntry` may break existing Parquet readers (schema mismatch)
- Removing a field silently produces `NULL` in Athena or causes query failures
- Renaming a field breaks all saved SQL queries referencing the old name
- Different files in the same partition can have incompatible schemas

### 8.2 Schema Evolution Rules

| Change Type | Classification | Safe? | Action Required |
|-------------|---------------|-------|----------------|
| Add optional field | Backward-compatible | ✅ Yes | Increment minor version; old readers return NULL |
| Add required field with default | Backward-compatible | ✅ Yes | Provide default in reader |
| Remove field | Breaking | ❌ No | Deprecate for 1 version; then remove with major bump |
| Rename field | Breaking | ❌ No | Add new field, alias old name for 2 versions |
| Change field type (widening: int→long) | Backward-compatible | ✅ Yes | Parquet handles safely |
| Change field type (narrowing: long→int) | Breaking | ❌ No | Forbidden without major version bump |
| Add enum value | Backward-compatible | ✅ Yes | Readers must handle unknown values |
| Remove enum value | Breaking | ❌ No | Deprecate first |

### 8.3 ISchemaRegistry Implementation

```csharp
// LightScope.Core/Schema/SchemaVersion.cs
public sealed record SchemaVersion
{
    public required string TenantId { get; init; }
    public required uint Version { get; init; }          // e.g., 3
    public required string VersionString { get; init; }  // e.g., "1.3.0" (major.minor.patch)
    public required SchemaDefinition Definition { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
    public string? ChangeDescription { get; init; }
}

public sealed record SchemaDefinition
{
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
    public required IReadOnlyList<string> RequiredFields { get; init; }
    public required IReadOnlyList<string> IndexedTagKeys { get; init; }  // Tags to index in OpenSearch
}

public sealed record FieldDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }     // "string" | "int64" | "double" | "bool" | "timestamp"
    public bool IsNullable { get; init; } = true;
    public bool IsDeprecated { get; init; } = false;
    public string? DeprecatedSince { get; init; }
    public object? DefaultValue { get; init; }
}

public sealed record SchemaCompatibilityResult
{
    public bool IsCompatible { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool HasUnknownFields { get; init; }          // Unknown fields → stored in Metadata
}
```

### 8.4 Schema Inference Mode

When `LightScope:Schema:Mode = Infer`, the system automatically evolves the schema by observing incoming entries:

```csharp
// LightScope.Core/Schema/SchemaInferenceEngine.cs
public class SchemaInferenceEngine
{
    // Observes a batch of LogEntry objects, detects new Tag keys or Metadata keys,
    // proposes a schema update if new fields exceed the discovery threshold.
    public async Task<SchemaProposal?> InferAsync(
        IReadOnlyList<LogEntry> entries,
        SchemaVersion current,
        CancellationToken ct)
    {
        var newTagKeys = entries
            .SelectMany(e => e.Tags?.Keys ?? [])
            .Distinct()
            .Except(current.Definition.IndexedTagKeys)
            .ToList();

        if (newTagKeys.Count > 0)
            return new SchemaProposal(
                NewIndexedTagKeys: newTagKeys,
                ChangeType: SchemaChangeType.AddedIndexedTags,
                IsBreaking: false);

        return null;
    }
}
```

### 8.5 Parquet Schema Compatibility During Reads

```csharp
// LightScope.Core/Storage/ParquetSchemaEvolutionReader.cs
// When reading Parquet files of different schema versions from S3:
// - Use "schema merging" (Parquet.Net supports this)
// - Missing columns → filled with null/default
// - Unknown extra columns → surfaced in Metadata dictionary
// This ensures Athena/DuckDB can read files written by both v1 and v2 of the schema.

public class ParquetSchemaEvolutionReader
{
    public async Task<IReadOnlyList<LogEntry>> ReadWithEvolutionAsync(
        Stream parquetStream,
        SchemaVersion targetSchema,
        CancellationToken ct)
    {
        // Read file schema from Parquet footer
        // Compute field mapping: fileField → domainField
        // Apply defaults for fields present in targetSchema but absent in file
        // Capture unknown fields into entry.Metadata["_unknown_{fieldName}"]
    }
}
```

---

## 9. Exactly-Once & Idempotency Strategy

### 9.1 The Problem

SQS delivers messages **at least once**. Under failure conditions (worker crash after processing but before ack, network timeout), the same message can be delivered multiple times. Without deduplication:
- Duplicate log entries in Parquet files
- Duplicate documents in OpenSearch
- Inflated alert counts and metric aggregations

### 9.2 UUIDv7 as Idempotency Key

Every `LogEntry` has an `Id` field set to a **UUIDv7** (time-ordered UUID). When agents generate the Id before sending, the same entry always carries the same Id regardless of retry. When LightScope generates the Id (for REST push without a client-supplied Id), the API generates and returns it — so any resubmission uses the same Id.

```
UUIDv7 structure: [48-bit timestamp ms][4-bit version=7][12-bit random][2-bit variant][62-bit random]
→ Globally unique, time-sortable, and stable across retries when generated once per event.
```

### 9.3 IIdempotencyStore Implementation

```csharp
// LightScope.Adapters.Local/Idempotency/RedisIdempotencyStore.cs
public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public async Task<bool> ExistsAsync(string id, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync($"idem:{id}");
    }

    public async Task MarkProcessedAsync(string id, TimeSpan ttl, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        // SET idem:{id} "1" EX {ttl.TotalSeconds} NX
        await db.StringSetAsync($"idem:{id}", "1", ttl, When.NotExists);
    }

    public async Task<long> PruneExpiredAsync(CancellationToken ct)
    {
        // Redis TTL handles expiry automatically — no manual pruning needed
        return 0;
    }
}
```

### 9.4 Idempotency in the Worker Pipeline

```csharp
// LightScope.Worker/Services/LogProcessorService.cs (relevant section)

private async Task ProcessEntryAsync(LogEntry entry, CancellationToken ct)
{
    // 1. Check idempotency store before any processing
    if (await _idempotencyStore.ExistsAsync(entry.Id, ct))
    {
        _logger.LogDebug("Duplicate entry {Id} skipped", entry.Id);
        _telemetry.RecordDuplicate();
        return;
    }

    // 2. Process (write to Parquet buffer, queue for OpenSearch)
    await _parquetBuffer.AddAsync(entry, ct);
    await _searchIndexer.QueueForIndexAsync(entry, ct);

    // 3. Mark as processed ONLY after successful processing
    // TTL = HotRetentionDays + 1 day buffer (no need to track longer)
    await _idempotencyStore.MarkProcessedAsync(entry.Id,
        TimeSpan.FromDays(_tenant.HotRetentionDays + 1), ct);
}
```

### 9.5 OpenSearch Idempotency

OpenSearch uses `_id` for document-level deduplication. Set `_id = LogEntry.Id` on every index request:

```csharp
// BulkIndexRequest: use entry.Id as _id → duplicate index → update (no duplicate doc)
var bulkRequest = new BulkRequest("lightscope-{tenantId}-{date}")
{
    Operations = entries.Select(e => new BulkIndexOperation<LogEntryDocument>(
        new LogEntryDocument(e)) { Id = e.Id }).ToList()
};
// OpenSearch: if document with same _id exists → update (idempotent)
```

### 9.6 Parquet Deduplication (Batch Level)

Within a write batch, deduplicate by `Id` before writing:

```csharp
// Dedup within batch before Parquet write (cheap, in-memory)
var deduped = batch
    .GroupBy(e => e.Id)
    .Select(g => g.First())
    .ToList();
await _parquetWriter.WriteAsync(deduped, s3Key, ct);
```

Cross-file deduplication (entries that were written in a previous file and appear again) is handled by the `IIdempotencyStore` Redis check before the entry reaches the write buffer.

---

## 10. API Design

### 10.1 .NET 10 Minimal API Style

```csharp
// LightScope.Api/Program.cs — Minimal API with MediatR routing
var app = builder.Build();

// --- Ingestion ---
app.MapPost("/api/v1/logs",
    async (IngestLogsRequest req, IMediator mediator, HttpContext ctx, CancellationToken ct) =>
    {
        var tenantId = ctx.GetTenantId();
        var result = await mediator.Send(new IngestLogsCommand(req.Entries, tenantId), ct);
        return result.HasErrors
            ? Results.BadRequest(result)
            : Results.Accepted(value: result);
    })
    .RequireAuthorization()
    .RequireRateLimiting("tenant-ingest")
    .WithName("IngestLogs")
    .WithOpenApi();

app.MapPost("/api/v1/logs/bulk",
    async (IFormFile file, IMediator mediator, HttpContext ctx, CancellationToken ct) => { ... })
    .RequireAuthorization()
    .DisableRequestSizeLimit();

app.MapPost("/api/v1/metrics",
    async (IngestMetricsRequest req, IMediator mediator, HttpContext ctx, CancellationToken ct) => { ... })
    .RequireAuthorization()
    .RequireRateLimiting("tenant-ingest");

// --- Query ---
app.MapPost("/api/v1/query/sql",        /* ... */).RequireAuthorization();
app.MapGet("/api/v1/query/{queryId}/status", /* ... */).RequireAuthorization();
app.MapGet("/api/v1/query/{queryId}/results", /* ... */).RequireAuthorization();
app.MapPost("/api/v1/query/search",     /* ... */).RequireAuthorization();
app.MapPost("/api/v1/query/natural",    /* ... */).RequireAuthorization();

// --- Graphs ---
app.MapPost("/api/v1/graphs/suggest",   /* ... */).RequireAuthorization();
app.MapPost("/api/v1/graphs/render",    /* ... */).RequireAuthorization();
app.MapGet("/api/v1/graphs/prebuilt",   /* ... */).RequireAuthorization();

// --- Pull Jobs ---
app.MapGet("/api/v1/pull-jobs",         /* ... */).RequireAuthorization();
app.MapPost("/api/v1/pull-jobs",        /* ... */).RequireAuthorization();
app.MapPut("/api/v1/pull-jobs/{jobId}", /* ... */).RequireAuthorization();
app.MapDelete("/api/v1/pull-jobs/{jobId}", /* ... */).RequireAuthorization();
app.MapPost("/api/v1/pull-jobs/{jobId}/run", /* ... */).RequireAuthorization();

// --- Alerts ---
app.MapGet("/api/v1/alerts",            /* ... */).RequireAuthorization();
app.MapPost("/api/v1/alerts",           /* ... */).RequireAuthorization();

// --- Auth ---
app.MapPost("/api/v1/auth/keys",        /* ... */).RequireAuthorization();
app.MapGet("/api/v1/auth/keys",         /* ... */).RequireAuthorization();
app.MapDelete("/api/v1/auth/keys/{id}", /* ... */).RequireAuthorization();

// --- Replay ---
app.MapPost("/api/v1/replay",           /* ... */).RequireAuthorization();
app.MapGet("/api/v1/replay/{jobId}",    /* ... */).RequireAuthorization();

// --- Saved Queries ---
app.MapGet("/api/v1/query/saved",       /* ... */).RequireAuthorization();
app.MapPost("/api/v1/query/saved",      /* ... */).RequireAuthorization();
app.MapPost("/api/v1/query/saved/{id}/run", /* ... */).RequireAuthorization();

// --- Health & Metrics ---
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
app.MapHealthChecks("/health/live",  new() { Predicate = r => r.Tags.Contains("live") });
app.MapPrometheusScrapingEndpoint("/metrics");
```

### 10.2 MediatR Command/Query Pattern

```csharp
// Commands (writes / side effects)
public sealed record IngestLogsCommand(
    IReadOnlyList<LogEntryDto> Entries,
    string TenantId) : IRequest<IngestResult>;

public sealed record StartReplayCommand(
    string TenantId,
    DateTimeOffset From,
    DateTimeOffset To,
    ReplayOptions Options) : IRequest<ReplayJob>;

// Queries (reads / no side effects)
public sealed record ExecuteSqlQuery(
    string TenantId,
    string Sql,
    bool Async) : IRequest<QuerySubmitResult>;

public sealed record GetNaturalLanguageQuery(
    string TenantId,
    string Question,
    string? Environment) : IRequest<NaturalLanguageQueryResult>;

// Handler example
public class IngestLogsHandler(
    ISchemaRegistry schemaRegistry,
    IMessageBus messageBus,
    IIdempotencyStore idempotencyStore,
    ITenantService tenantService,
    ILogger<IngestLogsHandler> logger) : IRequestHandler<IngestLogsCommand, IngestResult>
{
    public async Task<IngestResult> Handle(IngestLogsCommand cmd, CancellationToken ct)
    {
        var tenant = await tenantService.GetAsync(cmd.TenantId, ct);
        var entries = new List<LogEntry>(cmd.Entries.Count);
        var errors = new List<string>();

        foreach (var dto in cmd.Entries)
        {
            try
            {
                var entry = DtoMapper.ToDomain(dto, cmd.TenantId, IngestionMode.Push);

                // Schema validation
                var compat = await schemaRegistry.CheckCompatibilityAsync(cmd.TenantId, entry, ct);
                if (!compat.IsCompatible)
                {
                    errors.Add($"Schema violation on entry {entry.Id}: {string.Join(", ", compat.Errors)}");
                    continue;
                }

                entries.Add(entry);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (entries.Count > 0)
        {
            await messageBus.PublishAsync("lightscope-ingest", new LogEntryBatch(entries),
                new MessageAttributes { ["logLevel"] = entries.Max(e => e.Level).ToString() }, ct);
        }

        return new IngestResult(Accepted: entries.Count, Rejected: errors.Count, Errors: errors);
    }
}
```

### 10.3 Key REST Contracts

**POST /api/v1/logs — Response:**
```json
{
  "accepted": 99,
  "rejected": 1,
  "batchId": "019526b4-7c2a-7b3d-a1c2-4f8e3d9b2a1f",
  "errors": ["Entry 7: unknown logType 'CustomXyz'"]
}
```

**POST /api/v1/query/sql — Request:**
```json
{
  "sql": "SELECT sourceid, COUNT(*) AS errors FROM logs WHERE logtype='Error' AND year='2026' AND month='03' AND day='23' GROUP BY 1 ORDER BY 2 DESC LIMIT 20",
  "async": true,
  "confirmCostIfAboveUsd": 0.10
}
```

**POST /api/v1/query/sql — Response (before cost confirmation):**
```json
{
  "queryId": null,
  "status": "PendingCostConfirmation",
  "costEstimate": {
    "estimatedScanGb": 14.2,
    "estimatedCostUsd": 0.071,
    "warning": "Query will scan 14.2 GB. Add partition filters (year/month/day) to reduce cost."
  },
  "confirmationToken": "tok_abc123"
}
```

---

## 11. API-Layer Backpressure & Rate Limiting

### 11.1 Token Bucket Rate Limiter (per tenant)

```csharp
// LightScope.Api/RateLimiting/TenantRateLimiterExtensions.cs
public static IServiceCollection AddLightScopeRateLimiting(this IServiceCollection services)
{
    services.AddRateLimiter(options =>
    {
        // Ingestion rate limit: configurable per tenant from TenantSettings
        options.AddPolicy("tenant-ingest", context =>
        {
            var tenantId = context.User.GetTenantId() ?? "anonymous";
            var settings = context.RequestServices
                .GetRequiredService<ITenantSettingsCache>()
                .Get(tenantId);

            return RateLimitPartition.GetTokenBucket(tenantId,
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit          = settings.MaxIngestPerMinute / 60 * 10, // 10s burst
                    TokensPerPeriod     = settings.MaxIngestPerMinute / 60,       // per second
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 100,                                   // brief queue
                    AutoReplenishment    = true
                });
        });

        // Query rate limit: separate policy, lower ceiling
        options.AddPolicy("tenant-query", context =>
        {
            var tenantId = context.User.GetTenantId() ?? "anonymous";
            var settings = context.RequestServices
                .GetRequiredService<ITenantSettingsCache>()
                .Get(tenantId);

            return RateLimitPartition.GetSlidingWindow(tenantId,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit          = settings.MaxQueriesPerMinute,
                    Window               = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow    = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 5
                });
        });

        // Global fallback (unauthenticated or unknown tenant)
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => RateLimitPartition.GetFixedWindow("global",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1000,
                    Window      = TimeSpan.FromMinutes(1)
                }));

        // Return 429 with Retry-After header
        options.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.HttpContext.Response.Headers.RetryAfter = "60";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                retryAfterSeconds = 60
            }, ct);
        };
    });

    return services;
}
```

### 11.2 Adaptive Throttling Signals via gRPC

The gRPC `StreamWithAck` endpoint returns `AckResponse.throttled = true` when the message bus queue depth exceeds a threshold:

```csharp
// LightScope.Api/Grpc/LogIngestionGrpcService.cs (throttle check)
private async Task<bool> IsThrottledAsync(string tenantId, CancellationToken ct)
{
    var metrics = await _messageBus.GetQueueMetricsAsync("ls-storage-writer", ct);
    // If queue has > 100k messages in-flight, signal backpressure
    return metrics.ApproximateMessageCount > 100_000;
}
```

### 11.3 Payload Size Enforcement

```csharp
// In Program.cs: reject oversized payloads before they hit MediatR
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength > 4 * 1024 * 1024) // 4 MB
    {
        context.Response.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
        await context.Response.WriteAsJsonAsync(new { error = "Payload too large. Max 4 MB." });
        return;
    }
    await next();
});
```

---

## 12. Log Ingestion Pipeline with Async/Multi-Thread Detail

### 12.1 Push Flow (Thread Model)

```
ASP.NET Core Request Thread Pool (async, non-blocking):
│
├── [1] HTTP request received
├── [2] Rate limiter check (token bucket, ~microseconds)
├── [3] Auth middleware: API key lookup (Redis cache, ~1ms) or JWT validate (in-memory, ~0.1ms)
├── [4] Tenant context injected into HttpContext.Items
├── [5] MediatR: IngestLogsCommand dispatched
│     └── IngestLogsHandler:
│           ├── Validate DTOs (FluentValidation, synchronous, ~0.1ms/entry)
│           ├── Map DTOs → LogEntry (DtoMapper.ToDomain, enriches TenantId+Id)
│           ├── Schema compatibility check (ISchemaRegistry, cache-backed, ~1ms)
│           └── IMessageBus.PublishAsync (batch of entries → SNS/RabbitMQ, ~5ms)
│
└── [6] Return 202 Accepted  ← Total target: < 20ms p99 end-to-end

══════════════════════════════════════════════════════════════════
  WORKER SERVICE — Separate process, scales independently
══════════════════════════════════════════════════════════════════

4 Queues × 4 Consumer Tasks each = 16 parallel consumer pipelines

Per-queue consumer pipeline (StorageWriter shown):

Task 1..4 (parallel consumers from ls-storage-writer):
  Loop:
    Receive SQS batch (100 messages, 20s long poll)
    │
    Parallel.ForEachAsync (DOP=8) per message:
      │
      ├── Deserialize JSON → LogEntryBatch
      ├── For each entry:
      │     ├── IIdempotencyStore.ExistsAsync (Redis GET, ~1ms)
      │     │     If exists → skip (duplicate)
      │     │     If not → continue
      │     ├── Schema validation (cache hit, ~0.1ms)
      │     └── Write to bounded Channel<LogEntry> (capacity=50,000)
      │
    Acknowledge SQS batch on success (batch delete, ~5ms)
    Nack (leave for retry) on failure → goes to DLQ after 3 attempts

Channel<LogEntry> (bounded, backpressure):
  ↓
BatchWriter Task (single task, reads from channel):
  ConcurrentDictionary<PartitionKey, List<LogEntry>> partitionBuffers
  │
  PeriodicTimer (5 seconds):
    For each partition buffer with entries:
      Dedup by Id (in-memory)
      Write Parquet file to MinIO/S3 (async, ~50-200ms per file)
      IIdempotencyStore.MarkProcessedAsync for each entry (Redis MSET, ~5ms batch)
  │
  If any partition buffer >= 1,000 entries → immediate flush (don't wait for timer)
```

### 12.2 Worker Concurrency Code

```csharp
// LightScope.Worker/Workers/StorageWriterWorker.cs
public class StorageWriterWorker(
    IMessageBus messageBus,
    IIdempotencyStore idempotencyStore,
    ISchemaRegistry schemaRegistry,
    IParquetWriter parquetWriter,
    IObjectStore objectStore,
    IOptions<WorkerOptions> opts,
    ILogger<StorageWriterWorker> logger,
    IMeterFactory meterFactory) : BackgroundService
{
    private readonly Channel<LogEntry> _channel = Channel.CreateBounded<LogEntry>(
        new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true        // Single BatchWriter reads from channel
        });

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // N parallel consumer tasks (default 4)
        var consumers = Enumerable.Range(0, opts.Value.ConsumerCount)
            .Select(_ => ConsumeFromQueueAsync(ct))
            .ToArray();

        var writer = BatchWriterAsync(ct);
        await Task.WhenAll([..consumers, writer]);
    }

    private async Task ConsumeFromQueueAsync(CancellationToken ct)
    {
        await foreach (var envelope in messageBus.SubscribeAsync<LogEntryBatch>("ls-storage-writer", ct))
        {
            try
            {
                // Parallel deserialization + idempotency check within batch
                await Parallel.ForEachAsync(
                    envelope.Payload.Entries,
                    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
                    async (entry, innerCt) =>
                    {
                        if (await idempotencyStore.ExistsAsync(entry.Id, innerCt))
                        {
                            _duplicateCounter.Add(1);
                            return;
                        }
                        await _channel.Writer.WriteAsync(entry, innerCt);
                    });

                await messageBus.AcknowledgeAsync(envelope.ReceiptHandle, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing batch {MsgId}", envelope.MessageId);
                await messageBus.NegativeAcknowledgeAsync(envelope.ReceiptHandle, ct);
            }
        }
    }

    private async Task BatchWriterAsync(CancellationToken ct)
    {
        var buffers = new ConcurrentDictionary<string, List<LogEntry>>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var drainTask = DrainChannelToBuffersAsync(buffers, ct);

        while (await timer.WaitForNextTickAsync(ct))
            await FlushAllBuffersAsync(buffers, ct);

        await drainTask;
    }

    private async Task DrainChannelToBuffersAsync(
        ConcurrentDictionary<string, List<LogEntry>> buffers, CancellationToken ct)
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync(ct))
        {
            var key = S3PathBuilder.GetPartitionKey(entry);
            var list = buffers.GetOrAdd(key, _ => []);

            lock (list) list.Add(entry);

            if (list.Count >= 1_000)
                await FlushPartitionAsync(key, buffers, ct);
        }
    }

    private async Task FlushPartitionAsync(
        string partitionKey,
        ConcurrentDictionary<string, List<LogEntry>> buffers,
        CancellationToken ct)
    {
        if (!buffers.TryGetValue(partitionKey, out var list)) return;

        List<LogEntry> batch;
        lock (list)
        {
            if (list.Count == 0) return;
            batch = [.. list.GroupBy(e => e.Id).Select(g => g.First())]; // dedup
            list.Clear();
        }

        var s3Key = S3PathBuilder.Build(partitionKey);
        await using var stream = await parquetWriter.WriteAsync(batch, ct);
        await objectStore.WriteAsync(s3Key, stream, "application/octet-stream", ct);

        // Mark all as processed in idempotency store (batch Redis MSET)
        await Task.WhenAll(batch.Select(e =>
            idempotencyStore.MarkProcessedAsync(e.Id, TimeSpan.FromDays(4), ct)));

        _writeLatencyHistogram.Record((DateTimeOffset.UtcNow - batch[0].IngestedAt).TotalMilliseconds);
        logger.LogInformation("Flushed {Count} entries to {Key}", batch.Count, s3Key);
    }
}
```

### 12.3 Polly Retry Policies

All external calls (S3, OpenSearch, SQS, Redis) use **Polly** retry pipelines:

```csharp
// LightScope.Core/Resilience/ResiliencePolicies.cs
public static class ResiliencePipelines
{
    public static ResiliencePipeline<T> GetStorageWritePipeline<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 3,
                BackoffType      = DelayBackoffType.Exponential,
                Delay            = TimeSpan.FromMilliseconds(200),
                UseJitter        = true,
                ShouldHandle     = new PredicateBuilder<T>().Handle<IOException>()
                                                             .Handle<TimeoutException>()
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
            {
                FailureRatio          = 0.5,
                SamplingDuration      = TimeSpan.FromSeconds(30),
                MinimumThroughput     = 10,
                BreakDuration         = TimeSpan.FromSeconds(60)
            })
            .Build();

    public static ResiliencePipeline<T> GetSearchIndexPipeline<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 5,
                BackoffType      = DelayBackoffType.Exponential,
                Delay            = TimeSpan.FromMilliseconds(100),
                UseJitter        = true
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();
}
```

### 12.4 Volume Capacity Planning

At **1 million log entries / minute** (~16,667/sec):

| Stage | Target Throughput | Concurrency Model |
|-------|-----------------|------------------|
| API ingestion | 16,667 entries/sec | Async thread pool; < 20ms p99 |
| Message bus | ~unlimited (Standard) | No bottleneck |
| Worker consumers | 4 queues × 4 tasks × 100 batch | `Parallel.ForEachAsync` DOP=8 |
| Redis idempotency | ~16,667 GET/sec | Redis handles >100K ops/sec |
| Parquet write | ~8 MB/sec per partition writer | Batch flush 5s / 1,000 records |
| OpenSearch index | ~2,000 docs/sec/shard | Bulk 100 docs/request |
| S3 PUT | ~1 file/5s per partition | 128 MB target file |

**Horizontal scaling:** each Worker pod = 16 parallel consumer pipelines. At 10 pods → 160 pipelines, handling well over 1M/min at comfortable utilization.

---

## 13. External Log Pulling (S3 & Others)

### 13.1 IPullConnector Interface

```csharp
// LightScope.Core/Connectors/IPullConnector.cs
public interface IPullConnector
{
    string SourceType { get; }
    Task<PullResult> PullAsync(PullJobConfig config, PullJobState state, CancellationToken ct);
}

// Named factory (Open/Closed — new connectors added without modifying existing code):
// services.AddKeyedSingleton<IPullConnector, AwsS3PullConnector>("AwsS3");
// services.AddKeyedSingleton<IPullConnector, CloudWatchPullConnector>("CloudWatch");
// services.AddKeyedSingleton<IPullConnector, AzureBlobPullConnector>("AzureBlob");
// services.AddKeyedSingleton<IPullConnector, HttpPullConnector>("Http");
```

### 13.2 Supported Log File Formats

| Format | Parser | Examples |
|--------|--------|---------|
| NDJSON | `NdJsonParser` | Application structured logs |
| JSON Array | `JsonArrayParser` | Exported log batches |
| W3C Extended | `W3CParser` | IIS logs, ALB access logs |
| CLF | `ClfParser` | Apache/Nginx access logs |
| CSV | `CsvParser` | Custom metric exports |
| Syslog RFC 5424 | `SyslogParser` | OS/network device logs |
| AWS CloudTrail | `CloudTrailParser` | AWS API audit logs |
| AWS VPC Flow | `VpcFlowParser` | Network flow logs |
| GZIP (any above) | `GzipParserDecorator` | Compressed variants |
| Parquet (re-ingest) | `ParquetReIngestParser` | Replay from cold storage |

---

## 14. Storage Strategy & OpenSearch ILM

### 14.1 Storage Tiers

| Tier | Store | Retention | Format | Use Case |
|------|-------|-----------|--------|---------|
| **Hot** (0–N days) | OpenSearch | Per-tenant setting | JSON documents | Real-time search, alerting |
| **Warm** (N–90 days) | S3 Standard / MinIO | Per-tenant setting | Parquet + SNAPPY | SQL queries via Athena/DuckDB |
| **Cold** (90+ days) | S3 Glacier IR | Per-tenant setting | Parquet + ZSTD | Compliance, audit |
| **Archive** | S3 Deep Archive | Configurable | Parquet + ZSTD | Long-term retention |

### 14.2 OpenSearch Index Naming & ILM

**Index naming convention:**
```
lightscope-{tenantId}-{yyyy.MM.dd}
```
Examples:
- `lightscope-tenant-acme-2026.03.23`
- `lightscope-tenant-acme-2026.03.22`

**Index Lifecycle Management (ILM) Policy:**

```json
{
  "policy": {
    "phases": {
      "hot": {
        "min_age": "0ms",
        "actions": {
          "rollover": {
            "max_primary_shard_size": "50gb",
            "max_age": "1d",
            "max_docs": 50000000
          },
          "set_priority": { "priority": 100 }
        }
      },
      "warm": {
        "min_age": "1d",
        "actions": {
          "shrink": { "number_of_shards": 1 },
          "forcemerge": { "max_num_segments": 1 },
          "set_priority": { "priority": 50 }
        }
      },
      "delete": {
        "min_age": "{tenant.HotRetentionDays}d",
        "actions": {
          "delete": {}
        }
      }
    }
  }
}
```

**Index template (applied on creation):**

```json
{
  "index_patterns": ["lightscope-*"],
  "settings": {
    "number_of_shards": 2,
    "number_of_replicas": 1,
    "lifecycle.name": "lightscope-ilm-policy",
    "lifecycle.rollover_alias": "lightscope-{tenantId}-current"
  },
  "mappings": {
    "dynamic": false,
    "properties": {
      "id":           { "type": "keyword" },
      "tenantId":     { "type": "keyword" },
      "sourceId":     { "type": "keyword" },
      "logType":      { "type": "keyword" },
      "category":     { "type": "keyword" },
      "level":        { "type": "keyword" },
      "environment":  { "type": "keyword" },
      "timestamp":    { "type": "date" },
      "ingestedAt":   { "type": "date" },
      "message":      { "type": "text", "analyzer": "standard" },
      "stackTrace":   { "type": "text", "index": false },
      "traceId":      { "type": "keyword" },
      "hostname":     { "type": "keyword" },
      "ipAddress":    { "type": "ip" },
      "schemaVersion":{ "type": "integer" },
      "tags":         { "type": "flattened" },
      "http": {
        "properties": {
          "method":     { "type": "keyword" },
          "path":       { "type": "keyword" },
          "statusCode": { "type": "integer" },
          "durationMs": { "type": "long" }
        }
      },
      "metric": {
        "properties": {
          "metricName": { "type": "keyword" },
          "value":      { "type": "double" },
          "unit":       { "type": "keyword" }
        }
      }
    }
  }
}
```

**Sharding strategy:**
- Default: 2 primary shards per index (scales to ~50 GB/day each)
- High-volume tenants (>10M entries/day): 4–8 shards via per-tenant template override
- Replicas: 1 (production), 0 (local dev)

### 14.3 Parquet File Strategy

- **File size target:** 128 MB (optimal for Athena partition scan)
- **Compression:** SNAPPY (warm), ZSTD (cold — 30% better ratio)
- **Library:** `Parquet.Net v5.x` (.NET 10 compatible)
- **Schema merging:** enabled for backward-compatible reads across versions

### 14.4 S3 Lifecycle Policy

```json
{
  "Rules": [{
    "Id": "TierTransitions",
    "Filter": { "Prefix": "logs/" },
    "Status": "Enabled",
    "Transitions": [
      { "Days": 90,  "StorageClass": "GLACIER_IR" },
      { "Days": 365, "StorageClass": "DEEP_ARCHIVE" }
    ],
    "Expiration": { "Days": 2555 }
  }]
}
```

---

## 15. Query Tier Routing: Hot vs Warm vs Cold

### 15.1 Routing Decision Engine

```csharp
// LightScope.Core/Routing/QueryTierRouter.cs
public class QueryTierRouter(ILogger<QueryTierRouter> logger)
{
    public QueryTierDecision Route(ParsedQuery query, TenantSettings tenant)
    {
        var now = DateTimeOffset.UtcNow;
        var hotCutoff  = now.AddDays(-tenant.HotRetentionDays);
        var warmCutoff = now.AddDays(-tenant.WarmRetentionDays);

        // Rule 1: Full-text search always → Hot (OpenSearch only supports full-text)
        if (query.HasFullTextSearch)
        {
            logger.LogDebug("Query {Id} → Hot (full-text search)", query.QueryId);
            return new(QueryTier.Hot, Reason: "Full-text search requires OpenSearch");
        }

        // Rule 2: Entirely within hot window → Hot
        if (query.EarliestTimestamp >= hotCutoff && query.LatestTimestamp <= now)
            return new(QueryTier.Hot, Reason: $"Data within {tenant.HotRetentionDays}-day hot window");

        // Rule 3: Entirely within warm window → Warm
        if (query.EarliestTimestamp >= warmCutoff && query.LatestTimestamp < hotCutoff)
            return new(QueryTier.Warm, Reason: $"Data in warm window ({tenant.HotRetentionDays}–{tenant.WarmRetentionDays} days)");

        // Rule 4: Entirely in cold storage → Cold (with warning)
        if (query.EarliestTimestamp < warmCutoff)
        {
            if (query.LatestTimestamp < warmCutoff)
                return new(QueryTier.Cold, Reason: "Data in cold storage (>90 days)", Warning: "Query may take 30–120s");

            // Rule 5: Spans warm + cold → Cross-tier (fan-out)
            return new(QueryTier.CrossTier,
                SubQueries: [
                    new SubQuery(QueryTier.Warm, warmCutoff, query.LatestTimestamp),
                    new SubQuery(QueryTier.Cold,  query.EarliestTimestamp, warmCutoff)
                ],
                Reason: "Query spans warm and cold tiers — fan-out required");
        }

        // Rule 6: Spans hot + warm → Cross-tier fan-out
        return new(QueryTier.CrossTier,
            SubQueries: [
                new SubQuery(QueryTier.Hot,  hotCutoff, query.LatestTimestamp),
                new SubQuery(QueryTier.Warm, query.EarliestTimestamp, hotCutoff)
            ],
            Reason: "Query spans hot and warm tiers — fan-out required");
    }
}
```

### 15.2 Tier Routing with Concrete Examples

#### Example A — Real-time error spike (→ Hot)
```
User query: "All Fatal errors in payment-service, last 2 hours"

Time range: now-2h → now
Age: 2 hours < HotRetentionDays (3 days)
HasFullTextSearch: false

→ Tier: HOT (OpenSearch)
→ Latency: 50–200ms
→ Cost: OpenSearch compute (always running)

OpenSearch DSL:
{
  "query": { "bool": { "filter": [
    { "term": { "tenantId": "t-abc" }},
    { "term": { "level": "Fatal" }},
    { "term": { "sourceId": "payment-service" }},
    { "range": { "timestamp": { "gte": "now-2h" }}}
  ]}},
  "sort": [{ "timestamp": "desc" }],
  "size": 100
}
```

#### Example B — Yesterday's error count by service (→ Warm)
```
User query: "Count errors by service for 2026-03-22"

Time range: 2026-03-22
Age: ~26 hours; data has rolled out of OpenSearch hot window (3 days rotation)

→ Tier: WARM (Athena / DuckDB)
→ Latency: 2–10s
→ Cost: ~$0.005 per GB scanned (with partition pruning)

SQL (partition-pruned):
SELECT sourceid, COUNT(*) AS error_count
FROM logs
WHERE logtype = 'Error'
  AND year = '2026' AND month = '03' AND day = '22'
GROUP BY sourceid ORDER BY 2 DESC
```

#### Example C — 30-day P99 latency trend (→ Warm)
```
User natural language: "P99 latency trend by service, last 30 days"

→ AI translates to:
SELECT
  DATE_TRUNC('day', from_iso8601_timestamp(timestamp)) AS day,
  sourceid,
  APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.99) AS p99_ms
FROM logs
WHERE logtype='Metric' AND category='http-latency'
  AND year='2026' AND month IN ('02','03')
GROUP BY 1, 2 ORDER BY 1, 2

→ Tier: WARM (30 days well within warm window)
→ Cost estimate: "Will scan ~8.4 GB (~$0.04). Continue?"
→ Graph suggestion: LineChart (one line per service over time)
→ Latency: 5–15s
```

#### Example D — 6-month compliance audit (→ Cold)
```
User query: "Export all audit logs for Q3 2025"

Time range: 2025-07-01 → 2025-09-30
Age: ~180 days > WarmRetentionDays (90 days)

→ Tier: COLD (Athena + Glacier IR)
→ Warning returned: "Query spans cold storage (180 days old). Expected latency: 30–120s."
→ Cost estimate: "Will scan ~120 GB. Estimated cost: $0.60. Continue?"
→ Output: CSV download link to S3 results bucket
→ Latency: 45–90s
```

#### Example E — 5-day cross-tier query (→ Cross-Tier fan-out)
```
User query: "Error count by hour for the last 5 days"

HotRetentionDays = 3
Time range: now-5d → now
  → Days 0-3: Hot tier
  → Days 3-5: Warm tier

→ Tier: CrossTier

Code:
var (hotResult, warmResult) = await (
    _searchIndexer.AggregateAsync(BuildHotAggRequest(now.AddDays(-3), now), ct),
    _queryEngine.SubmitAsync(tenantId, BuildWarmSql(now.AddDays(-5), now.AddDays(-3)), ct)
).WhenAll();

// Merge by hour bucket, sort, return unified result
var merged = MergeTimeBuckets(hotResult.Buckets, warmResult.Rows)
    .OrderBy(b => b.Hour)
    .ToList();
```

### 15.3 Tier Routing Summary

| Time Range | Query Type | Tier | Expected Latency |
|-----------|-----------|------|-----------------|
| < HotRetentionDays | Any | Hot (OpenSearch) | 50–500ms |
| Any range | Full-text search | Hot (OpenSearch only) | 50–200ms |
| Hot–Warm boundary | SQL/aggregation | Warm (Athena/DuckDB) | 2–15s |
| > WarmRetentionDays | SQL/aggregation | Cold (Athena+Glacier) | 30–120s |
| Spans multiple tiers | SQL/aggregation | Cross-tier fan-out | Max of tiers |

---

## 16. Query Engine, Cost Guardrails & Replay

### 16.1 Query Cost Estimation & Guardrails

```csharp
// LightScope.QueryEngine/Services/QueryService.cs
public class QueryService(
    IQueryEngine queryEngine,
    ISearchIndexer searchIndexer,
    ISqlSafetyValidator safetyValidator,
    QueryTierRouter tierRouter,
    IMetadataStore metadataStore,
    ILogger<QueryService> logger) : IRequestHandler<ExecuteSqlQuery, QuerySubmitResult>
{
    public async Task<QuerySubmitResult> Handle(ExecuteSqlQuery cmd, CancellationToken ct)
    {
        // 1. Parse and validate SQL safety
        safetyValidator.Validate(cmd.Sql);   // throws SqlSafetyException on violation

        var parsed = SqlParser.Parse(cmd.Sql);
        var tenant = await metadataStore.GetAsync<TenantSettings>("tenants", cmd.TenantId, ct: ct);

        // 2. Enforce tenant limits
        if (tenant!.RequireTimeFilter && !parsed.HasTimeFilter)
            throw new QueryGuardException("Query must include a time range filter.");

        if (tenant.RequireLimit && !parsed.HasLimit)
            throw new QueryGuardException("Query must include a LIMIT clause.");

        // 3. Cost estimation (before execution)
        var tier = tierRouter.Route(parsed, tenant);
        QueryCostEstimate? estimate = null;

        if (tier.Tier is QueryTier.Warm or QueryTier.Cold)
        {
            estimate = await queryEngine.EstimateCostAsync(cmd.Sql, ct);

            // Block if exceeds tenant scan limit
            if (estimate.EstimatedScanGb > tenant.MaxQueryScanGb)
                throw new QueryGuardException(
                    $"Query would scan {estimate.EstimatedScanGb:F1} GB, exceeding your limit of {tenant.MaxQueryScanGb} GB. " +
                    "Add partition filters (year/month/day) to reduce scope.");

            // Require confirmation if above threshold
            if (estimate.EstimatedCostUsd > cmd.ConfirmCostIfAboveUsd)
            {
                await metadataStore.PutAsync("pending-query-confirmations", new PendingQueryConfirmation
                {
                    Token     = GenerateConfirmationToken(),
                    TenantId  = cmd.TenantId,
                    Sql       = cmd.Sql,
                    Estimate  = estimate,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
                }, ct);

                return new QuerySubmitResult
                {
                    Status   = QueryStatus.PendingCostConfirmation,
                    Estimate = estimate
                };
            }
        }

        // 4. Execute on routed tier
        return await ExecuteOnTierAsync(cmd, parsed, tier, ct);
    }
}
```

### 16.2 ISqlSafetyValidator

```csharp
// LightScope.Core/Query/SqlSafetyValidator.cs
public class SqlSafetyValidator : ISqlSafetyValidator
{
    private static readonly HashSet<string> ForbiddenKeywords =
        ["DROP", "DELETE", "INSERT", "UPDATE", "CREATE", "ALTER", "TRUNCATE", "GRANT", "REVOKE"];

    private static readonly Regex CrossJoinPattern =
        new(@"\bCROSS\s+JOIN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public void Validate(string sql)
    {
        var report = Analyze(sql);
        if (report.Errors.Count > 0)
            throw new SqlSafetyException(string.Join("; ", report.Errors));
    }

    public SqlSafetyReport Analyze(string sql)
    {
        var errors   = new List<string>();
        var warnings = new List<string>();

        var upperSql = sql.ToUpperInvariant();

        // Enforce read-only
        foreach (var keyword in ForbiddenKeywords)
            if (upperSql.Contains($" {keyword} ") || upperSql.StartsWith(keyword))
                errors.Add($"Forbidden keyword: {keyword}. Only SELECT statements are allowed.");

        // Warn on CROSS JOIN
        if (CrossJoinPattern.IsMatch(sql))
            warnings.Add("CROSS JOIN detected — may produce very large result sets.");

        // Warn if no partition filter
        if (!upperSql.Contains("YEAR") && !upperSql.Contains("MONTH") && !upperSql.Contains("DAY"))
            warnings.Add("No partition filter (year/month/day) detected — query may be expensive.");

        // Warn if no LIMIT
        if (!upperSql.Contains("LIMIT"))
            warnings.Add("No LIMIT clause — result set may be very large.");

        // Log all queries for AI observability (prompt + output tracing)
        return new SqlSafetyReport(errors, warnings);
    }
}
```

### 16.3 AI SQL Observability

Every AI-generated SQL is logged for debugging and quality monitoring:

```csharp
// LightScope.QueryEngine/AI/AiQueryAuditLogger.cs
public class AiQueryAuditLogger(IObjectStore objectStore, ILogger<AiQueryAuditLogger> logger)
{
    public async Task LogAsync(AiQueryAudit audit, CancellationToken ct)
    {
        // Store prompt + response + safety report in S3 for audit trail
        var key = $"ai-audit/{audit.TenantId}/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{audit.QueryId}.json";
        var json = JsonSerializer.Serialize(audit, JsonOptions.Default);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await objectStore.WriteAsync(key, stream, "application/json", ct);

        // Emit structured log (picked up by OpenTelemetry)
        logger.LogInformation(
            "AI query generated. TenantId={TenantId} QueryId={QueryId} InputTokens={Input} " +
            "OutputTokens={Output} SafetyErrors={Errors} SafetyWarnings={Warnings}",
            audit.TenantId, audit.QueryId,
            audit.InputTokenCount, audit.OutputTokenCount,
            audit.SafetyReport.Errors.Count, audit.SafetyReport.Warnings.Count);
    }
}

public sealed record AiQueryAudit
{
    public required string QueryId { get; init; }
    public required string TenantId { get; init; }
    public required string NaturalLanguageInput { get; init; }
    public required string SystemPrompt { get; init; }
    public required string GeneratedSql { get; init; }
    public required string Explanation { get; init; }
    public required string SuggestedGraphType { get; init; }
    public required SqlSafetyReport SafetyReport { get; init; }
    public required int InputTokenCount { get; init; }
    public required int OutputTokenCount { get; init; }
    public required string ModelUsed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

### 16.4 Replay / Reprocessing Service

```csharp
// LightScope.QueryEngine/Replay/ReplayService.cs
public class ReplayService(
    IObjectStore objectStore,
    IMessageBus messageBus,
    IMetadataStore metadataStore,
    ILogger<ReplayService> logger) : IReplayService
{
    public async Task<ReplayJob> StartAsync(
        string tenantId, DateTimeOffset from, DateTimeOffset to,
        ReplayOptions options, CancellationToken ct)
    {
        var job = new ReplayJob
        {
            JobId     = UuidV7.New().ToString(),
            TenantId  = tenantId,
            From      = from,
            To        = to,
            Options   = options,
            Status    = ReplayStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await metadataStore.PutAsync("replay-jobs", job, ct);

        // Publish to replay queue — processed async by Worker
        await messageBus.PublishAsync("lightscope-system-events",
            new ReplayStartedEvent(job.JobId, tenantId, from, to, options), ct: ct);

        return job;
    }
}

// Use cases for replay:
// 1. Parser bug fixed → re-parse and re-index all entries for a time window
// 2. New schema field added → re-process entries to populate the new field
// 3. Alert rule added retroactively → evaluate against historical data
// 4. OpenSearch index corruption → rebuild hot tier from Parquet (warm/cold source)
// 5. New tenant onboarding → re-index pre-existing S3 data from another system

// ReplayOptions:
public sealed record ReplayOptions
{
    public bool ReindexSearch { get; init; } = true;      // Rebuild OpenSearch from Parquet
    public bool ReprocessAlerts { get; init; } = false;   // Re-evaluate alert rules
    public bool ReparseFiles { get; init; } = false;      // Re-run parsers (for parser bug fixes)
    public string? OverrideParser { get; init; }          // Use a specific parser version
    public int MaxParallelFiles { get; init; } = 4;
}
```

---

## 17. Graph Support & Visualization Engine

### 17.1 Graph Type Intelligence (Rule-Based + AI)

```csharp
// LightScope.Core/Graphs/GraphSuggestionEngine.cs
public class GraphSuggestionEngine
{
    public IReadOnlyList<GraphSuggestion> SuggestFromSchema(QueryResultSchema schema)
    {
        List<GraphSuggestion> suggestions = [];

        bool hasTime      = schema.HasTimeColumn();
        bool hasCount     = schema.HasColumn("count") || schema.HasColumn("error_count") || schema.HasColumn("occurrences");
        bool hasCat       = schema.HasCategoricalColumn();
        bool hasP99       = schema.Columns.Any(c => c.Name.StartsWith("p9") || c.Name == "p99_ms");
        bool hasHourDay   = schema.HasColumns("hour_of_day", "day_of_week");
        bool isSingleRow  = schema.RowCount == 1;
        bool isSingleNum  = schema.HasSingleNumericColumn();
        bool isSmallCat   = schema.RowCount is > 1 and <= 10 && hasCat;
        bool hasCorrelation = schema.HasColumns("duration_ms", "request_bytes") ||
                              schema.HasColumns("p99_ms", "error_count");

        if (hasTime && hasCount)      suggestions.Add(new(GraphType.AreaChart,         "Error/Count Rate Over Time",      1));
        if (hasTime && !hasCount)     suggestions.Add(new(GraphType.LineChart,          "Time Series Trend",               1));
        if (hasP99)                   suggestions.Add(new(GraphType.GroupedBarChart,    "Latency Percentiles",             1));
        if (hasHourDay && hasCount)   suggestions.Add(new(GraphType.Heatmap,            "Error Density Heatmap",           1));
        if (isSingleRow && isSingleNum) suggestions.Add(new(GraphType.Gauge,           "Current Value",                   1));
        if (isSmallCat && hasCount)   suggestions.Add(new(GraphType.DonutChart,        "Distribution Breakdown",          2));
        if (hasCat && hasCount && !hasTime) suggestions.Add(new(GraphType.HorizontalBarChart, "Category Comparison",     2));
        if (hasCorrelation)           suggestions.Add(new(GraphType.ScatterPlot,        "Correlation Analysis",            2));
        if (hasTime && hasCat)        suggestions.Add(new(GraphType.StackedAreaChart,   "Stacked Over Time",               3));

        return [.. suggestions.OrderBy(s => s.Priority)];
    }
}
```

### 17.2 Prebuilt Graph Templates

The following prebuilt graphs are always available via `GET /api/v1/graphs/prebuilt`:

| Name | Type | Description |
|------|------|-------------|
| `error-rate-heatmap` | Heatmap | Error density by hour × day of week |
| `latency-p99-trend` | LineChart | P99 latency over time per service |
| `error-rate-gauge` | Gauge | Current errors/minute (last 5 min) |
| `top-errors-bar` | HorizontalBarChart | Top 10 error messages by frequency |
| `status-code-donut` | DonutChart | HTTP status code distribution |
| `service-error-scatter` | ScatterPlot | Error count vs. latency correlation |
| `log-volume-stacked` | StackedAreaChart | Log ingestion volume by log type |
| `alert-firing-timeline` | LineChart | Alert fires over time |

Each template includes: SQL (with `{TENANT_FILTER}` and `{TIME_FILTER}` tokens), Vega-Lite spec, Chart.js config, and recommended time range.

### 17.3 Graph Render Flow

```
POST /api/v1/graphs/render
{ "queryId": "qry_01HZ...", "graphType": "Auto", "options": { "theme": "dark" } }

1. Load query results + schema from metadata store
2. If graphType == "Auto":
   a. Rule-based GraphSuggestionEngine → candidate list
   b. If AI enabled: IAiService.SuggestGraphsAsync(schema, queryIntent) → enriched ranking
   c. Pick top suggestion
3. VegaLiteSpecBuilder.Build(graphType, schema, results) → JSON spec
4. ChartJsConfigBuilder.Build(graphType, schema, results) → Chart.js config
5. Return { vegaLiteSpec, chartJsConfig, suggestedType, alternativeTypes[] }
6. Client renders in-browser using Vega-Embed or Chart.js
```

---

## 18. AI-Powered Queries: GitHub Models API + Safety Layer

### 18.1 GitHub Models API Integration

```csharp
// LightScope.QueryEngine/AI/GitHubModelsAiService.cs
public class GitHubModelsAiService(
    HttpClient httpClient,
    IOptions<GitHubModelsOptions> opts,
    AiQueryAuditLogger auditLogger,
    ISqlSafetyValidator safetyValidator,
    ILogger<GitHubModelsAiService> logger) : IAiService
{
    public async Task<NlQueryResult> TranslateToSqlAsync(
        string naturalLanguage, QueryContext ctx, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(ctx);
        var queryId = UuidV7.New().ToString();

        // Call GitHub Models API (OpenAI-compatible)
        var requestBody = new
        {
            model       = opts.Value.Model,             // "openai/gpt-4o"
            messages    = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = naturalLanguage }
            },
            response_format = new { type = "json_object" },
            temperature     = 0.1,
            max_tokens      = 1500
        };

        var response = await httpClient.PostAsJsonAsync("/chat/completions", requestBody, ct);
        response.EnsureSuccessStatusCode();

        var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(ct);
        var rawJson = completion!.Choices[0].Message.Content;

        NlQueryResult result;
        try
        {
            result = JsonSerializer.Deserialize<NlQueryResult>(rawJson)!;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "AI returned non-JSON response: {Raw}", rawJson);
            throw new AiQueryException("AI returned an unparseable response. Please rephrase your query.");
        }

        // Safety validation on AI-generated SQL
        var safetyReport = safetyValidator.Analyze(result.Sql);

        // Log full audit trail (prompt + output + safety)
        await auditLogger.LogAsync(new AiQueryAudit
        {
            QueryId              = queryId,
            TenantId             = ctx.TenantId,
            NaturalLanguageInput = naturalLanguage,
            SystemPrompt         = systemPrompt,
            GeneratedSql         = result.Sql,
            Explanation          = result.Explanation,
            SuggestedGraphType   = result.SuggestedGraphType,
            SafetyReport         = safetyReport,
            InputTokenCount      = completion.Usage.PromptTokens,
            OutputTokenCount     = completion.Usage.CompletionTokens,
            ModelUsed            = opts.Value.Model
        }, ct);

        // If safety errors → throw (don't execute unsafe AI-generated SQL)
        if (safetyReport.Errors.Count > 0)
            throw new SqlSafetyException($"AI-generated SQL failed safety check: {string.Join("; ", safetyReport.Errors)}");

        return result with { SafetyWarnings = safetyReport.Warnings };
    }

    private string BuildSystemPrompt(QueryContext ctx) => $"""
        You are a SQL query generator for LightScope, a log observability platform.
        Today's date: {DateTimeOffset.UtcNow:yyyy-MM-dd}. Current UTC hour: {DateTimeOffset.UtcNow.Hour}.

        TABLE: logs
        PARTITION COLUMNS (always filter these for cost): year (string), month (string), day (string), hour (string)
        DATA COLUMNS: id, sourceId, logType, category, level, environment,
          timestamp (ISO 8601 string), ingestedAt, message, stackTrace, traceId,
          hostname, ipAddress, schemaVersion, http_method, http_path, http_statusCode,
          http_durationMs, metric_name, metric_value, metric_unit, tags (map<string,string>)

        STRICT RULES:
        1. Output ONLY valid Athena/Presto SQL — no DML/DDL.
        2. ALWAYS include partition filters: AND year='YYYY' AND month='MM' AND day='DD'.
           For ranges: AND year='2026' AND month='03' AND day >= '01' AND day <= '23'.
        3. Do NOT include tenant filter — it is injected automatically by the system.
        4. ALWAYS include LIMIT (max 10000 unless user specifies).
        5. For time aggregations use DATE_TRUNC or date_format().
        6. For percentiles use APPROX_PERCENTILE(value, 0.99).
        7. Return valid JSON only — no markdown, no code fences:
           {{"sql": "...", "explanation": "...", "suggestedGraphType": "..."}}
        8. suggestedGraphType must be one of: LineChart, BarChart, HorizontalBarChart,
           AreaChart, Heatmap, DonutChart, ScatterPlot, Gauge, StackedAreaChart, Table.

        TENANT CONTEXT:
        Environments: {string.Join(", ", ctx.Environments)}
        Known services (sourceId): {string.Join(", ", ctx.KnownSources.Take(20))}
        Log types in use: {string.Join(", ", ctx.LogTypes)}
        Hot retention: {ctx.HotRetentionDays} days  Warm retention: {ctx.WarmRetentionDays} days
        """;
}
```

### 18.2 Natural Language Query Full Flow

```
POST /api/v1/query/natural
{
  "question": "How many fatal errors per service yesterday?",
  "environment": "prod"
}

Step 1: Build QueryContext (tenant environments, known sources, retention settings)
Step 2: IAiService.TranslateToSqlAsync(question, context)
  → GitHub Models / Ollama generates JSON:
    {
      "sql": "SELECT sourceid AS service, COUNT(*) AS fatal_errors FROM logs WHERE logtype='Error' AND level='Fatal' AND environment='prod' AND year='2026' AND month='03' AND day='22' GROUP BY 1 ORDER BY 2 DESC LIMIT 50",
      "explanation": "Counting Fatal errors per service on 2026-03-22 in prod environment.",
      "suggestedGraphType": "HorizontalBarChart"
    }

Step 3: ISqlSafetyValidator.Validate(sql) → passes (SELECT, has partition filter, has LIMIT)
Step 4: QueryTierRouter.Route → yesterday in hot window → Hot tier
Step 5: ISearchIndexer.AggregateAsync(aggregation DSL) → 2 results
Step 6: GraphSuggestionEngine.SuggestFromSchema → confirms HorizontalBarChart
Step 7: VegaLiteSpecBuilder.Build → Vega-Lite spec for horizontal bar chart
Step 8: AuditLogger.LogAsync → AI audit trail saved to S3
Step 9: Return unified response

Response:
{
  "queryId": "qry_...",
  "sql": "SELECT ...",
  "explanation": "Counting Fatal errors per service on 2026-03-22 in prod environment.",
  "tier": "Hot",
  "results": {
    "columns": ["service", "fatal_errors"],
    "rows": [["payment-service", 142], ["auth-service", 37]]
  },
  "graphSpec": {
    "type": "HorizontalBarChart",
    "vegaLiteSpec": { "$schema": "...", "mark": "bar", "encoding": { ... } },
    "chartJsConfig": { "type": "bar", "options": { "indexAxis": "y", ... } }
  },
  "safetyWarnings": []
}
```

---

## 19. Authentication: API Key + JWT/Cognito Dual Strategy

### 19.1 Dual Auth — Both Schemes Active Simultaneously

```csharp
// LightScope.Api/Auth/AuthExtensions.cs
public static IServiceCollection AddLightScopeAuthentication(
    this IServiceCollection services, IConfiguration config)
{
    var authProvider = config["LightScope:Auth:Provider"]; // "Cognito" | "AzureAD" | "Local"

    services.AddAuthentication(options =>
    {
        // No single default — try both schemes
        options.DefaultAuthenticateScheme = null;
        options.DefaultChallengeScheme    = null;
    })
    .AddScheme<ApiKeyAuthSchemeOptions, ApiKeyAuthHandler>("ApiKey", _ => { })
    .AddJwtBearer("Jwt", options =>
    {
        if (authProvider == "Cognito")
        {
            var region   = config["LightScope:Auth:Cognito:Region"];
            var poolId   = config["LightScope:Auth:Cognito:UserPoolId"];
            var clientId = config["LightScope:Auth:Cognito:ClientId"];
            options.Authority = $"https://cognito-idp.{region}.amazonaws.com/{poolId}";
            options.TokenValidationParameters = new()
            {
                ValidateIssuerSigningKey = true,
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidAudience            = clientId,
                ClockSkew                = TimeSpan.FromSeconds(30)
            };
        }
        else if (authProvider == "Local")
        {
            // LocalJwtAuthHandler: validates self-signed dev tokens
            options.TokenValidationParameters = new()
            {
                ValidateIssuer           = true,
                ValidIssuer              = "lightscope-local",
                ValidateAudience         = true,
                ValidAudience            = "lightscope-dev",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(config["LightScope:Auth:LocalSecret"]!))
            };
        }
    });

    // Combined policy: accept either scheme
    services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddAuthenticationSchemes("ApiKey", "Jwt")
            .Build();
    });

    return services;
}
```

### 19.2 API Key Authentication (with Redis Cache)

```csharp
// LightScope.Api/Auth/ApiKeyAuthHandler.cs
public class ApiKeyAuthHandler(
    IOptionsMonitor<ApiKeyAuthSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITenantService tenantService,
    IConnectionMultiplexer redis) : AuthenticationHandler<ApiKeyAuthSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var rawKey) &&
            !Request.Query.TryGetValue("api_key", out rawKey))
            return AuthenticateResult.NoResult();

        var hash = HashApiKey(rawKey.ToString());

        // 1. Check Redis cache (TTL = 60s) before hitting DynamoDB/PostgreSQL
        var db = redis.GetDatabase();
        var cached = await db.StringGetAsync($"apikey:{hash}");

        TenantPrincipal? principal;
        if (cached.HasValue)
        {
            principal = JsonSerializer.Deserialize<TenantPrincipal>(cached!);
        }
        else
        {
            var tenant = await tenantService.GetByApiKeyHashAsync(hash, Context.RequestAborted);
            if (tenant is null)
                return AuthenticateResult.Fail("Invalid API key");
            if (!tenant.IsActive)
                return AuthenticateResult.Fail("API key is inactive");

            principal = new TenantPrincipal(tenant.TenantId, tenant.Plan, tenant.Name);
            // Cache for 60 seconds
            await db.StringSetAsync($"apikey:{hash}",
                JsonSerializer.Serialize(principal), TimeSpan.FromSeconds(60));
        }

        if (principal is null) return AuthenticateResult.Fail("Could not resolve tenant");

        var claims = new[]
        {
            new Claim("tenantId",  principal.TenantId),
            new Claim("plan",      principal.Plan),
            new Claim("authMethod", "ApiKey"),
            new Claim(ClaimTypes.Name, principal.Name)
        };

        var identity = new ClaimsIdentity(claims, "ApiKey");
        var ticket   = new AuthenticationTicket(new ClaimsPrincipal(identity), "ApiKey");
        return AuthenticateResult.Success(ticket);
    }

    // SHA-256 with application salt (never MD5, never unsalted)
    private string HashApiKey(string key) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(key + _options.CurrentValue.ApplicationSalt)));
}
```

---

## 20. Multi-Tenancy: Strong Isolation Model

### 20.1 Isolation Layers

| Layer | Isolation Mechanism | Strength |
|-------|-------------------|---------|
| Auth | TenantId from auth context — never from payload | Hard |
| S3 / Object Store | IAM prefix policy: `arn:aws:s3:::lightscope-logs/logs/tenant={id}/*` | Hard |
| Athena | Injected `WHERE tenant = '{id}'` + per-tenant Workgroup (optional) | Hard |
| OpenSearch | Per-tenant index: `lightscope-{tenantId}-{date}`; document-level security | Hard |
| Rate limits | Per-tenant token bucket (memory + Redis) | Hard |
| Query scan limit | `TenantSettings.MaxQueryScanGb` enforced before execution | Hard |
| Idempotency | Redis keys namespaced `idem:{tenantId}:{entryId}` | Isolation |
| Schema Registry | Per-tenant schema versions | Isolation |

### 20.2 Per-Tenant Athena Workgroup (Optional, for Cost Attribution)

```json
// CDK: Per-tenant Athena workgroup with scan limit enforcement
{
  "Name": "lightscope-tenant-acme",
  "Configuration": {
    "ResultConfiguration": {
      "OutputLocation": "s3://lightscope-athena-results/tenant=acme/"
    },
    "EnforceWorkGroupConfiguration": true,
    "BytesScannedCutoffPerQuery": 107374182400
  }
}
```

Per-tenant workgroups enable: per-tenant cost attribution in AWS Cost Explorer, independent scan limits enforced at Athena level (not just app level), and workgroup-level query history isolation.

### 20.3 Tenant Injector (Defense-in-Depth)

```csharp
// LightScope.Core/Query/TenantQueryInjector.cs
// Applied to EVERY SQL query before execution — cannot be bypassed by API callers
public class TenantQueryInjector
{
    public string Inject(string sql, string tenantId)
    {
        // Sanitize tenantId: only alphanumeric + hyphen/underscore (prevent injection)
        if (!TenantIdRegex().IsMatch(tenantId))
            throw new SecurityException($"Invalid tenantId format: {tenantId}");

        // Inject as partition predicate (performance + security)
        var injected = InjectPartitionPredicate(sql, $"tenant = '{tenantId}'");

        // Verify injection was successful (belt + suspenders)
        if (!injected.Contains($"tenant = '{tenantId}'"))
            throw new SecurityException("Failed to inject tenant filter — query blocked.");

        return injected;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]{1,64}$")]
    private static partial Regex TenantIdRegex();
}
```

---

## 21. Self-Observability: OpenTelemetry & Health Endpoints

### 21.1 OpenTelemetry Setup

```csharp
// LightScope.Api/Program.cs — OpenTelemetry registration
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("LightScope.Ingestion")
        .AddMeter("LightScope.Worker")
        .AddMeter("LightScope.Query")
        .AddPrometheusExporter())           // /metrics endpoint for Prometheus scraping
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("LightScope.*")
        .AddOtlpExporter(o =>
            o.Endpoint = new Uri(config["LightScope:Telemetry:OtlpEndpoint"]!)))
    .WithLogging(logging => logging
        .AddOtlpExporter());
```

### 21.2 Internal Metrics

```csharp
// LightScope.Worker/Telemetry/WorkerMetrics.cs
public class WorkerMetrics(IMeterFactory meterFactory)
{
    private readonly Meter _meter = meterFactory.Create("LightScope.Worker");

    // Ingestion rate (entries/sec)
    public Counter<long> IngestCounter        => _meter.CreateCounter<long>("lightscope.ingest.entries");
    public Counter<long> DuplicateCounter     => _meter.CreateCounter<long>("lightscope.ingest.duplicates");
    public Counter<long> RejectedCounter      => _meter.CreateCounter<long>("lightscope.ingest.rejected");

    // Processing latency
    public Histogram<double> ProcessingLatency =>
        _meter.CreateHistogram<double>("lightscope.worker.processing_ms",
            unit: "ms", description: "Time from ingest to Parquet write");

    // Queue lag (set from SQS ApproximateAgeOfOldestMessage)
    public ObservableGauge<long> QueueLagSeconds => _meter.CreateObservableGauge(
        "lightscope.queue.lag_seconds",
        () => _messageBus.GetQueueMetricsAsync("ls-storage-writer").GetAwaiter().GetResult().OldestMessageAgeSeconds);

    // Parquet files written
    public Counter<long> ParquetFilesWritten  => _meter.CreateCounter<long>("lightscope.parquet.files_written");
    public Counter<long> ParquetBytesWritten  => _meter.CreateCounter<long>("lightscope.parquet.bytes_written", unit: "bytes");

    // OpenSearch index rate
    public Counter<long> SearchIndexed        => _meter.CreateCounter<long>("lightscope.search.indexed");
    public Histogram<double> IndexLatency     =>
        _meter.CreateHistogram<double>("lightscope.search.index_latency_ms", unit: "ms");
}
```

### 21.3 Health Endpoints

```csharp
// LightScope.Api/Program.cs — health checks
builder.Services.AddHealthChecks()
    // Readiness: all downstream dependencies must be healthy
    .AddCheck("message-bus",    () => CheckMessageBus(),    tags: ["ready"])
    .AddCheck("object-store",   () => CheckObjectStore(),   tags: ["ready"])
    .AddCheck("search-indexer", () => CheckSearchIndexer(), tags: ["ready"])
    .AddCheck("metadata-store", () => CheckMetadataStore(), tags: ["ready"])
    .AddCheck("idempotency",    () => CheckRedis(),         tags: ["ready"])
    // Liveness: process is alive (no downstream deps)
    .AddCheck("self",           () => HealthCheckResult.Healthy("Running"), tags: ["live"]);

app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
app.MapHealthChecks("/health/live",  new() { Predicate = r => r.Tags.Contains("live") });
app.MapPrometheusScrapingEndpoint("/metrics");  // Prometheus scrapes this

// Example Prometheus metrics exposed:
// lightscope_ingest_entries_total{tenantId="t-abc", logType="Error"}
// lightscope_queue_lag_seconds{queue="ls-storage-writer"}
// lightscope_worker_processing_ms_bucket{le="100"}
// lightscope_search_indexed_total
// lightscope_parquet_bytes_written_total
```

### 21.4 Grafana Dashboard (Local Dev)

Pre-built Grafana dashboard (stored in `infra/grafana/dashboards/lightscope.json`) shows:
- Ingestion rate (entries/sec) by tenant and log type
- Queue lag in seconds (SQS approximate age)
- P99 processing latency (ingest → Parquet write)
- Duplicate entry rate (% of total)
- OpenSearch index throughput
- Error rate on worker (failed batch processing)
- AI query usage (count, input/output tokens)

---

## 22. Materialized Views & Aggregations

### 22.1 Why Materialized Views?

Ad-hoc Athena queries for dashboards are slow (2–15s) and costly. Pre-aggregating common metrics into a fast store enables sub-second dashboard refresh.

### 22.2 Precomputed Aggregations

```csharp
// LightScope.Core/MatViews/MatViewDefinitions.cs
public static class StandardMatViews
{
    // error_rate_per_minute: updated every minute from ls-matview-refresh queue
    public static MatViewDefinition ErrorRatePerMinute => new()
    {
        Name        = "error_rate_per_minute",
        Description = "Count of Error/Fatal log entries per service per minute",
        Sql = """
            SELECT
              date_trunc('minute', timestamp) AS minute_bucket,
              sourceid AS service,
              COUNT(*) AS error_count
            FROM logs
            WHERE logtype = 'Error' AND level IN ('Error', 'Fatal')
              AND {TENANT_FILTER}
              AND timestamp >= NOW() - INTERVAL '2' MINUTE
            GROUP BY 1, 2
            """,
        RefreshIntervalSeconds = 60,
        StorageTarget = MatViewStorage.Redis,        // sub-second read
        RetentionMinutes = 1440,                     // 24 hours of per-minute data
        SuggestedGraphType = GraphType.AreaChart
    };

    // latency_p99_per_service: updated every 5 minutes
    public static MatViewDefinition LatencyP99 => new()
    {
        Name        = "latency_p99_per_service",
        Description = "P50/P95/P99 latency per service per 5-minute bucket",
        Sql = """
            SELECT
              date_trunc('minute', timestamp) - INTERVAL MOD(MINUTE(timestamp), 5) MINUTE AS bucket,
              sourceid AS service,
              APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.50) AS p50_ms,
              APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.95) AS p95_ms,
              APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.99) AS p99_ms
            FROM logs
            WHERE logtype = 'Metric' AND category = 'http-latency'
              AND {TENANT_FILTER}
              AND timestamp >= NOW() - INTERVAL '10' MINUTE
            GROUP BY 1, 2
            """,
        RefreshIntervalSeconds = 300,
        StorageTarget = MatViewStorage.Redis,
        RetentionMinutes = 2880,
        SuggestedGraphType = GraphType.LineChart
    };

    // log_volume_by_type: updated every minute
    public static MatViewDefinition LogVolumeByType => new()
    {
        Name = "log_volume_by_type",
        Description = "Total log entries per log type per minute",
        // ... (similar pattern)
        RefreshIntervalSeconds = 60,
        SuggestedGraphType = GraphType.StackedAreaChart
    };
}
```

### 22.3 MatView Refresh Consumer

```csharp
// LightScope.QueryEngine/MatViews/MatViewRefreshConsumer.cs
// Triggered by ls-matview-refresh SQS queue (receives Metric entries)
public class MatViewRefreshConsumer : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var envelope in _messageBus.SubscribeAsync<LogEntryBatch>("ls-matview-refresh", ct))
        {
            // Determine which views need refresh based on entry types received
            var views = StandardMatViews.All
                .Where(v => ShouldRefresh(v, envelope.Payload))
                .ToList();

            await Parallel.ForEachAsync(views,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                async (view, innerCt) =>
                {
                    foreach (var tenantId in envelope.Payload.TenantIds)
                        await _matViewEngine.RefreshAsync(tenantId, view.Name, innerCt);
                });

            await _messageBus.AcknowledgeAsync(envelope.ReceiptHandle, ct);
        }
    }
}
```

### 22.4 Dashboard Query Routing (MatView First)

```csharp
// When a dashboard requests error_rate_per_minute:
// 1. Check IMatViewEngine: does this view exist and is it fresh?
// 2. If yes → return from Redis (< 5ms)
// 3. If no (first load, refresh lag) → fall back to Hot tier (OpenSearch agg, ~200ms)
// 4. Never fall back to Warm/Cold for dashboard queries (too slow for real-time UX)

public async Task<MatViewOrLiveResult> GetDashboardMetricAsync(
    string tenantId, string viewName, CancellationToken ct)
{
    var matView = await _matViewEngine.QueryAsync(tenantId, viewName,
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, ct);

    if (matView.IsFresh)
        return new(matView.Data, Source: "MatView");

    // Fallback to live OpenSearch aggregation
    var live = await _searchIndexer.AggregateAsync(
        BuildLiveAggRequest(tenantId, viewName), ct);

    return new(live.Buckets, Source: "Live");
}
```

---

## 23. Stream Processing Option (Kafka/Redpanda)

### 23.1 When to Consider Kafka over SNS/SQS

| Factor | SNS+SQS | Kafka/Redpanda |
|--------|---------|---------------|
| Throughput | Millions/sec | Millions/sec |
| Message retention | Up to 14 days | Configurable (days to forever) |
| Replay capability | Via DLQ only | Native, per-offset |
| Ordering | No (Standard) | Yes (per partition) |
| Real-time analytics | Limited | Kafka Streams / ksqlDB |
| Cost at scale | Pay-per-message | Fixed cluster cost |
| Operational complexity | Low | Higher |
| Best for LightScope | Production default | >10M entries/min OR when replay+ordering critical |

### 23.2 KafkaMessageBus Adapter

```csharp
// LightScope.Adapters.Kafka/KafkaMessageBus.cs
public class KafkaMessageBus(IOptions<KafkaOptions> opts) : IMessageBus
{
    // Topics mirror SQS queues: lightscope.storage-writer, lightscope.search-indexer, etc.
    // Consumer groups: each service = one consumer group (independent offset)
    // Partitions: 12 per topic (scales consumers horizontally)
    // Retention: 7 days (enables replay without S3 for recent data)
    // Compression: lz4 (fast, good ratio for JSON/Protobuf)

    public async Task PublishAsync<T>(string topic, T message,
        MessageAttributes? attributes, CancellationToken ct)
    {
        using var producer = new ProducerBuilder<string, string>(_producerConfig).Build();
        var json = JsonSerializer.Serialize(message);
        var key  = attributes?.TryGetValue("tenantId", out var tid) == true ? tid : "default";
        await producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = json }, ct);
        // Keying by tenantId ensures all entries for a tenant go to the same partition
        // → ordering guaranteed within tenant → consistent matview updates
    }
}
```

### 23.3 Enabling Kafka via Configuration

```json
// Switch from SNS/SQS to Kafka with one config change:
{
  "LightScope": {
    "Messaging": {
      "Provider": "Kafka",
      "BootstrapServers": "kafka-broker-1:9092,kafka-broker-2:9092",
      "ConsumerGroupId": "lightscope-worker",
      "SecurityProtocol": "SASL_SSL"
    }
  }
}
```

---

## 24. Security

| Concern | Implementation |
|---------|---------------|
| **Authentication** | Dual: API Key (SHA-256 + salt + Redis cache) + JWT Bearer (Cognito/Azure AD/Local) |
| **Tenant Isolation** | TenantId always from auth — never from payload; injected into every query |
| **S3 Isolation** | IAM prefix policy per tenant; `tenant={id}/` enforced by bucket policy |
| **Athena Isolation** | `TenantQueryInjector` appends partition predicate; regex validation on tenantId |
| **OpenSearch Isolation** | Per-tenant index naming; OpenSearch fine-grained access control (optional) |
| **AI SQL Safety** | `ISqlSafetyValidator` blocks DDL/DML; forbids CROSS JOIN; warns on missing partition filters |
| **AI Audit Trail** | All AI prompts + outputs + safety reports logged to immutable S3 path |
| **Rate Limiting** | Token bucket per tenant (API layer); sliding window for queries |
| **Cost Guardrails** | Scan limit per tenant; cost confirmation threshold; EXPLAIN before execute |
| **Query Read-Only** | SQL allow-list parser; only SELECT permitted; enforced before reaching query engine |
| **Idempotency** | Redis-backed, TTL-expired; prevents duplicate processing under failure |
| **Secrets** | `ISecretStore` → Secrets Manager / Key Vault / local `.env` (never in config files) |
| **Encryption at Rest** | S3 SSE-KMS; OpenSearch encryption; DynamoDB/PostgreSQL encryption |
| **Encryption in Transit** | TLS 1.3; ALB + ACM; gRPC TLS |
| **Network** | VPC-deployed; OpenSearch in private subnet; API behind ALB + WAF |
| **Audit Logs** | All API calls produce `LogType.Audit` entries through the same pipeline |
| **API Key Hashing** | SHA-256 + application salt; keys never stored in plaintext; shown once on creation |

---

## 25. Project Structure

```
LightScope/
├── src/
│   ├── LightScope.Core/                            # Domain — ZERO cloud SDK refs
│   │   ├── Models/
│   │   │   ├── LogEntry.cs
│   │   │   ├── LogEntryDto.cs                      # API input (untrusted)
│   │   │   ├── DtoMapper.cs                        # DTO → Domain (enriches TenantId+Id)
│   │   │   ├── HttpLogContext.cs
│   │   │   ├── MetricPayload.cs
│   │   │   ├── TenantSettings.cs                   # Per-tenant retention + limits (NEW)
│   │   │   ├── PullJobConfig.cs
│   │   │   ├── PullJobState.cs
│   │   │   ├── AlertRule.cs
│   │   │   ├── GraphSuggestion.cs
│   │   │   ├── SavedQuery.cs
│   │   │   ├── MatViewDefinition.cs                # Materialized view spec (NEW)
│   │   │   └── ReplayJob.cs                        # Replay job model (NEW)
│   │   ├── Interfaces/
│   │   │   ├── Storage/IObjectStore.cs
│   │   │   ├── Search/ISearchIndexer.cs
│   │   │   ├── Query/IQueryEngine.cs
│   │   │   ├── Messaging/IMessageBus.cs
│   │   │   ├── Metadata/IMetadataStore.cs
│   │   │   ├── Secrets/ISecretStore.cs
│   │   │   ├── Scheduling/IScheduler.cs
│   │   │   ├── Connectors/IPullConnector.cs
│   │   │   ├── AI/IAiService.cs
│   │   │   ├── Schema/ISchemaRegistry.cs           # NEW
│   │   │   ├── Idempotency/IIdempotencyStore.cs    # NEW
│   │   │   ├── Replay/IReplayService.cs            # NEW
│   │   │   ├── MatViews/IMatViewEngine.cs          # NEW
│   │   │   └── Query/ISqlSafetyValidator.cs        # NEW
│   │   ├── Commands/                               # MediatR commands & handlers
│   │   │   ├── IngestLogsCommand.cs
│   │   │   ├── IngestLogsHandler.cs
│   │   │   ├── StartReplayCommand.cs
│   │   │   └── StartReplayHandler.cs
│   │   ├── Queries/                                # MediatR queries & handlers
│   │   │   ├── ExecuteSqlQuery.cs
│   │   │   ├── ExecuteSqlQueryHandler.cs
│   │   │   ├── GetNaturalLanguageQuery.cs
│   │   │   └── GetNaturalLanguageQueryHandler.cs
│   │   ├── Schema/
│   │   │   ├── SchemaVersion.cs
│   │   │   ├── SchemaDefinition.cs
│   │   │   ├── SchemaCompatibilityResult.cs
│   │   │   └── SchemaInferenceEngine.cs            # Auto-detect new tag keys
│   │   ├── Parsers/
│   │   │   ├── ILogFileParser.cs
│   │   │   ├── NdJsonParser.cs
│   │   │   ├── W3CParser.cs
│   │   │   ├── ClfParser.cs
│   │   │   ├── SyslogParser.cs
│   │   │   ├── VpcFlowParser.cs
│   │   │   ├── CloudTrailParser.cs
│   │   │   └── GzipParserDecorator.cs
│   │   ├── Routing/
│   │   │   ├── QueryTierRouter.cs
│   │   │   └── TenantQueryInjector.cs
│   │   ├── Query/
│   │   │   └── SqlSafetyValidator.cs
│   │   ├── Graphs/
│   │   │   ├── GraphSuggestionEngine.cs
│   │   │   ├── VegaLiteSpecBuilder.cs
│   │   │   └── ChartJsConfigBuilder.cs
│   │   ├── MatViews/
│   │   │   └── MatViewDefinitions.cs               # Prebuilt view specs
│   │   ├── Storage/
│   │   │   ├── IParquetWriter.cs
│   │   │   └── S3PathBuilder.cs
│   │   ├── Resilience/
│   │   │   └── ResiliencePipelines.cs              # Polly retry/circuit-breaker policies
│   │   └── Validation/
│   │       └── LogEntryDtoValidator.cs             # FluentValidation
│   │
│   ├── LightScope.Api/                             # ASP.NET Core 10 Minimal API
│   │   ├── Endpoints/
│   │   │   ├── LogEndpoints.cs
│   │   │   ├── QueryEndpoints.cs
│   │   │   ├── GraphEndpoints.cs
│   │   │   ├── PullJobEndpoints.cs
│   │   │   ├── AlertEndpoints.cs
│   │   │   ├── AuthEndpoints.cs
│   │   │   ├── ReplayEndpoints.cs
│   │   │   └── HealthEndpoints.cs
│   │   ├── Grpc/
│   │   │   └── LogIngestionGrpcService.cs
│   │   ├── Auth/
│   │   │   ├── ApiKeyAuthHandler.cs
│   │   │   ├── ApiKeyAuthSchemeOptions.cs
│   │   │   └── TenantContextMiddleware.cs
│   │   ├── RateLimiting/
│   │   │   └── TenantRateLimiterExtensions.cs
│   │   ├── Extensions/
│   │   │   ├── ProviderExtensions.cs
│   │   │   └── AuthExtensions.cs
│   │   ├── protos/
│   │   │   └── log_ingestion.proto
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── LightScope.Worker/                          # .NET 10 Background Worker
│   │   ├── Workers/
│   │   │   ├── StorageWriterWorker.cs
│   │   │   └── SearchIndexerWorker.cs
│   │   ├── Services/
│   │   │   ├── LogNormalizerService.cs
│   │   │   ├── ParquetWriterService.cs
│   │   │   └── PartitionKeyBuilder.cs
│   │   ├── Telemetry/
│   │   │   └── WorkerMetrics.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── LightScope.Puller/                          # Pull job worker
│   │   ├── Connectors/
│   │   │   ├── AwsS3PullConnector.cs
│   │   │   ├── AzureBlobPullConnector.cs
│   │   │   ├── CloudWatchPullConnector.cs
│   │   │   └── HttpPullConnector.cs
│   │   ├── Scheduling/
│   │   │   ├── PullJobScheduler.cs
│   │   │   └── PullJobQuartzJob.cs
│   │   ├── Services/
│   │   │   └── PullJobStateService.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── LightScope.QueryEngine/                     # Query, AI, Graph, Alerts, MatViews
│   │   ├── Services/
│   │   │   ├── QueryService.cs
│   │   │   ├── TierRoutingService.cs
│   │   │   └── ReportSchedulerService.cs
│   │   ├── AI/
│   │   │   ├── GitHubModelsAiService.cs
│   │   │   ├── OllamaAiService.cs
│   │   │   ├── NoOpAiService.cs
│   │   │   ├── AiQueryAuditLogger.cs
│   │   │   └── GraphSuggestionEnricher.cs
│   │   ├── Alerts/
│   │   │   └── AlertEvaluationConsumer.cs
│   │   ├── Graphs/
│   │   │   ├── GraphRenderService.cs
│   │   │   └── Templates/PrebuiltGraphs.cs
│   │   ├── MatViews/
│   │   │   └── MatViewRefreshConsumer.cs
│   │   ├── Replay/
│   │   │   └── ReplayService.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   └── LightScope.Adapters/                        # All cloud + local adapters
│       ├── Aws/
│       │   ├── S3ObjectStore.cs
│       │   ├── AwsSnsMessageBus.cs
│       │   ├── AwsSqsSubscriber.cs
│       │   ├── DynamoMetadataStore.cs
│       │   ├── DynamoSchemaRegistry.cs
│       │   ├── AthenaQueryEngine.cs
│       │   ├── OpenSearchIndexer.cs
│       │   ├── ElastiCacheIdempotencyStore.cs
│       │   ├── GlueSchemaRegistry.cs
│       │   ├── SecretsManagerSecretStore.cs
│       │   └── EventBridgeScheduler.cs
│       ├── Azure/
│       │   ├── AzureBlobObjectStore.cs
│       │   ├── ServiceBusMessageBus.cs
│       │   ├── CosmosMetadataStore.cs
│       │   └── SynapseQueryEngine.cs
│       ├── Gcp/
│       │   ├── GcsObjectStore.cs
│       │   ├── PubSubMessageBus.cs
│       │   └── BigQueryQueryEngine.cs
│       ├── Kafka/
│       │   └── KafkaMessageBus.cs
│       └── Local/
│           ├── MinIOObjectStore.cs
│           ├── RabbitMqMessageBus.cs
│           ├── PostgresMetadataStore.cs
│           ├── PostgresSchemaRegistry.cs
│           ├── DuckDbQueryEngine.cs
│           ├── MeilisearchIndexer.cs
│           ├── RedisIdempotencyStore.cs
│           ├── RedisMatViewEngine.cs
│           ├── QuartzScheduler.cs
│           ├── LocalSecretStore.cs
│           ├── LocalJwtAuthHandler.cs
│           └── OllamaAiService.cs
│
├── infra/
│   ├── cdk/                                        # AWS CDK (C#)
│   │   ├── LightScopeCdkApp.cs
│   │   └── Stacks/
│   │       ├── StorageStack.cs         # S3 + lifecycle + Glue
│   │       ├── MessagingStack.cs       # 2 SNS + 8 SQS + 8 DLQs
│   │       ├── SearchStack.cs          # OpenSearch + ILM policy
│   │       ├── DatabaseStack.cs        # DynamoDB tables
│   │       ├── CacheStack.cs           # ElastiCache Redis
│   │       ├── ComputeStack.cs         # ECS/EKS + ECR
│   │       ├── AuthStack.cs            # Cognito User Pool
│   │       └── NetworkStack.cs         # VPC + ALB + WAF
│   ├── prometheus.yml                  # Prometheus scrape config
│   ├── grafana/
│   │   └── dashboards/lightscope.json  # Pre-built Grafana dashboard
│   ├── opensearch/
│   │   ├── ilm-policy.json
│   │   └── index-template.json
│   └── scripts/
│       ├── local-setup.sh
│       └── schema.sql
│
├── tests/
│   ├── LightScope.Core.Tests/
│   │   ├── Parsers/
│   │   ├── Routing/QueryTierRouterTests.cs
│   │   ├── Schema/SchemaRegistryTests.cs
│   │   ├── Schema/SchemaEvolutionTests.cs
│   │   ├── Idempotency/IdempotencyStoreTests.cs
│   │   ├── Graphs/GraphSuggestionEngineTests.cs
│   │   ├── Query/SqlSafetyValidatorTests.cs
│   │   └── Validation/LogEntryValidatorTests.cs
│   ├── LightScope.Api.Tests/
│   │   ├── Auth/ApiKeyAuthHandlerTests.cs
│   │   ├── Auth/JwtAuthTests.cs
│   │   ├── RateLimiting/TenantRateLimiterTests.cs
│   │   └── Endpoints/
│   ├── LightScope.Worker.Tests/
│   │   ├── StorageWriterWorkerTests.cs
│   │   ├── IdempotencyIntegrationTests.cs
│   │   └── LogNormalizerTests.cs
│   ├── LightScope.Puller.Tests/
│   └── LightScope.Integration.Tests/
│       ├── LocalStackIntegrationTests.cs
│       └── DuckDbQueryEngineTests.cs
│
├── docker/
│   ├── Dockerfile.api
│   ├── Dockerfile.worker
│   ├── Dockerfile.puller
│   ├── Dockerfile.queryengine
│   └── docker-compose.yml
│
├── docs/
│   ├── README.md
│   ├── architecture.md
│   ├── api-reference.md
│   ├── configuration.md
│   ├── local-development.md
│   ├── query-guide.md
│   ├── graph-guide.md
│   ├── connectors.md
│   ├── security.md
│   ├── schema-evolution.md             # NEW
│   ├── idempotency.md                  # NEW
│   ├── replay-guide.md                 # NEW
│   ├── materialized-views.md           # NEW
│   └── runbooks/
│       ├── scaling.md
│       ├── incident-response.md
│       ├── data-retention.md
│       ├── adding-a-connector.md
│       └── adding-a-cloud-provider.md
│
├── global.json                         # .NET 10 SDK pin
├── LightScope.sln
└── README.md
```

---

## 26. Configuration & Deployment

### 26.1 global.json (.NET 10 SDK Pin)

```json
{
  "sdk": {
    "version": "10.0.0",
    "rollForward": "latestMinor"
  }
}
```

### 26.2 appsettings.json (AWS Provider — Production)

```json
{
  "LightScope": {
    "Provider": "AWS",
    "Auth": {
      "Provider": "Cognito",
      "ApiKey": { "ApplicationSalt": "CHANGE_ME_IN_SECRETS", "CacheTtlSeconds": 60 },
      "Cognito": {
        "UserPoolId": "us-east-1_XXXXXXXX",
        "ClientId":   "YYYYYYYYYYYYYYYY",
        "Region":     "us-east-1"
      }
    },
    "Messaging": {
      "Provider":        "AwsSns",
      "IngestTopicArn":  "arn:aws:sns:us-east-1:123456789012:lightscope-ingest",
      "SystemTopicArn":  "arn:aws:sns:us-east-1:123456789012:lightscope-system-events",
      "Queues": {
        "StorageWriter":  "https://sqs.us-east-1.amazonaws.com/123456789012/ls-storage-writer",
        "SearchIndexer":  "https://sqs.us-east-1.amazonaws.com/123456789012/ls-search-indexer",
        "AlertEvaluator": "https://sqs.us-east-1.amazonaws.com/123456789012/ls-alert-evaluator",
        "MatViewRefresh": "https://sqs.us-east-1.amazonaws.com/123456789012/ls-matview-refresh",
        "ReplayEvents":   "https://sqs.us-east-1.amazonaws.com/123456789012/ls-replay-events"
      }
    },
    "Storage": {
      "Provider":       "S3",
      "LogsBucket":     "lightscope-logs-123456789012",
      "ResultsBucket":  "lightscope-athena-results-123456789012"
    },
    "Search": {
      "Provider":           "OpenSearch",
      "Endpoint":           "https://search-lightscope.us-east-1.es.amazonaws.com",
      "IndexPrefix":        "lightscope",
      "DefaultShards":      2,
      "DefaultReplicas":    1,
      "IlmPolicyName":      "lightscope-ilm-policy"
    },
    "Metadata": {
      "Provider": "DynamoDB",
      "Region":   "us-east-1"
    },
    "Idempotency": {
      "Provider":           "ElastiCache",
      "ConnectionString":   "lightscope-redis.xxxxx.ng.0001.use1.cache.amazonaws.com:6379"
    },
    "SchemaRegistry": {
      "Provider":           "DynamoDB",
      "InferenceMode":      "Observe"
    },
    "Query": {
      "Provider":     "Athena",
      "Database":     "lightscope",
      "WorkGroup":    "lightscope-workgroup",
      "DefaultScanLimitGb": 10,
      "CostConfirmationThresholdUsd": 0.05
    },
    "AI": {
      "Provider":         "GitHubModels",
      "Endpoint":         "https://models.github.ai/inference",
      "Model":            "openai/gpt-4o",
      "TokenSecretName":  "lightscope/github-models-token",
      "AuditBucketPrefix": "ai-audit/"
    },
    "Ingestion": {
      "MaxBatchSize":        1000,
      "MaxPayloadSizeBytes": 4194304,
      "WorkerConsumerCount": 4,
      "WorkerBatchSize":     100,
      "FlushIntervalSeconds":5,
      "FlushBatchSize":      1000
    },
    "Telemetry": {
      "OtlpEndpoint": "http://otel-collector:4317",
      "ServiceName":  "lightscope-api",
      "Environment":  "prod"
    }
  }
}
```

### 26.3 Key NuGet Packages (.NET 10)

```xml
<!-- LightScope.Core — zero cloud deps -->
<PackageReference Include="MediatR"                   Version="12.*" />
<PackageReference Include="FluentValidation"           Version="11.*" />
<PackageReference Include="Parquet.Net"                Version="5.*" />
<PackageReference Include="Polly"                      Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Options" Version="10.*" />

<!-- LightScope.Api -->
<PackageReference Include="Grpc.AspNetCore"            Version="2.*" />
<PackageReference Include="MediatR.Extensions.Microsoft.DependencyInjection" Version="12.*" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
<PackageReference Include="Serilog.AspNetCore"         Version="8.*" />
<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.*" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />

<!-- LightScope.Adapters.Aws -->
<PackageReference Include="AWSSDK.S3"                  Version="3.*" />
<PackageReference Include="AWSSDK.SQS"                 Version="3.*" />
<PackageReference Include="AWSSDK.SimpleNotificationService" Version="3.*" />
<PackageReference Include="AWSSDK.DynamoDBv2"          Version="3.*" />
<PackageReference Include="AWSSDK.Athena"              Version="3.*" />
<PackageReference Include="AWSSDK.ElastiCache"         Version="3.*" />
<PackageReference Include="AWSSDK.SecretsManager"      Version="3.*" />
<PackageReference Include="OpenSearch.Net"             Version="1.*" />
<PackageReference Include="OpenSearch.Client"          Version="1.*" />

<!-- LightScope.Adapters.Kafka -->
<PackageReference Include="Confluent.Kafka"            Version="2.*" />

<!-- LightScope.Adapters.Local -->
<PackageReference Include="DuckDB.NET.Data.Full"       Version="1.*" />
<PackageReference Include="RabbitMQ.Client"            Version="7.*" />
<PackageReference Include="Meilisearch"                Version="0.*" />
<PackageReference Include="Npgsql"                     Version="9.*" />
<PackageReference Include="Dapper"                     Version="2.*" />
<PackageReference Include="Quartz"                     Version="3.*" />
<PackageReference Include="StackExchange.Redis"        Version="2.*" />

<!-- Tests -->
<PackageReference Include="xunit"                      Version="2.*" />
<PackageReference Include="Moq"                        Version="4.*" />
<PackageReference Include="Testcontainers"             Version="3.*" />
<PackageReference Include="Testcontainers.PostgreSql"  Version="3.*" />
<PackageReference Include="Testcontainers.Redis"       Version="3.*" />
<PackageReference Include="FluentAssertions"           Version="7.*" />

<!-- Infrastructure -->
<PackageReference Include="Amazon.CDK.Lib"             Version="2.*" />
```

### 26.4 Dockerfiles (.NET 10)

```dockerfile
# docker/Dockerfile.api
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/LightScope.Api/LightScope.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "LightScope.Api.dll"]
```

---

## 27. Coding Agent Rules & Conventions

> **This section is the primary instruction set for the AI Coding Agent generating this solution. Follow every rule strictly.**

### 27.1 Technology Stack (Mandatory)

| Concern | Technology | Version |
|---------|-----------|---------|
| Runtime | .NET 10 | `net10.0` |
| Language | C# 14 | Primary constructors, `required`, `record`, collection expressions |
| API style | ASP.NET Core 10 Minimal APIs | No controllers — use endpoint route groups |
| Command/Query | MediatR 12 | All business logic via `IRequest<T>` / `IRequestHandler<T>` |
| Validation | FluentValidation 11 | All DTOs validated before MediatR dispatch |
| Resilience | Polly 8 | All external I/O calls wrapped in `ResiliencePipeline<T>` |
| Logging | Serilog | Structured; sink: Console + OpenTelemetry |
| Telemetry | OpenTelemetry | Traces + Metrics + Logs; Prometheus exporter |
| Messaging | MediatR (in-process) + IMessageBus (external) | Never call IMessageBus directly from controllers |
| Testing | xUnit + Moq + Testcontainers + FluentAssertions | Integration tests use real Docker containers |
| Infrastructure | AWS CDK v2 (C#) | One stack file per resource group |

### 27.2 Naming Conventions

| Artifact | Convention | Example |
|---------|-----------|---------|
| Interfaces | `I` prefix, PascalCase | `IObjectStore`, `ISchemaRegistry` |
| Commands (writes) | `{Verb}{Noun}Command` | `IngestLogsCommand`, `StartReplayCommand` |
| Queries (reads) | `Get{Noun}Query` or `Execute{Noun}Query` | `ExecuteSqlQuery`, `GetNaturalLanguageQuery` |
| Handlers | `{CommandOrQuery}Handler` | `IngestLogsHandler` |
| Events | `{Noun}{PastVerb}Event` | `ReplayStartedEvent`, `AlertFiredEvent` |
| DTOs | `{Domain}Dto` | `LogEntryDto`, `PullJobConfigDto` |
| Options | `{Feature}Options` | `WorkerOptions`, `GitHubModelsOptions` |
| Adapters | `{Provider}{Interface}` | `S3ObjectStore`, `DuckDbQueryEngine` |
| Tests | `{ClassUnderTest}Tests` | `QueryTierRouterTests` |
| Constants | `static readonly` or `const` | `SchemaVersions.Current = 1u` |

### 27.3 DTO vs Domain Rules

```
RULE: DTOs exist in LightScope.Core/Models/*Dto.cs
RULE: Domain objects exist in LightScope.Core/Models/*.cs
RULE: DtoMapper.ToDomain() is the ONLY place where:
  - Id is generated (UUIDv7) — NEVER use DTO's Id field
  - TenantId is set — ALWAYS from auth context, never from DTO
  - IngestedAt is set — ALWAYS DateTimeOffset.UtcNow
  - IngestionMode is set — from calling context (Push/Pull/Replay)
RULE: No cloud SDK types in DTOs or domain models
RULE: DTOs use JsonPropertyName attributes for API serialization
RULE: Domain models use records with init-only properties
```

### 27.4 Error Handling Pattern

```csharp
// Use custom exception hierarchy — not generic Exception
public abstract class LightScopeException(string message) : Exception(message);
public class ValidationException(string message)         : LightScopeException(message);
public class SqlSafetyException(string message)          : LightScopeException(message);
public class QueryGuardException(string message)         : LightScopeException(message);
public class TenantNotFoundException(string tenantId)    : LightScopeException($"Tenant {tenantId} not found");
public class SchemaIncompatibleException(string message) : LightScopeException(message);
public class AiQueryException(string message)            : LightScopeException(message);
public class ReplayException(string message)             : LightScopeException(message);

// Global exception handler in Program.cs:
app.UseExceptionHandler(exApp =>
    exApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, code) = ex switch
        {
            ValidationException     => (400, "VALIDATION_ERROR"),
            SqlSafetyException      => (400, "SQL_SAFETY_ERROR"),
            QueryGuardException     => (400, "QUERY_GUARD_ERROR"),
            TenantNotFoundException => (404, "TENANT_NOT_FOUND"),
            AiQueryException        => (422, "AI_QUERY_ERROR"),
            UnauthorizedAccessException => (401, "UNAUTHORIZED"),
            _                       => (500, "INTERNAL_ERROR")
        };
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { error = ex?.Message, code });
    }));
```

### 27.5 All Interfaces Must Have 1+ Implementation (Local Provider First)

```
RULE: Before implementing any AWS/Azure/GCP adapter, implement the Local adapter first.
RULE: Integration tests run against Local adapters (no cloud account needed).
RULE: If an interface has no local implementation, generate a NoOp stub that logs a warning.
RULE: Cloud adapters live in LightScope.Adapters.{Provider}/ — never in Core or service projects.
```

### 27.6 No Cloud SDKs in Core

```
RULE: LightScope.Core.csproj must reference ONLY:
  - MediatR
  - FluentValidation
  - Parquet.Net
  - Polly
  - Microsoft.Extensions.* (Options, Logging, DI abstractions)
  - System.* (BCL only)
  
VIOLATION: Any reference to AWSSDK.*, Azure.*, Google.Cloud.* in Core is a build error.
Enforce via Directory.Build.props WarningsAsErrors for banned package references.
```

### 27.7 Async & Cancellation Rules

```
RULE: Every async method must accept CancellationToken as its last parameter.
RULE: Never use .Result or .Wait() — always await.
RULE: Use IAsyncEnumerable<T> for streaming (message bus, file parsing).
RULE: Use Channel<T> for producer-consumer pipelines within a process.
RULE: Use Parallel.ForEachAsync for CPU-parallel async operations.
RULE: MaxDegreeOfParallelism must always be explicitly set (never unbounded).
RULE: Use ValueTask for hot-path async methods (idempotency store checks).
```

### 27.8 Serilog Structured Logging

```csharp
// RULE: Use Serilog message templates with named properties (not string interpolation)
// CORRECT:
_logger.LogInformation("Flushed {Count} entries to {S3Key} in {ElapsedMs}ms",
    batch.Count, s3Key, elapsed.TotalMilliseconds);

// WRONG (loses structured data):
_logger.LogInformation($"Flushed {batch.Count} entries to {s3Key}");

// RULE: Use LogContext.PushProperty for per-request correlation
using (LogContext.PushProperty("TenantId", tenantId))
using (LogContext.PushProperty("RequestId", requestId))
{
    // All log statements within this scope include TenantId + RequestId
}

// RULE: Log at appropriate levels
// Trace: per-entry processing details (disabled in prod)
// Debug: batch-level decisions
// Information: file writes, queue acks, query completions
// Warning: duplicates, schema warnings, cost warnings, retries
// Error: processing failures, AI safety violations, unhandled exceptions (with stack trace)
// Fatal: startup failures, data corruption detected
```

### 27.9 FluentValidation Rules

```csharp
// LightScope.Core/Validation/LogEntryDtoValidator.cs
public class LogEntryDtoValidator : AbstractValidator<LogEntryDto>
{
    public LogEntryDtoValidator()
    {
        RuleFor(x => x.SourceId)
            .NotEmpty()
            .MaximumLength(256)
            .Matches(@"^[a-zA-Z0-9\-_.:/]+$")
            .WithMessage("SourceId must contain only alphanumeric characters, hyphens, underscores, dots, colons, slashes.");

        RuleFor(x => x.LogType)
            .NotEmpty()
            .Must(v => Enum.TryParse<LogType>(v, ignoreCase: true, out _))
            .WithMessage("LogType must be one of: Application, Error, Network, OS, Metric, Audit, Custom.");

        RuleFor(x => x.Level)
            .NotEmpty()
            .Must(v => Enum.TryParse<LogLevel>(v, ignoreCase: true, out _));

        RuleFor(x => x.Environment)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.Timestamp)
            .NotEmpty()
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddMinutes(5))
            .WithMessage("Timestamp cannot be more than 5 minutes in the future.")
            .GreaterThan(_ => DateTimeOffset.UtcNow.AddDays(-30))
            .WithMessage("Timestamp cannot be more than 30 days in the past.");

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(65536);

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 50)
            .WithMessage("Maximum 50 tags per entry.")
            .Must(tags => tags == null || tags.Keys.All(k => k.Length <= 128))
            .WithMessage("Tag keys must be ≤ 128 characters.")
            .Must(tags => tags == null || tags.Values.All(v => v.Length <= 1024))
            .WithMessage("Tag values must be ≤ 1024 characters.");

        When(x => x.LogType?.Equals("Metric", StringComparison.OrdinalIgnoreCase) == true, () =>
        {
            RuleFor(x => x.Metric).NotNull().WithMessage("Metric entries must include a Metric payload.");
            RuleFor(x => x.Metric!.MetricName).NotEmpty().MaximumLength(256);
            RuleFor(x => x.Metric!.Unit).NotEmpty();
        });
    }
}
```

### 27.10 Test Requirements

```
RULE: Every interface must have a corresponding unit test file.
RULE: SqlSafetyValidator must have tests for: DROP, DELETE, INSERT, CREATE, ALTER,
      CROSS JOIN, missing partition filter, missing LIMIT, valid SELECT.
RULE: QueryTierRouter must have tests for each routing rule (Hot, Warm, Cold, CrossTier).
RULE: DtoMapper.ToDomain must have tests verifying TenantId+Id+IngestedAt are always system-set.
RULE: ApiKeyAuthHandler must have tests: valid key (cache hit), valid key (DB hit),
      invalid key (fail), inactive key (fail), missing header (no result).
RULE: Integration tests use Testcontainers (real PostgreSQL, real Redis, real MinIO).
RULE: All tests must pass with `dotnet test` against the docker-compose local stack.
RULE: Use FluentAssertions for readable assertions: result.Should().Be(expected).
```

---

## 28. Implementation Phases

```
Phase 1:  LightScope.Core
          - All models (LogEntry, LogEntryDto, TenantSettings, etc.)
          - All interfaces (IObjectStore, ISearchIndexer, IQueryEngine, IMessageBus,
            IMetadataStore, ISchemaRegistry, IIdempotencyStore, IReplayService,
            IMatViewEngine, IAiService, ISqlSafetyValidator, ISecretStore, IScheduler)
          - DtoMapper, MediatR commands + handlers (stubs)
          - FluentValidation: LogEntryDtoValidator
          - SqlSafetyValidator
          - QueryTierRouter
          - TenantQueryInjector
          - S3PathBuilder
          - ResiliencePipelines (Polly)
          - GraphSuggestionEngine (rule-based)
          - SchemaInferenceEngine
          - MatViewDefinitions

Phase 2:  LightScope.Adapters.Local (ALL local adapters — required before any service)
          - MinIOObjectStore
          - RabbitMqMessageBus
          - PostgresMetadataStore + PostgresSchemaRegistry
          - DuckDbQueryEngine (with MinIO S3-compatible reads)
          - RedisIdempotencyStore
          - RedisMatViewEngine
          - MeilisearchIndexer
          - LocalSecretStore
          - LocalJwtAuthHandler
          - OllamaAiService
          - QuartzScheduler
          - InProcessChannelMessageBus (for unit tests)

Phase 3:  docker-compose.yml + Dockerfiles + scripts/local-setup.sh
          - All services runnable locally with `docker-compose up`
          - scripts/local-setup.sh bootstraps MinIO buckets + PostgreSQL schema + Ollama model

Phase 4:  LightScope.Api
          - Minimal API endpoints (all routes from Section 10.1)
          - Dual auth: ApiKeyAuthHandler + JwtBearer
          - TenantContextMiddleware
          - TenantRateLimiterExtensions (token bucket + sliding window)
          - MediatR dispatch pipeline
          - gRPC: LogIngestionGrpcService (unary + client streaming + bidirectional)
          - OpenTelemetry + Serilog
          - Health endpoints: /health/ready + /health/live + /metrics
          - Global exception handler
          - Payload size enforcement middleware

Phase 5:  LightScope.Worker
          - StorageWriterWorker (bounded Channel, N consumers, Parquet flush)
          - SearchIndexerWorker (OpenSearch bulk indexer)
          - IdempotencyStore integration (Redis check before processing)
          - WorkerMetrics (OpenTelemetry meters)
          - Polly retry on all external calls

Phase 6:  LightScope.Puller
          - IPullConnector factory (named services)
          - AwsS3PullConnector (with state tracking)
          - AzureBlobPullConnector
          - CloudWatchPullConnector
          - HttpPullConnector
          - PullJobScheduler (Quartz.NET)
          - PullJobStateService

Phase 7:  LightScope.QueryEngine — Core Query
          - QueryService with full tier routing
          - Cost estimation + user confirmation flow
          - ISqlSafetyValidator integration
          - Athena adapter (AwsProvider) + DuckDB adapter (LocalProvider)
          - Cross-tier fan-out (Task.WhenAll + merge)
          - Saved queries CRUD
          - Scheduled reports

Phase 8:  LightScope.QueryEngine — AI & Graphs
          - GitHubModelsAiService (NL→SQL)
          - AiQueryAuditLogger
          - OllamaAiService (local fallback)
          - NoOpAiService (disabled mode)
          - GraphSuggestionEngine integration (AI-enriched)
          - VegaLiteSpecBuilder (all 9 graph types)
          - ChartJsConfigBuilder (all 9 graph types)
          - PrebuiltGraphs templates (all 8 prebuilt graphs)
          - GraphRenderService

Phase 9:  LightScope.QueryEngine — Alerts, MatViews, Replay
          - AlertEvaluationConsumer
          - MatViewRefreshConsumer
          - MatViewRefreshService (per-tenant, Redis storage)
          - ReplayService + ReplayWorker
          - ISchemaRegistry integration

Phase 10: LightScope.Adapters.Aws (all AWS adapters)
          - S3ObjectStore, AwsSnsMessageBus, AwsSqsSubscriber
          - DynamoMetadataStore, DynamoSchemaRegistry
          - AthenaQueryEngine (with Glue catalog integration)
          - OpenSearchIndexer (with ILM policy setup on startup)
          - ElastiCacheIdempotencyStore
          - GlueSchemaRegistry
          - SecretsManagerSecretStore
          - EventBridgeScheduler

Phase 11: CDK Infrastructure Stack
          - StorageStack: S3 (2 buckets) + Glue + lifecycle rules
          - MessagingStack: 2 SNS + 8 SQS + 8 DLQs + subscription filter policies
          - SearchStack: OpenSearch domain + ILM policy + index template
          - DatabaseStack: DynamoDB tables (tenants, pull-jobs, saved-queries, alert-rules,
            replay-jobs, query-executions, schema-versions)
          - CacheStack: ElastiCache Redis (idempotency + matviews + API key cache)
          - AuthStack: Cognito User Pool + pre-token-generation Lambda (inject tenantId claim)
          - NetworkStack: VPC + private subnets + ALB + WAF + TLS
          - ComputeStack: ECS cluster + 4 task definitions (api, worker, puller, queryengine)

Phase 12: Full Test Suite
          - Unit tests: all Core components (SqlSafetyValidator, QueryTierRouter, DtoMapper,
            LogEntryDtoValidator, GraphSuggestionEngine, S3PathBuilder, SchemaInferenceEngine)
          - Auth tests: API key (cache hit/miss/invalid/inactive), JWT decode
          - Rate limiter tests: token bucket exhaustion, refill, per-tenant isolation
          - Integration tests (Testcontainers): PostgreSQL, Redis, MinIO, OpenSearch, DuckDB
          - End-to-end: docker-compose stack (ingest → SQS → Worker → MinIO → DuckDB query)

Phase 13: Azure + GCP Adapters (optional but recommended)
          - LightScope.Adapters.Azure: BlobObjectStore, ServiceBusMessageBus, CosmosMetadataStore,
            SynapseQueryEngine
          - LightScope.Adapters.Gcp: GcsObjectStore, PubSubMessageBus, BigQueryQueryEngine
          - LightScope.Adapters.Kafka: KafkaMessageBus

Phase 14: Full Documentation (docs/ directory)
          See Section 29 for complete specification.
```

---

## 29. Full Markdown Documentation (Phase 14)

> **Coding Agent instruction:** Generate ALL files listed below under the `docs/` directory as the final implementation phase. Every file must be complete, developer-facing, include working examples, and follow the content spec below.

### 29.1 docs/README.md

**Required content:**
- LightScope summary (2 paragraphs)
- Feature bullet list (all major features from Section 6)
- Quick Start (local docker-compose, 5 steps max)
- Architecture diagram link
- Provider compatibility table (from Section 4.3)
- Links to all other docs

### 29.2 docs/architecture.md

**Required content:**
- Full ASCII architecture diagram (from Section 1)
- Hexagonal architecture explanation with diagram (Section 4.1)
- Service responsibilities table (Api, Worker, Puller, QueryEngine)
- Fanout pattern explanation + full queue topology table (Section 3.2)
- Tier routing summary table (Section 15.3)
- Cloud provider mapping table (Section 4.3)

### 29.3 docs/api-reference.md

**Required content:**
- Every endpoint: method, path, description, auth requirement
- Request/response schemas (JSON, with field descriptions)
- HTTP status codes and error codes
- Working `curl` examples for every endpoint:

```bash
# Ingest logs
curl -X POST http://localhost:5000/api/v1/logs \
  -H "X-Api-Key: ls_your_api_key_here" \
  -H "Content-Type: application/json" \
  -d '{"entries":[{"sourceId":"my-svc","logType":"Error","level":"Error",
       "environment":"dev","category":"exception","timestamp":"2026-03-23T10:00:00Z",
       "message":"NullRef in PaymentProcessor","tags":{"orderId":"ORD-123"}}]}'

# Natural language query
curl -X POST http://localhost:5000/api/v1/query/natural \
  -H "Authorization: Bearer YOUR_JWT" \
  -H "Content-Type: application/json" \
  -d '{"question":"How many fatal errors per service yesterday?"}'

# SQL query (with cost guard)
curl -X POST http://localhost:5000/api/v1/query/sql \
  -H "X-Api-Key: ls_key" \
  -H "Content-Type: application/json" \
  -d '{"sql":"SELECT sourceid,COUNT(*) FROM logs WHERE logtype='\''Error'\'' AND year='\''2026'\'' AND month='\''03'\'' AND day='\''23'\'' GROUP BY 1 LIMIT 20","async":true,"confirmCostIfAboveUsd":0.05}'

# Graph render
curl -X POST http://localhost:5000/api/v1/graphs/render \
  -H "X-Api-Key: ls_key" \
  -d '{"queryId":"qry_01HZ...","graphType":"Auto"}'

# Create API key
curl -X POST http://localhost:5000/api/v1/auth/keys \
  -H "Authorization: Bearer YOUR_JWT" \
  -d '{"description":"My CI pipeline key","expiresInDays":365}'

# Create pull job (S3 source)
curl -X POST http://localhost:5000/api/v1/pull-jobs \
  -H "X-Api-Key: ls_key" \
  -d '{"name":"prod-alb-logs","sourceType":"AwsS3","schedule":"0 */5 * * * ?",
       "config":{"bucketName":"my-alb-logs","prefix":"alb/prod/","fileFormat":"W3C",
                 "logType":"Network","environment":"prod","sourceId":"alb-prod"}}'

# Start replay
curl -X POST http://localhost:5000/api/v1/replay \
  -H "X-Api-Key: ls_key" \
  -d '{"from":"2026-03-01T00:00:00Z","to":"2026-03-10T23:59:59Z",
       "options":{"reindexSearch":true,"reprocessAlerts":false}}'
```

### 29.4 docs/local-development.md

**Required content:**
- Prerequisites: Docker Desktop 4.x, .NET 10 SDK, Git
- Step-by-step setup (6 steps: clone → up → setup script → pull Ollama model → verify → test)
- All environment variables table (provider, endpoints, credentials for local stack)
- How to run tests: `dotnet test` + `dotnet test --filter Category=Integration`
- How to use Ollama for local AI (model pull command, switching models)
- How to use Grafana dashboard (URL, credentials, pre-built panels)
- Troubleshooting: common errors + solutions (MinIO connection, OpenSearch heap, DuckDB Parquet path)
- How to switch providers (change one `LightScope__Provider` env var)

### 29.5 docs/query-guide.md

**Required content:**
- Tier routing explained with the 5 examples from Section 15.2
- SQL best practices (partition filters, LIMIT, APPROX_PERCENTILE)
- Anti-patterns (full table scans, no partition filters)
- Cost estimation flow (how to interpret PendingCostConfirmation response)
- Full-text search syntax (OpenSearch DSL passthrough)
- Natural language query examples (10+ examples with expected SQL output)
- DuckDB differences from Athena (local dev quirks)
- Partition filter reference table:

```sql
-- Today
AND year='2026' AND month='03' AND day='23'

-- Yesterday
AND year='2026' AND month='03' AND day='22'

-- This week (Mon-Sun)
AND year='2026' AND month='03' AND day >= '17' AND day <= '23'

-- This month
AND year='2026' AND month='03'

-- Last 7 days (straddling month boundary)
AND ((year='2026' AND month='03' AND day >= '17')
  OR (year='2026' AND month='02' AND day >= '24'))
```

### 29.6 docs/schema-evolution.md (NEW)

**Required content:**
- Why schema evolution matters (data at scale, Parquet compatibility)
- Evolution rules table (Section 8.2)
- How to add a new optional field (step-by-step example)
- How to deprecate and remove a field (deprecation period, migration steps)
- Schema inference mode explained
- How to register a manual schema version via API
- How to view schema history for a tenant
- Parquet schema merging behavior

### 29.7 docs/idempotency.md (NEW)

**Required content:**
- Why exactly-once matters for logging (duplicates in dashboards)
- UUIDv7 as idempotency key (structure, time-sortability)
- How the Redis idempotency store works (key format, TTL)
- Deduplication at each layer (Redis, OpenSearch `_id`, Parquet batch dedup)
- What happens on duplicate entry (skip + telemetry counter)
- Idempotency TTL calculation (HotRetentionDays + 1)
- Client-side best practices (always generate Id before sending; use same Id on retry)

### 29.8 docs/replay-guide.md (NEW)

**Required content:**
- When to use replay (5 use cases from Section 16.4)
- How replay works (S3 → Puller → SQS → Worker → OpenSearch)
- Starting a replay via API (curl example)
- ReplayOptions reference (reindexSearch, reprocessAlerts, reparseFiles)
- Monitoring replay progress
- Cost considerations (Parquet read from S3 is free; re-indexing to OpenSearch has compute cost)
- How to cancel a running replay

### 29.9 docs/materialized-views.md (NEW)

**Required content:**
- Why pre-aggregated views matter (dashboard latency vs. cost)
- All standard prebuilt views (from Section 22.2)
- How to query a materialized view via API
- Refresh cadence and freshness guarantees
- Fallback behavior (matview stale → live OpenSearch agg)
- How to register a custom materialized view
- Redis storage format (key pattern, TTL)

### 29.10 docs/graph-guide.md

**Required content:**
- All 9 supported graph types with description and when each is used
- Graph type selection logic (rule-based → AI-enriched)
- All 8 prebuilt graph templates with example curl
- How to render in browser: Vega-Embed snippet + Chart.js snippet
- Vega-Lite spec schema reference
- Custom graph options (theme, height, color scheme)

### 29.11 docs/security.md

**Required content:**
- Dual auth setup: creating API keys (curl), setting up Cognito (CDK snippet)
- Tenant isolation guarantees at each layer (table from Section 24)
- SQL safety rules enforced (all forbidden keywords, CROSS JOIN warning)
- AI SQL safety (audit trail, safety validation on every generated query)
- Rate limiting configuration per tenant
- Secret management per provider
- Network topology recommendations (VPC, private subnets, WAF rules)
- Compliance notes (audit log retention, encryption at rest/in-transit)

### 29.12 docs/runbooks/scaling.md

**Required content:**
- When to scale Worker pods (queue depth > 10,000 messages)
- Worker scaling formula: pods = ceil(target_throughput / (ConsumerCount × BatchSize × RecvRate))
- OpenSearch shard scaling triggers (primary shard > 30 GB)
- How to increase tenant rate limits without restart
- Metrics to watch in Grafana (queue lag, processing latency, duplicate rate)

### 29.13 docs/runbooks/incident-response.md

**Required content:**
- DLQ investigation steps (how to inspect, replay, or purge)
- How to replay messages from DLQ to main queue
- OpenSearch index recovery (if index corrupted → trigger replay from Parquet)
- Worker crash recovery (at-least-once + idempotency ensures no data loss)
- Alert escalation paths
- How to temporarily disable a pull job during incident

### 29.14 docs/runbooks/adding-a-connector.md

**Required content:**
- Step-by-step: implement `IPullConnector` (with code template)
- Register with named DI factory
- Add parser for new log format (implement `ILogFileParser`)
- Deploy via CDK (environment variable for new source type)
- Test with docker-compose (use HttpPullConnector + local file server as mock)

### 29.15 docs/runbooks/adding-a-cloud-provider.md

**Required content:**
- Step-by-step: create `LightScope.Adapters.{Provider}/` project
- Implement all required interfaces (9 core interfaces)
- Add provider key to `ProviderExtensions.cs`
- Add provider section to `appsettings.json`
- Add docker-compose service for local equivalent
- Testing checklist (all integration tests must pass against new provider)
```
