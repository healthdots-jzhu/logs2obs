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

### 2026-03-24: Phase 3 — Docker Compose & Local Infrastructure

**Files created:**
- `docker/docker-compose.yml` — full local stack, name: logs2obs
- `docker/init-scripts/01-schema.sql` — PostgreSQL schema (metadata tables + schema_registry)
- `docker/Dockerfile.api` — Logs2Obs.Api, exposes 8080
- `docker/Dockerfile.worker` — Logs2Obs.Worker, exposes 8081
- `docker/Dockerfile.puller` — Logs2Obs.Puller, exposes 8082
- `docker/Dockerfile.queryengine` — Logs2Obs.QueryEngine, exposes 8083
- `infra/scripts/local-setup.sh` — idempotent bootstrap (MinIO buckets, health checks, Ollama pull)
- `infra/prometheus.yml` — scrapes api:8080, worker:8081, queryengine:8082 via host.docker.internal
- `infra/grafana/datasources/prometheus.yml` — Prometheus datasource
- `infra/grafana/dashboards/logs2obs.json` — 3-panel dashboard (Ingestion Rate, Error Rate, Query Latency P99)
- `README.md` — root-level quick start
- `Directory.Build.props` — updated with global Nullable/ImplicitUsings/TreatWarningsAsErrors + cloud SDK ban for Logs2Obs.Core
- `.gitignore` — updated with .NET, local dev, DuckDB, secrets patterns

**Docker Compose services (core profile — always up):**
MinIO:9000/9001, RabbitMQ:5672/15672, PostgreSQL:5432, Redis:6379, Meilisearch:7700

**Optional profiles:**
- `ai` — Ollama:11434 (GPU-capable, heavy)
- `monitoring` — Prometheus:9090, Grafana:3000

**Port assignments:**
| Service | Port(s) |
|---|---|
| MinIO API | 9000 |
| MinIO Console | 9001 |
| RabbitMQ AMQP | 5672 |
| RabbitMQ Mgmt | 15672 |
| PostgreSQL | 5432 |
| Redis | 6379 |
| Meilisearch | 7700 |
| Ollama | 11434 |
| Prometheus | 9090 |
| Grafana | 3000 |
| API service | 8080 |
| Worker service | 8081 |
| Puller service | 8082 |
| QueryEngine | 8083 |

**Potential port conflicts to watch:**
- 5432 (PostgreSQL) — conflicts with any local Postgres installation
- 6379 (Redis) — conflicts with any local Redis installation
- 9000 (MinIO) — conflicts with SonarQube if running locally
- 3000 (Grafana) — conflicts with local Node/React dev servers

**DuckDB note:** Embedded in-process — no Docker container needed; `*.duckdb` and `*.duckdb.wal` added to .gitignore.

**Meilisearch chosen over OpenSearch** for local dev — see felix-infra-decisions.md.

### 2026-03-26: Phase 10 — Health Checks, Compose, CI

- Added ASP.NET Core health endpoints for API, Worker, Puller, and QueryEngine with dedicated ports (8080/5000/5001/8081).
- Hardened Dockerfiles with multi-stage .NET 10 builds, non-root `app` user, and curl healthchecks.
- Expanded docker-compose to include all four services with dependency ordering; QueryEngine now waits on API health.
- Prometheus now scrapes logs2obs-api and logs2obs-queryengine over the Compose network.
- Added GitHub Actions CI for restore/build/test and Docker image builds.

### 2026-03-27: Phase 12 — CDK Infrastructure
- Created the `infra/cdk` CDK v2 project (net10.0) with Program.cs and 8 stack files.
- Implemented S3/Glue storage, SNS/SQS messaging, OpenSearch domain, DynamoDB tables, Redis cache, Cognito auth, VPC/ALB/WAF networking, and ECS/ECR compute.
- Verified the CDK project builds cleanly with `dotnet build`.

### 2026-03-27: Phase 14 — Documentation Files
- Created 4 comprehensive documentation files for logs2obs (Phase 14 requirements):
  - `docs/graph-guide.md` — Complete guide covering 9 graph types, prebuilt templates, Vega-Lite and Chart.js rendering examples
  - `docs/security.md` — Dual authentication (API keys + JWT/Cognito), tenant isolation at all layers, SQL safety rules, rate limiting, secret management, network topology, compliance notes
  - `docs/runbooks/scaling.md` — Worker scaling formulas, HPA/ECS auto-scaling configuration, OpenSearch shard scaling, rate limit updates, Grafana metrics, CloudWatch alarms
  - `docs/runbooks/incident-response.md` — DLQ investigation procedures, OpenSearch recovery, worker crash recovery, high error rate triage decision tree, emergency procedures
- All files use "logs2obs" branding (not LightScope), include working curl examples, and reference actual source code (GraphSuggestionEngine.cs, VegaLiteSpecBuilder.cs, AuthStack.cs, NetworkStack.cs, ApiKeyAuthHandler.cs)
- Documentation is operationally focused for engineers on-call with step-by-step runbooks and real-world troubleshooting scenarios

### 2026-03-27: Complete CI/CD GitHub Actions Setup
- Created `.github/workflows/deploy.yml` — Push Docker images to ECR and deploy to ECS on push to main
  - Uses OIDC authentication with `aws-actions/configure-aws-credentials@v4` (no access keys)
  - Builds and pushes all 4 images (api, worker, puller, queryengine) with both `:latest` and `:sha` tags
  - Updates ECS services with force-new-deployment and waits for stability
  - Concurrency control to prevent overlapping deployments
- Created `.github/workflows/release.yml` — Tag-based GitHub Releases with auto-generated changelog
  - Triggers on `v*.*.*` tags
  - Generates changelog from git log since previous tag
  - Auto-detects prerelease status based on `-rc`, `-beta`, `-alpha` suffixes
- Created `.github/workflows/codeql.yml` — Security scanning with CodeQL for C#
  - Runs on push to main, PRs to main, and weekly schedule (Mondays 2 AM)
- Modified `.github/workflows/ci.yml` — Added code coverage collection
  - Replaced single Test step with "Install dotnet-coverage" + "Test with coverage"
  - Collects XPlat Code Coverage with Cobertura XML output
  - Uploads coverage reports as artifacts alongside test results
- All workflows use latest actions (@v4 for setup-dotnet, checkout, upload-artifact; @v2 for ECR login, @v3 for CodeQL)
- Deploy workflow requires GitHub secrets: `AWS_REGION`, `AWS_ROLE_TO_ASSUME`, `ECR_REGISTRY`
- ECS cluster and service names hardcoded: `logs2obs-cluster`, `logs2obs-{api|worker|puller|queryengine}`

