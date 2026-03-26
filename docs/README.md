# logs2obs

## Overview

**logs2obs** is a lightweight, cloud-agnostic observability and log intelligence platform built on .NET 10. It provides multi-tenant log ingestion, intelligent schema inference, tiered query routing (hot/warm/cold), natural language querying powered by AI, and real-time alerting. logs2obs enables developers to ship logs from any source and gain instant observability without managing complex infrastructure.

The platform solves three critical problems: **cost control** (tiered storage automatically moves old logs to cheaper tiers), **operational simplicity** (one API accepts all log formats with automatic schema inference), and **query flexibility** (SQL, full-text search, or plain English queries). Built with hexagonal architecture, logs2obs runs locally with Docker or deploys to AWS, Azure, or GCP with a single environment variable change.

## Features

- **Multi-Tenant Log Ingestion** — REST/JSON and gRPC streaming APIs with per-tenant rate limiting and API key isolation
- **Schema Inference** — Automatic schema detection and evolution; no manual mapping required
- **Tiered Query Routing** — Hot tier (OpenSearch, <200ms), Warm tier (DuckDB/Athena, <5s), Cold tier (S3 Glacier, <30s)
- **Natural Language Queries** — Ask "How many fatal errors per service yesterday?" — AI translates to SQL with safety validation
- **Graph Rendering** — Auto-suggest chart types (line, bar, heatmap, scatter) from query results; export Vega-Lite or Chart.js specs
- **Real-Time Alerting** — SQL-based alert rules evaluated on ingest; fire to Slack, PagerDuty, or webhooks
- **Replay/Reprocessing** — Re-index historical data from Parquet archives without re-ingesting
- **Materialized Views** — Pre-aggregated dashboards (error rates, latency p99) refreshed every 5 minutes
- **Pull Connectors** — Ingest from AWS S3, Azure Blob, GCS, or HTTP on schedule (CloudWatch, ALB logs, etc.)
- **Multi-Cloud Ready** — Swap providers (Local/AWS/Azure/GCP) via single `Logs2Obs__Provider` environment variable

## Quick Start

```bash
# 1. Clone repository
git clone https://github.com/your-org/logs2obs.git
cd logs2obs

# 2. Start local stack (MinIO, RabbitMQ, PostgreSQL, Redis, MeiliSearch)
cd docker
docker compose up -d

# 3. Build solution
dotnet build ../logs2obs.slnx

# 4. Run API service
dotnet run --project ../src/Logs2Obs.Api

# 5. Verify health
curl http://localhost:8080/health/ready
```

**Result:** API listening on `http://localhost:8080`. See [Local Development](local-development.md) for full setup.

## Architecture

logs2obs uses **hexagonal architecture** with a domain core (`Logs2Obs.Core`) and pluggable adapters for each cloud provider. The ingestion flow is: **Producers → API → Message Bus (fanout) → Workers → Storage (Parquet + OpenSearch)**.

```
┌─────────────┐
│  Producers  │  (apps, agents, S3 pull jobs)
└──────┬──────┘
       ▼
┌─────────────┐
│  Logs2Obs   │  (ASP.NET Core 10 Minimal API + gRPC)
│    .Api     │  (auth, rate limiting, validation)
└──────┬──────┘
       ▼  (publish to message bus)
┌────────────────────────────────┐
│  SNS/RabbitMQ Fanout (2 topics)│
│  → 8 SQS/queues + 4 DLQs       │
└────────┬───────────────────────┘
         ▼
┌────────────────────────┐
│  Logs2Obs.Worker       │  (consume queues, write Parquet, index OpenSearch)
│  Logs2Obs.Puller       │  (pull connectors: S3, HTTP, Blob)
│  Logs2Obs.QueryEngine  │  (DuckDB/Athena queries, alerts, matviews)
└────────┬───────────────┘
         ▼
┌──────────────────────────────────┐
│  Storage: S3/MinIO (Parquet)     │
│  Search: OpenSearch/MeiliSearch  │
│  Metadata: PostgreSQL/DynamoDB   │
│  Cache: Redis/ElastiCache        │
└──────────────────────────────────┘
```

See [architecture.md](architecture.md) for full details.

## Provider Compatibility

logs2obs adapters abstract infrastructure differences. Switch providers by setting `Logs2Obs__Provider=Aws|Azure|Gcp|Local`.

| Capability | Local (Dev) | AWS | Azure | GCP |
|---|---|---|---|
| Object Store | MinIO | S3 | Blob Storage | GCS |
| Message Bus | RabbitMQ | SNS+SQS | Service Bus | Pub/Sub |
| Query Engine | DuckDB | Athena | Synapse | BigQuery |
| Search | MeiliSearch | OpenSearch Service | — | — |
| Metadata | PostgreSQL | DynamoDB | CosmosDB | — |
| Cache | Redis | ElastiCache | — | — |

**Note:** Azure and GCP adapters are under development. AWS and Local adapters are production-ready.

## Documentation

- **[Architecture](architecture.md)** — Hexagonal design, service responsibilities, fanout pattern, tier routing
- **[API Reference](api-reference.md)** — All endpoints with curl examples (ingest, query, graph, auth, alerts, pull jobs, replay)
- **[Local Development](local-development.md)** — Prerequisites, setup steps, running tests, troubleshooting

## Configuration

**Essential environment variables:**

```bash
# Provider (Local | Aws | Azure | Gcp)
export Logs2Obs__Provider=Local

# Local stack endpoints (docker-compose defaults)
export ConnectionStrings__Postgres="Host=localhost;Database=logs2obs;Username=logs2obs;Password=logs2obs"
export ObjectStore__MinIO__Endpoint=http://localhost:9000
export ObjectStore__MinIO__AccessKey=minioadmin
export ObjectStore__MinIO__SecretKey=minioadmin
export MessageBus__RabbitMq__Host=localhost
export Search__MeiliSearch__Endpoint=http://localhost:7700
export Metadata__Redis__ConnectionString=localhost:6379
```

Full configuration reference in [local-development.md](local-development.md).

## Testing

```bash
# Run all unit tests
dotnet test logs2obs.slnx

# Run integration tests (requires docker-compose up)
dotnet test logs2obs.slnx --filter "Category=Integration"

# Exclude local adapter tests (for CI without Docker)
dotnet test logs2obs.slnx --configuration Release --filter "FullyQualifiedName!~Adapters.Local"
```

## Example: Ingest and Query

```bash
# 1. Ingest logs via REST API
curl -X POST http://localhost:8080/api/v1/logs \
  -H "X-Api-Key: ls_your_api_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "entries": [{
      "sourceId": "payment-service",
      "logType": "Error",
      "level": "Fatal",
      "environment": "prod",
      "category": "exception",
      "timestamp": "2026-03-23T14:32:00Z",
      "message": "NullReferenceException in PaymentProcessor.Charge",
      "tags": {"orderId": "ORD-98765", "customerId": "CUST-123"}
    }]
  }'

# 2. Query with natural language
curl -X POST http://localhost:8080/api/v1/query/natural \
  -H "X-Api-Key: ls_your_api_key_here" \
  -H "Content-Type: application/json" \
  -d '{"question": "How many fatal errors in prod today?"}'

# Response:
# {
#   "queryId": "qry_01HZ...",
#   "sql": "SELECT COUNT(*) FROM logs WHERE level='Fatal' AND environment='prod' AND year='2026' AND month='03' AND day='23' LIMIT 10000",
#   "explanation": "Counting Fatal-level errors in prod environment on 2026-03-23.",
#   "tier": "Hot",
#   "results": { "columns": ["count"], "rows": [[142]] }
# }
```

## License

Proprietary. © 2026 Jason Zhu. All rights reserved.
