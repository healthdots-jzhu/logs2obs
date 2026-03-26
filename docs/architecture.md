# logs2obs Architecture

## Overview

logs2obs uses **hexagonal architecture** (ports and adapters pattern) to achieve cloud-agnostic design. The domain core contains all business logic; infrastructure adapters are swappable. This document describes the system architecture, service responsibilities, messaging topology, and tier routing strategy.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              PRODUCERS / SOURCES                                │
│  App Logs │ Error Logs │ Network/OS │ Metrics │ S3 │ Cloud Logs │ Kafka │ HTTP  │
└──────┬────┴──────┬──────┴──────┬─────┴────┬───┴────┴──────┬─────┴───────┴──────┘
       │           │             │          │               │
       ▼           ▼             ▼          ▼               │
┌──────────────────────────────────────────────────────┐    │
│           Logs2Obs.Api  (.NET 10)                     │◄───┘
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
│                                                              │
│  [2 Topics] → [8 Queues + 4 DLQs] (see Fanout Pattern)      │
└───────────────────────────┬──────────────────────────────────┘
                            │ Per-consumer queues
          ┌─────────────────┼────────────────┬─────────────────┐
          ▼                 ▼                ▼                 ▼
  [storage-writer]  [search-indexer] [alert-evaluator] [pull-job-events]
          │                 │                │
          ▼                 ▼                ▼
┌─────────────────────────────────────────────────────────────┐
│         Logs2Obs.Worker  (.NET 10 Worker Service)            │
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
│ MinIO        │ │MeiliSrch │ │  Firestore/PgSQL  │
│              │ │          │ │                   │
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
│       Logs2Obs.QueryEngine  (.NET 10)                     │
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

## Hexagonal Architecture

logs2obs strictly separates **domain logic** (Core) from **infrastructure** (Adapters). The Core defines **ports** (interfaces); adapters implement them.

```
┌──────────────────────────────────────────────────┐
│                  DOMAIN CORE                     │
│             (Logs2Obs.Core)                      │
│                                                  │
│  ┌──────────────────────────────────────┐        │
│  │  Domain Models (LogEntry, QueryExec)│        │
│  │  MediatR Handlers (IngestLogsHandler)│       │
│  │  Validation (FluentValidation)       │       │
│  │  Business Rules (SchemaInference,    │       │
│  │    QueryTierRouter, GraphSuggestion) │       │
│  └──────────────────────────────────────┘        │
│                                                  │
│  ┌──────────────────────────────────────┐        │
│  │         PORTS (Interfaces)            │        │
│  │  IMessageBus, IObjectStore,          │        │
│  │  ISearchIndexer, IQueryEngine,       │        │
│  │  IMetadataStore, ISecretStore,       │        │
│  │  IIdempotencyStore, IAiService       │        │
│  └──────────────────────────────────────┘        │
└──────────────────────────────────────────────────┘
                     │
       ┌─────────────┼──────────────┐
       ▼             ▼              ▼
┌────────────┐ ┌────────────┐ ┌────────────┐
│  Adapters  │ │  Adapters  │ │  Adapters  │
│   .Local   │ │    .Aws    │ │   .Azure   │
│            │ │            │ │            │
│ MinIO      │ │ S3         │ │ Blob Store │
│ RabbitMQ   │ │ SNS+SQS    │ │ Svc Bus    │
│ DuckDB     │ │ Athena     │ │ Synapse    │
│ MeiliSrch  │ │ OpenSearch │ │ —          │
│ PostgreSQL │ │ DynamoDB   │ │ CosmosDB   │
│ Redis      │ │ ElastiCache│ │ —          │
└────────────┘ └────────────┘ └────────────┘

```

**Benefits:**
- Core has **zero cloud SDK dependencies** — testable without infrastructure
- Swap providers by changing DI registration: `services.AddLocalAdapters()` → `services.AddAwsAdapters()`
- New provider = new adapter project implementing 9 core interfaces

## Service Responsibilities

| Service | Port | Responsibility |
|---|---|---|
| **Logs2Obs.Api** | 8080 (HTTP), 5001 (gRPC) | HTTP/gRPC ingestion, authentication (API key + JWT), per-tenant rate limiting, request validation, MediatR dispatch, message bus publish |
| **Logs2Obs.Worker** | — | Consume SQS/RabbitMQ, idempotency check (Redis), normalize/enrich entries, batch Parquet writes (S3/MinIO), bulk index to OpenSearch/MeiliSearch |
| **Logs2Obs.Puller** | — | Pull connectors: AWS S3, Azure Blob, GCS, HTTP; scheduled jobs (cron); parse log formats (W3C, JSON, syslog); publish to message bus |
| **Logs2Obs.QueryEngine** | 8081 | DuckDB/Athena query execution, tier routing (hot/warm/cold), cost estimation + guardrails, materialized view refresh, alert rule evaluation, replay orchestration |

## Fanout Pattern: SNS Topics and SQS Queues

When the API receives a log entry, it publishes **once** to SNS; SNS fans out to **multiple independent SQS queues**. Each consumer (Worker, QueryEngine) has its own queue and processes at its own rate — no coupling.

### SNS Topics (2)

| Topic | Type | Purpose |
|-------|------|---------|
| `logs2obs-ingest` | Standard | All incoming log/metric entries — main fanout hub |
| `logs2obs-system-events` | Standard | Internal events: job complete, crawler trigger, alert fired, replay started |

**Why Standard (not FIFO)?** FIFO SNS/SQS caps at 3,000 msg/sec with batching. Logs carry their own `timestamp`; delivery order is irrelevant. Standard queues support unlimited throughput.

### SQS Queues (8 Main + 4 DLQs)

| Queue | SNS Subscription | Consumer | DLQ | Max Receive Count |
|-------|-----------------|---------|-----|-------------------|
| `ls-storage-writer` | `logs2obs-ingest` | Worker | `ls-storage-writer-dlq` | 3 |
| `ls-search-indexer` | `logs2obs-ingest` | Worker | `ls-search-indexer-dlq` | 3 |
| `ls-alert-evaluator` | `logs2obs-ingest` | QueryEngine | `ls-alert-evaluator-dlq` | 3 |
| `ls-matview-refresh` | `logs2obs-ingest` | QueryEngine | `ls-matview-refresh-dlq` | 3 |
| `ls-pull-job-events` | `logs2obs-system-events` | Puller | `ls-pull-job-events-dlq` | 3 |
| `ls-replay-events` | `logs2obs-system-events` | Worker | `ls-replay-events-dlq` | 3 |
| `ls-report-scheduler` | `logs2obs-system-events` | QueryEngine | `ls-report-scheduler-dlq` | 3 |
| `ls-idempotency-expire` | `logs2obs-system-events` | Worker | `ls-idempotency-expire-dlq` | 3 |

**Total:** 2 SNS Topics + 8 SQS Queues + 8 DLQs = **18 messaging resources**

### Fanout Flow

```
  API publishes once ──►  SNS: logs2obs-ingest  (Standard, ~unlimited throughput)
                                    │
              ┌─────────────────────┼────────────────────┬───────────────────────┐
              ▼                     ▼                    ▼                       ▼
    SQS: storage-writer   SQS: search-indexer   SQS: alert-evaluator  SQS: matview-refresh
    (+ DLQ)               (+ DLQ)               (+ DLQ)               (+ DLQ)
         │                     │                     │                      │
    Worker: Parquet       Worker: OpenSearch     QueryEngine:          QueryEngine:
    batch write           bulk index             alert rules           aggregation update
```

**Why separate queues?**
- **Independent scaling** — search indexer can scale to 20 pods while storage writer needs only 5
- **Independent retry policies** — OpenSearch bulk errors retry 3×; Parquet write failures go to DLQ immediately
- **Failure isolation** — if search indexing fails, storage writes continue unaffected

## Tier Routing: Hot / Warm / Cold

logs2obs routes queries to the appropriate storage tier based on the time range. This balances **latency** (hot tier = fast) with **cost** (cold tier = cheap).

| Tier | Engine | Time Range | Storage | Latency | Use Case |
|---|---|---|---|---|---|
| **Hot** | OpenSearch / MeiliSearch | Last 3 days (configurable per tenant) | Index in memory/SSD | <200ms | Real-time dashboards, full-text search, log tailing |
| **Warm** | DuckDB / Athena | 4–90 days | Parquet on S3/MinIO (Standard storage) | <5s | Ad-hoc SQL queries, weekly reports, debugging incidents |
| **Cold** | DuckDB / Athena | >90 days | Parquet on S3 Glacier / Azure Archive | <30s | Compliance queries, yearly audits, historical analysis |
| **CrossTier** | Parallel query + merge | Spans multiple tiers (e.g., "last 7 days" when hot = 3 days) | Queries hot + warm in parallel | varies | Date ranges crossing tier boundaries |

### Tier Selection Examples

1. **"Show errors in the last hour"** → Hot tier (OpenSearch aggregation, <200ms)
2. **"Count fatal errors yesterday"** → Warm tier (DuckDB local Parquet scan, <2s)
3. **"Top errors in March 2025"** → Cold tier (Athena on S3 Standard, 3–10s depending on partition size)
4. **"Weekly error trend for last 14 days"** (hot = 3 days) → CrossTier (parallel: hot for days 0–3, warm for days 4–14, merge results)
5. **"All errors in 2024"** → Cold tier (Athena Glacier restore may take minutes; user confirmation required if cost >$0.50)

## Cloud Provider Mapping

| Component | Local (Dev) | AWS | Azure | GCP |
|---|---|---|---|---|
| **Object Store** | MinIO (S3-compatible) | S3 (Standard → Glacier) | Blob Storage (Hot → Archive) | Google Cloud Storage |
| **Message Bus** | RabbitMQ (exchanges + queues) | SNS (topics) + SQS (queues) | Service Bus (topics + subscriptions) | Pub/Sub (topics + subscriptions) |
| **Query Engine** | DuckDB (embedded) | Athena (serverless Presto) | Synapse Serverless SQL | BigQuery |
| **Search Index** | MeiliSearch (embedded) | OpenSearch Service (managed) | — (use Synapse for full-text) | — (use BigQuery for full-text) |
| **Metadata Store** | PostgreSQL (docker) | DynamoDB (NoSQL) | Cosmos DB (NoSQL) | Firestore (NoSQL) |
| **Cache / Idempotency** | Redis (docker) | ElastiCache (Redis) | Azure Cache for Redis | Memorystore (Redis) |
| **Secrets** | appsettings.json | Secrets Manager | Key Vault | Secret Manager |
| **Auth** | API keys in PostgreSQL | Cognito | Entra ID (Azure AD) | Firebase Auth |

**Switch providers:** Set `Logs2Obs__Provider=Aws` in environment variables. All adapter interfaces remain identical; only DI registration changes.

## Data Flow: Ingest to Query

### 1. Ingestion Flow

```
1. Client → POST /api/v1/logs (with X-Api-Key or JWT Bearer)
2. TenantContextMiddleware extracts tenantId from auth
3. PayloadSizeMiddleware rejects requests >10 MB
4. Rate limiter checks tenant quota (token bucket: 1000 tokens, refill 500/sec)
5. FluentValidation validates LogEntryDto (required fields, enum values)
6. MediatR dispatches IngestLogsCommand
7. IngestLogsHandler:
   a. Maps DTO → LogEntry domain model (generates UUIDv7 Id, sets TenantId, IngestedAt)
   b. Publishes to IMessageBus (SNS/RabbitMQ topic: logs2obs-ingest)
8. API returns 202 Accepted { accepted: 1, requestId: "..." }
9. SNS fans out to 4 SQS queues:
   - storage-writer (Worker writes Parquet to S3)
   - search-indexer (Worker bulk-indexes to OpenSearch)
   - alert-evaluator (QueryEngine evaluates active alerts)
   - matview-refresh (QueryEngine updates pre-aggregated views)
```

### 2. Query Flow

```
1. Client → POST /api/v1/query/sql { sql: "SELECT ...", async: true }
2. SqlSafetyValidator checks SQL:
   - No DML/DDL (DROP, DELETE, INSERT, UPDATE, ALTER)
   - CROSS JOIN warning (performance risk)
   - Partition filter present (year, month, day)
   - LIMIT clause present
3. QueryTierRouter analyzes time range:
   - Partition filters: year=2026, month=03, day=23
   - Tier decision: Hot (within last 3 days)
4. If async=true:
   a. Create QueryExecution record (status: Running, queryId)
   b. Dispatch to background queue (IMessageBus: query-exec topic)
   c. Return 202 { queryId, status: "Running" }
5. QueryEngine worker:
   a. Route to IQueryEngine (DuckDBQueryEngine or AthenaQueryEngine)
   b. Execute query (tenant filter auto-injected: WHERE tenantId='...')
   c. Cost guard: estimate scan size; if >10 GB, require user confirmation
   d. Stream results to IMetadataStore (partitioned by queryId)
6. Client polls: GET /api/v1/query/{queryId}/results
7. Return: { status: "Completed", results: { columns, rows }, executionTimeMs }
```

## Self-Observability

All services export:
- **OpenTelemetry traces** (Activity API) to Prometheus or OTEL Collector
- **Prometheus metrics** at `/metrics` endpoint
- **Health checks** at `/health/ready` (dependencies up) and `/health/live` (process alive)

**Key metrics:**
- `logs2obs_ingestion_rate` — entries/sec per tenant
- `logs2obs_queue_lag_seconds` — SQS message age (detect backlog)
- `logs2obs_processing_latency_ms` — ingest-to-index latency
- `logs2obs_duplicate_rate` — idempotency hit rate (should be <1%)
- `logs2obs_query_execution_time_ms` — query latency by tier (hot/warm/cold)
- `logs2obs_cost_estimate_usd` — estimated query cost before execution

**Alerting:**
- Queue lag >300 seconds → scale Worker pods
- Duplicate rate >5% → investigate client retry logic
- OpenSearch indexing errors >10/min → check cluster health

See [local-development.md](local-development.md) for Grafana dashboard setup.
