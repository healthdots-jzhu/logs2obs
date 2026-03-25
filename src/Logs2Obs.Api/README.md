# Logs2Obs.Api

ASP.NET Core 10 Web API for the logs2obs observability service. Provides both REST and gRPC interfaces for log ingestion and querying.

## Features

- **Dual Authentication**: API Key (cached) + JWT Bearer (AWS Cognito)
- **Rate Limiting**: Per-tenant token bucket (ingestion) and sliding window (queries)
- **gRPC**: Three streaming modes - unary, client streaming, bidirectional
- **OpenTelemetry**: Traces + metrics with Prometheus export
- **Health Checks**: `/health/ready` and `/health/live` endpoints
- **Structured Logging**: Serilog with console sink

## Running Locally

```bash
dotnet run --project src/Logs2Obs.Api/Logs2Obs.Api.csproj
```

The API starts on `http://localhost:5000` by default.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_URLS` | `http://localhost:5000` | Listening URLs |
| `PayloadSize__MaxPayloadBytes` | `10485760` | Max request payload (10MB) |
| `RateLimiter__IngestTokenLimit` | `1000` | Token bucket size for ingestion |
| `RateLimiter__QueryPermitLimit` | `100` | Sliding window limit for queries |
| `ApiKey__CacheDurationSeconds` | `300` | API key cache TTL |
| `OpenTelemetry__ServiceName` | `logs2obs-api` | Service name for traces |

## Rate Limiter Behavior

### Ingestion (`tenant-ingest`)
- **Policy**: Token Bucket
- **Token Limit**: 1000 tokens
- **Replenishment**: 500 tokens per second
- **Partition**: `tenantId` (or IP if unauthenticated)

Burst capacity of 1000 requests, sustained rate of 500 req/s per tenant.

### Queries (`tenant-query`)
- **Policy**: Sliding Window
- **Permit Limit**: 100 per minute
- **Window**: 1 minute, 4 segments
- **Partition**: `tenantId` (or IP if unauthenticated)

Even distribution across 15-second segments, preventing burst spikes.

## API Endpoints

### Ingestion
- `POST /api/v1/logs` — Ingest log entries
- `POST /api/v1/logs/bulk` — Bulk upload (file)
- `POST /api/v1/metrics` — Ingest metrics

### Query
- `POST /api/v1/query/sql` — Execute SQL query
- `GET /api/v1/query/{queryId}/status` — Query status
- `GET /api/v1/query/{queryId}/results` — Query results
- `POST /api/v1/query/search` — Full-text search
- `POST /api/v1/query/natural` — Natural language query

### Management
- `GET/POST /api/v1/pull-jobs` — Pull job CRUD
- `GET/POST /api/v1/alerts` — Alert CRUD
- `GET/POST/DELETE /api/v1/auth/keys` — API key management
- `POST /api/v1/replay` — Start replay job

### Health & Metrics
- `GET /health/ready` — Readiness probe
- `GET /health/live` — Liveness probe
- `GET /metrics` — Prometheus scrape endpoint

## gRPC Service

Service: `logs2obs.LogIngestionService`

**Port**: Same as HTTP (HTTP/2 required for gRPC)

**Methods**:
- `IngestLog` (unary)
- `IngestLogStream` (client streaming)
- `IngestBidirectional` (bidirectional streaming)

**Metadata Headers**:
- `x-tenant-id` — Tenant identifier (required if not in request body)
- `x-api-key` — API key for authentication

## Authentication

Two schemes available:

1. **ApiKey** (default): `X-Api-Key` header
2. **JwtBearer**: `Authorization: Bearer <token>` header

Both extract `tenantId` claim. `TenantContextMiddleware` enforces tenant isolation.

## Development

Build:
```bash
dotnet build src/Logs2Obs.Api/Logs2Obs.Api.csproj
```

Test health:
```bash
curl http://localhost:5000/health/ready
```

Ingest logs:
```bash
curl -X POST http://localhost:5000/api/v1/logs \
  -H "X-Api-Key: your-key-here" \
  -H "Content-Type: application/json" \
  -d '{"entries": [{"sourceId": "app1", "level": "Info", "message": "Test log"}]}'
```

## Dependencies

- `Logs2Obs.Core` — Domain models, commands, queries, interfaces
- `Logs2Obs.Adapters.Local` — Local file system adapters for development

## Notes

- `TreatWarningsAsErrors` is enabled — all warnings must be fixed
- `Microsoft.Extensions.Logging` namespace is removed at project level to avoid conflict with `Logs2Obs.Core.Models.LogLevel`
- All async methods accept `CancellationToken` as last parameter
- Serilog uses structured logging — no string interpolation in log messages
