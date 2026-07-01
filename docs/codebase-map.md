# Codebase Map

This document maps the repository structure to responsibilities, so new engineers can quickly locate behavior and make safe changes.

## Top-Level Layout

- `src/`: application and library projects
- `tests/`: test projects aligned one-to-one with production projects
- `docs/`: design, API, operational, and onboarding documentation
- `docker/`: local runtime stack and service Dockerfiles
- `infra/`: IaC and monitoring assets (CDK, Prometheus, Grafana)
- `protos/`: gRPC contracts

## Solution and Projects

### Core Domain

- `src/Logs2Obs.Core`: provider-agnostic domain logic
- Key folders:
  - `Abstractions/`: interfaces and boundaries used by hosts/adapters
  - `Commands/`, `Query/`, `Handlers/`: command/query orchestration
  - `Models/`, `Mapping/`, `Validation/`: domain contracts and validation logic
  - `Schema/`, `Storage/`, `MatViews/`, `Graphs/`, `AI/`: specialized domains

### Hosts (Runtime Services)

- `src/Logs2Obs.Api`: HTTP/gRPC gateway, auth, rate limiting, endpoint composition
- `src/Logs2Obs.Worker`: asynchronous queue consumers and pipeline workers
- `src/Logs2Obs.QueryEngine`: query routing and analytics execution host
- `src/Logs2Obs.Puller`: scheduled external source pull ingestion host

### Infrastructure Adapters

- `src/Logs2Obs.Adapters.Local`: local dev implementations (MinIO/RabbitMQ/Redis/Meili/etc.)
- `src/Logs2Obs.Adapters.Aws`: AWS implementations (S3, SQS/SNS, DynamoDB, OpenSearch, etc.)

Each adapter project includes infrastructure-specific implementations under folders such as:

- `ObjectStore/`, `MessageBus/`, `MetadataStore/`, `Search/`
- `Scheduler/`, `SchemaRegistry/`, `QueryEngine/`, `Secrets/`, `Idempotency/`

## Tests Layout

Tests mirror production project boundaries for discoverability:

- `tests/Logs2Obs.Core.Tests`
- `tests/Logs2Obs.Api.Tests`
- `tests/Logs2Obs.Worker.Tests`
- `tests/Logs2Obs.QueryEngine.Tests`
- `tests/Logs2Obs.Puller.Tests`
- `tests/Logs2Obs.Adapters.Local.Tests`
- `tests/Logs2Obs.Adapters.Aws.Tests`

Recommended test strategy for new changes:

1. Add/adjust unit tests in the matching project test folder.
2. Run impacted project tests first.
3. Run solution-wide tests before merge.

## API and Protocol Contracts

- REST contracts and examples: [api-reference.md](api-reference.md)
- gRPC schema: [../protos/log_ingestion.proto](../protos/log_ingestion.proto)
- Security and auth contract expectations: [security.md](security.md)

## Local Runtime and Infrastructure Files

- Local stack orchestration: `docker/docker-compose.yml`
- Service images: `docker/Dockerfile.api`, `docker/Dockerfile.worker`, `docker/Dockerfile.queryengine`, `docker/Dockerfile.puller`
- Bootstrap SQL: `docker/init-scripts/01-schema.sql`
- Monitoring setup: `infra/prometheus.yml`, `infra/grafana/`
- Cloud provisioning: `infra/cdk/`

## Where to Start by Task

- Add or modify API endpoint:
  - Start in `src/Logs2Obs.Api/Endpoints/`
  - Follow dependency registration in `src/Logs2Obs.Api/DependencyInjection/`
  - Validate auth/rate-limit behavior in middleware/options
- Change ingestion processing behavior:
  - Trace API ingestion command path into message bus publish
  - Follow worker consumers in `src/Logs2Obs.Worker/Workers/`
  - Check adapter implementations for object store/search writes
- Update query behavior:
  - Start in `src/Logs2Obs.Core/Query/` and `src/Logs2Obs.Core/Handlers/`
  - Review `src/Logs2Obs.QueryEngine/Services/`
  - Validate with [query-guide.md](query-guide.md)
- Update schema or replay logic:
  - Read [schema-evolution.md](schema-evolution.md) and [replay-guide.md](replay-guide.md)
  - Check Core schema models and replay service paths in QueryEngine/Puller

## Known Documentation Gaps

Current docs are strongest on API and operations. Areas that still need deeper docs over time:

- CI/CD pipeline and release process details
- Production deployment topology by provider
- Performance benchmark baselines and SLO targets
- Feature parity status matrix beyond high-level provider capability list

## Related Documents

- [README.md](README.md)
- [architecture.md](architecture.md)
- [local-development.md](local-development.md)
