# logs2obs Documentation Hub

This folder is the primary starting point for engineers onboarding to the logs2obs codebase.

If you are new to the project, read this page top-to-bottom once, then follow the "New Engineer Ramp Path" below.

## What logs2obs Is

logs2obs is a cloud-agnostic observability and log intelligence platform on .NET 10.

Core capabilities:

- Multi-tenant ingestion via REST and gRPC
- Schema inference and schema evolution
- Tiered query routing (hot/warm/cold)
- SQL, full-text, and natural-language query flows
- Graph generation from query outputs
- Replay/reprocessing and materialized views
- Pull connectors for cloud/object storage and HTTP sources

## New Engineer Ramp Path

Read in this order for fastest codebase understanding:

1. [architecture.md](architecture.md)
2. [codebase-map.md](codebase-map.md)
3. [local-development.md](local-development.md)
4. [api-reference.md](api-reference.md)
5. [query-guide.md](query-guide.md)
6. Domain deep dives (schema, replay, graphs, matviews, security)
7. Operations runbooks

## High-Level Breakdown

Each section below links to deep-dive documentation.

### 1) System Architecture

- [architecture.md](architecture.md): end-to-end request/data flows, service boundaries, and cross-cutting concerns
- [codebase-map.md](codebase-map.md): project-by-project map of solution structure and responsibilities

### 2) Service and Project Breakdown

- [codebase-map.md](codebase-map.md): `src/`, `tests/`, `infra/`, and `protos/` ownership map
- [../src/Logs2Obs.Api/README.md](../src/Logs2Obs.Api/README.md): API host responsibilities and endpoint groups
- [../src/Logs2Obs.QueryEngine/README.md](../src/Logs2Obs.QueryEngine/README.md): query engine host summary
- [../src/Logs2Obs.Worker/README.md](../src/Logs2Obs.Worker/README.md): ingestion workers, queues, and metrics
- [../src/Logs2Obs.Puller/README.md](../src/Logs2Obs.Puller/README.md): pull-based ingestion service summary
- [../src/Logs2Obs.Adapters.Local/README.md](../src/Logs2Obs.Adapters.Local/README.md): local provider adapter notes

### 3) API Surface and Contracts

- [api-reference.md](api-reference.md): request/response contracts and endpoint catalog
- [security.md](security.md): API key/JWT auth, identity provider setup, tenant claim normalization
- [../protos/log_ingestion.proto](../protos/log_ingestion.proto): gRPC contract for streaming ingest

### 4) Query, Analytics, and Visualization

- [query-guide.md](query-guide.md): routing strategy, SQL optimization patterns, and anti-patterns
- [graph-guide.md](graph-guide.md): graph suggestion and rendering behavior
- [materialized-views.md](materialized-views.md): matview definitions, refresh behavior, and read patterns

### 5) Data Lifecycle and Schema Management

- [schema-evolution.md](schema-evolution.md): schema versioning, migration safety rules, and inference mode
- [replay-guide.md](replay-guide.md): reprocessing flow and replay operations

### 6) Security and Isolation

- [security.md](security.md): authN/authZ model, key management, tenant isolation, and JWT/OIDC patterns
- [api-reference.md](api-reference.md): auth-required endpoints and expected auth headers

### 7) Local Development and Test Workflow

- [local-development.md](local-development.md): local stack setup, environment variables, tests, and troubleshooting
- [codebase-map.md](codebase-map.md): test project layout and suggested "where to start" areas

### 8) Operations and Reliability

- [runbooks/incident-response.md](runbooks/incident-response.md): DLQ handling and recovery flows
- [runbooks/scaling.md](runbooks/scaling.md): scaling workers/OpenSearch and autoscaling guidance

## Documentation Inventory

| Document | Primary Audience | Purpose |
|---|---|---|
| [architecture.md](architecture.md) | All engineers | System architecture, service interaction, and data flow |
| [codebase-map.md](codebase-map.md) | New contributors | Full solution map with project ownership and navigation hints |
| [local-development.md](local-development.md) | Contributors | Local environment setup and test execution |
| [api-reference.md](api-reference.md) | API/backend engineers | REST endpoint behavior and payload examples |
| [query-guide.md](query-guide.md) | Query and analytics engineers | Tier routing and SQL practices |
| [graph-guide.md](graph-guide.md) | API/UI engineers | Graph type suggestion and template usage |
| [materialized-views.md](materialized-views.md) | Dashboard/analytics engineers | Pre-aggregation strategy and freshness semantics |
| [schema-evolution.md](schema-evolution.md) | Data platform engineers | Schema lifecycle and compatibility rules |
| [replay-guide.md](replay-guide.md) | SRE/data engineers | Backfill/replay execution and operational patterns |
| [security.md](security.md) | Platform/security engineers | Auth, tenant isolation, and security hardening |
| [runbooks/incident-response.md](runbooks/incident-response.md) | On-call engineers | Incident triage and mitigation procedures |
| [runbooks/scaling.md](runbooks/scaling.md) | SRE/platform engineers | Capacity/scaling strategies |

## Current Coverage Assessment

Current documentation is strong for API workflows and operations, but historically lacked a central map of architecture and project ownership.

This hub now fills that gap by:

- Making this file the canonical starting page
- Linking every high-level area to at least one deep-dive doc
- Adding architecture and codebase-map docs for onboarding context

Areas still worth extending over time:

- CI/CD and deployment pipeline details by environment
- Adapter parity matrix (feature-level Local vs AWS vs Azure vs GCP)
- Performance benchmarking methodology and capacity targets

## License

Proprietary. Copyright (c) 2026 Jason Zhu. All rights reserved.
