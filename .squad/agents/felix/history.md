# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** .NET 10, AWS CDK v2 (C#), Docker Compose, GitHub Actions, Prometheus, Grafana, OpenSearch ILM, Testcontainers
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0)
- **Created:** 2026-03-24

## Key Facts for My Work

- **My phases:** Phase 3 (Docker + local setup) and Phase 11 (CDK Infrastructure)
- **Docker Compose services (local dev):** MinIO (S3-compat), RabbitMQ, PostgreSQL, Redis, OpenSearch (or Meilisearch), Ollama, Prometheus, Grafana — all with health checks and dependency ordering
- **Dockerfiles:** 4 total — Dockerfile.api, Dockerfile.worker, Dockerfile.puller, Dockerfile.queryengine — all use mcr.microsoft.com/dotnet/sdk:10.0 multi-stage build, expose 8080/8081
- **local-setup.sh:** Creates MinIO buckets (lightscope-logs, lightscope-results), runs PostgreSQL schema migration (infra/scripts/schema.sql), pulls Ollama model — idempotent
- **global.json:** Pins .NET 10 SDK (`"version": "10.0.0", "rollForward": "latestMinor"`)
- **CDK stacks (8 total):** StorageStack (S3 × 2 + Glue + lifecycle), MessagingStack (2 SNS + 8 SQS + 8 DLQs), SearchStack (OpenSearch domain + ILM policy), DatabaseStack (DynamoDB tables: tenants, pull-jobs, saved-queries, alert-rules, replay-jobs, query-executions, schema-versions), CacheStack (ElastiCache Redis), AuthStack (Cognito User Pool + Lambda), NetworkStack (VPC + ALB + WAF + TLS), ComputeStack (ECS cluster + 4 task defs)
- **Directory.Build.props:** Enforces WarningsAsErrors for any AWSSDK.*/Azure.*/Google.Cloud.* reference in LightScope.Core
- **OpenSearch ILM:** Hot → Warm → Cold → Delete phases; index template with `lightscope-{tenantId}-{yyyy.MM.dd}` rollover alias
- **Grafana dashboard:** Pre-built (infra/grafana/dashboards/lightscope.json) — ingestion rate, queue lag, P99 latency, duplicate rate, OpenSearch throughput, AI query usage
- **Prometheus config:** Scrapes /metrics endpoint on all 4 services; also scrapes RabbitMQ, OpenSearch, Redis exporters

## Learnings

<!-- Append new learnings below. -->
