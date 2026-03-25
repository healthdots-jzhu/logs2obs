# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** .NET 10, C# 14, ASP.NET Core Minimal APIs + gRPC, MediatR 12, FluentValidation 11, Polly 8, Serilog, OpenTelemetry, Parquet.Net
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0)
- **Created:** 2026-03-24

## Key Facts for My Work

- **My phases:** Phase 1 (LightScope.Core) and Phase 4 (LightScope.Api)
- **Core rule:** LightScope.Core references ONLY MediatR, FluentValidation, Parquet.Net, Polly, Microsoft.Extensions.*, System.* — never cloud SDKs
- **DtoMapper rule:** Id (UUIDv7), TenantId (from auth), IngestedAt (UtcNow) always set in DtoMapper.ToDomain() — never from DTO fields
- **API style:** Minimal APIs with endpoint route groups — no controllers ever
- **gRPC:** 3 streaming modes — unary (SendBatch), client streaming (StreamLogs), bidirectional (StreamWithAck)
- **Auth:** Dual — ApiKey (hashed, cached in Redis) + JWT/Cognito; TenantContextMiddleware extracts tenantId from both
- **Rate limiting:** Token bucket (burst) + sliding window (sustained), per-tenant, Section 11 of design doc
- **Validation:** FluentValidation validators for all DTOs before MediatR dispatch; LogEntryDtoValidator is the canonical example (Section 27.9)
- **Error hierarchy:** LightScopeException → ValidationException, SqlSafetyException, QueryGuardException, TenantNotFoundException, etc.
- **Protobuf:** `protos/log_ingestion.proto` — LogEntryProto, AckResponse, BatchRequest, SendResponse
- **Health endpoints:** /health/ready, /health/live, /metrics (Prometheus scrape endpoint)

## Learnings

### 2026-03-24: Phase 4 — Logs2Obs.Api Scaffolding

Successfully scaffolded the Logs2Obs.Api project with ASP.NET Core 10 Minimal APIs + gRPC.

**Key implementations:**
- Dual authentication (ApiKey + JwtBearer) with ApiKeyAuthHandler using IMemoryCache for 5min caching
- Per-tenant rate limiting: TokenBucket (1000 tokens, refill 500/sec) for ingestion, SlidingWindow (100/min) for queries
- gRPC LogIngestionService with 3 streaming modes: unary, client streaming, bidirectional
- 7 endpoint groups: Logs, Query, Graphs, PullJobs, Alerts, Auth, Replay
- TenantContextMiddleware extracts tenantId from claims, enforces on authenticated requests
- PayloadSizeMiddleware rejects >10MB requests to /api/v1/logs
- GlobalExceptionHandler with FluentValidation support (fully qualified to avoid ambiguity)
- OpenTelemetry traces + Prometheus metrics export
- Health endpoints: /health/ready, /health/live
- Serilog structured logging (requires explicit `using Microsoft.Extensions.Logging;` due to removed implicit using)

**Interface discoveries:**
- IMetadataStore uses Get/Put/Delete/Query (table, key pattern), NOT Read/Write/List
- IScheduler has no TriggerJobAsync — manual job triggering requires background queue
- ISearchIndexer.SearchAsync takes 4 params (tenantId, query, limit, ct), no environment filter
- ISecretStore methods: GetSecretAsync/SetSecretAsync (not StoreSecretAsync)
- IQueryEngine.GetResultAsync (not GetExecutionStatusAsync/GetResultsAsync)
- GetNaturalLanguageQuery.NaturalLanguage property (not Question)
- LogEntryDto.Timestamp (not TimestampUtc)

**ASP.NET Core 10 API changes:**
- Rate limiter policy API changed from `.AddTokenBucketLimiter().WithPartitionKey()` to `.AddPolicy(context => RateLimitPartition.Get...())`
- IMemoryCache.Set requires MemoryCacheEntryOptions, not TimeSpan directly
- HttpClientInstrumentation extension missing from OpenTelemetry package set (removed for now)
- WithOpenApi() deprecated (ASPDEPR002) — suppressed via NoWarn

**Code analysis suppressions needed:**
- CA1305 (culture-specific): Serilog console format provider
- CA1848 (LoggerMessage delegates): Prefer simplicity for non-hot-path logging
- CA1861 (static readonly arrays): Health check tags array
- CA1873 (expensive logging args): Structured logging property evaluation
- CA2024 (EndOfStream in async): File upload parsing

**Created CoreServiceCollectionExtensions in Logs2Obs.Core/DependencyInjection/**
- AddLogs2ObsCore() registers MediatR, GraphSuggestionEngine, SqlSafetyValidator, QueryTierRouter
- TenantQueryInjector, SchemaInferenceEngine are static — no DI registration needed

**Protobuf schema:**
- `protos/log_ingestion.proto` with LogIngestionService (3 RPC methods)
- Maps LogEntryProto → LogEntryDto in gRPC service layer

**Build verified:** All projects compile cleanly with TreatWarningsAsErrors enabled.

<!-- Append new learnings below. -->

