# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** .NET 10, C# 14, ASP.NET Core Minimal APIs + gRPC, MediatR 12, FluentValidation 11, Polly 8, Serilog, OpenTelemetry, Parquet.Net, S3/MinIO, OpenSearch, DynamoDB/PostgreSQL, SNS/SQS/RabbitMQ, Athena/DuckDB, AWS CDK (C#), Docker Compose, xUnit + Testcontainers + FluentAssertions
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0, 163KB)
- **Created:** 2026-03-24

## Key Architecture Facts

- Multi-service: LightScope.Core, LightScope.Api, LightScope.Worker, LightScope.Puller, LightScope.QueryEngine, LightScope.Adapters.{Local,Aws,Azure,Gcp,Kafka}, infra/cdk
- 14 implementation phases — Phase 1 (Core) through Phase 14 (Docs)
- All cloud integrations behind interfaces (IObjectStore, IMessageBus, ISearchIndexer, IQueryEngine, IMetadataStore, ISchemaRegistry, IIdempotencyStore)
- Local adapter first rule: no cloud adapter ships without local equivalent
- LightScope.Core must have zero cloud SDK references (enforced via Directory.Build.props)
- SNS fanout → 8 SQS queues + 8 DLQs; 2 SNS topics total (18 messaging resources)
- Query tier routing: Hot (OpenSearch, <3 days), Warm (Athena/DuckDB, 3–90 days), Cold (Athena+Glacier, >90 days), CrossTier (fan-out)
- Multi-tenant: TenantId always from auth context, never from DTO
- Exactly-once via Redis idempotency store (check before processing)
- AI NL→SQL via GitHub Models API (openai/gpt-4o) with ISqlSafetyValidator + audit log

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
