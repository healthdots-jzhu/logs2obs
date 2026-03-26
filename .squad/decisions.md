# Squad Decisions

## Active Decisions

---

### 2026-03-24: Product Identity — logs2obs Naming Convention

**By:** Bernard (Lead & Architect), confirmed by Jason Zhu  
**User directive:** 2026-03-24T14-24-53 — "The product name is **logs2obs** (not LightScope)."

| Context | Convention | Example |
|---|---|---|
| Product name (human-facing, filenames, slugs) | `logs2obs` (all lowercase) | `logs2obs.slnx`, README title |
| C# namespace prefix | `Logs2Obs` (PascalCase) | `namespace Logs2Obs.Core.Models` |
| Assembly names | `Logs2Obs.<Component>` | `Logs2Obs.Core`, `Logs2Obs.Api` |
| Project/csproj file names | `Logs2Obs.<Component>.csproj` | `Logs2Obs.Core.csproj` |
| Solution file name | `logs2obs.slnx` (lowercase) | `logs2obs.slnx` |
| Source directories | `Logs2Obs.<Component>` | `src/Logs2Obs.Core/` |
| Test project names | `Logs2Obs.<Component>.Tests` | `Logs2Obs.Core.Tests` |
| Base exception class | `Logs2ObsException` | `public abstract class Logs2ObsException` |

**Rationale:** `logs2obs` is the official product identifier. PascalCase `Logs2Obs` is required for valid C# namespace/identifier syntax. The digit `2` is preserved in all forms.

**Supersedes:** "LightScope" — all references replaced in Phase 1 rename task (2026-03-24). No new code may use "LightScope".

---

### 2026-03-24: Phase 1 Architectural Decisions (Logs2Obs.Core)

**By:** Bernard (Lead & Architect)

#### Decision 1: Namespace Strategy
Use `Logs2Obs.Core.{SubFolder}` for all namespaces. Matches physical folder structure exactly. Team members can infer namespace from folder path; no aliasing needed.

#### Decision 2: Records for All Domain Models
All domain models (`LogEntry`, `TenantSettings`, `ReplayJob`, etc.) are `sealed record` with `required` + `init` properties. Immutability prevents accidental mutation; value equality is correct for domain objects; `required` provides compile-time field population safety. Handlers cannot modify domain objects after creation — all "updates" produce new instances via `with` expressions.

#### Decision 3: IIdempotencyStore Uses ValueTask
`CheckAndSetAsync` and `ExpireAsync` return `ValueTask` (not `Task`). At ~16,667 entries/sec, `ValueTask` avoids heap allocation when operations complete synchronously (e.g., Redis cache hit). Redis adapter can use `ValueTask.FromResult()` on cache hit.

#### Decision 4: IMessageBus Simple Signature
`PublishAsync<T>` takes `(string topic, T message, CancellationToken ct)` without `MessageAttributes`. SNS filter policies are an infrastructure concern — the SNS adapter derives them from message type via reflection or convention, not passed from Core.

#### Decision 5: LogLevel Namespace Conflict Resolution
Remove implicit `using Microsoft.Extensions.Logging;` via `<Using Remove="Microsoft.Extensions.Logging" />` in the csproj. Files that need `ILogger` must add the explicit using themselves. We own `Logs2Obs.Core.Models.LogLevel`; the csproj-level removal is the cleanest solution.

#### Decision 6: Microsoft.Extensions.* Package Versions
Use `9.*` for `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, and `Microsoft.Extensions.DependencyInjection.Abstractions`. .NET 10 preview packages may not be on NuGet; `9.*` is fully compatible with `net10.0` TFM. Upgrade to `10.*` when .NET 10 GA ships.

#### Decision 7: ISchemaRegistry Simplified Interface
Use `IReadOnlyList<SchemaField>` instead of `SchemaDefinition` in `RegisterSchemaAsync`. The task spec explicitly specifies this signature; `SchemaDefinition` can be introduced in Phase 9 when the full schema registry is implemented.

#### Decision 8: .NET 10 Solution Format
Solution file is `logs2obs.slnx` (not `.sln`). `dotnet new sln` in .NET 10 creates the new `.slnx` XML format by default. All `dotnet sln` commands and CI/CD pipelines must reference `logs2obs.slnx`.

**What Phase 2 needs to know:**
- `IObjectStore.ReadAsync` returns `Stream?` (nullable) — local adapter must handle non-existent keys
- `TenantQueryInjector` uses `{TENANT_FILTER}` placeholder — all prebuilt SQL templates must include it
- All handlers are stubs — Phase 4 (API) completes `IngestLogsHandler`; Phase 7 completes `ExecuteSqlQueryHandler`
- `ResiliencePipelines` are static factories — adapters should cache pipelines as fields
- `SchemaField.InferredType` is string (`"string"`, `"int64"`, `"double"`, `"bool"`, `"timestamp"`)

---

### 2026-03-24: Core API Surface Assumptions (Stubbs — Logs2Obs.Core.Tests)

**By:** Stubbs (QA & Test Engineer)  
**Context:** Tests scaffolded anticipatorily before Core was built. These assumptions may need validation when tests are first run.

**Confirmed by Bernard (Phase 1 completion):**
- `LogLevel` enum values: `Trace, Debug, Info, Warn, Error, Fatal` (NOT `Information`/`Warning`)
- `DtoMapper.ToDto(LogEntry domain)` reverse method exists (Bernard implemented it)
- `MetricDto` in `Logs2Obs.Core.Models` with `MetricName, Unit, Value, MetricType`; `MetricPayloadDto` also exists for `LogEntryDto.Metric`

**Open assumptions (to verify on first test run):**
1. `QueryResultSchema` constructor signature: `(IEnumerable<QueryColumn> columns, int rowCount)` — assumed, not explicit in design
2. `TenantSettings.IsActive: bool` — not mentioned in design §7.3; confirm Bernard included it
3. `SubQueries` is null/empty (not `[]`) for entirely-Cold queries

---

### 2026-03-24: Phase 2+3 Commit Decisions

**By:** Bernard (Lead & Architect)

#### Decision 1: Local Adapters Are Self-Contained
`MinioObjectStore` and `RedisIdempotencyStore` create their own client/connection from `IOptions<T>` rather than accepting `IMinioClient` / `IConnectionMultiplexer` via constructor injection.

**Rationale:** Local adapters are designed for dev/testing. Self-contained construction enables `new(Options.Create(new SomeOptions{...}))` test patterns without a DI container. In production DI scenarios, the adapter still receives `IOptions<T>` from the service collection.

**Impact on DI extension:** Removed `services.AddSingleton<IMinioClient>(...)` registration. `IConnectionMultiplexer` singleton kept for `RedisMatViewEngine` (not changed). Future cloud adapters (Phase 5/6) WILL use injected clients for connection sharing.

#### Decision 2: Test Project Disables TreatWarningsAsErrors
Added `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` to `Logs2Obs.Adapters.Local.Tests.csproj`.

**Rationale:** CA1707 (underscores in method names) and CA1711 (names ending in 'Collection') are standard xUnit test conventions. Enforcing these analyzer rules in tests creates friction without benefit. `TreatWarningsAsErrors=false` matches the adapter project's pattern.

#### Decision 3: Optional Logger Pattern for Local Adapters
`InProcessChannelMessageBus`, `RedisIdempotencyStore`, `PostgresMetadataStore`, `MinioObjectStore` all have `ILogger<T>? logger = null` optional parameter with `NullLogger<T>.Instance` fallback.

**Rationale:** Enables `new()` / `new(Options.Create(...))` construction in tests without providing a logger. In production DI, the registered `ILogger<T>` is automatically injected.

#### Decision 4: C# Namespace-Body Using Aliases
In file-scoped namespace files where the outer namespace hierarchy contains a same-named sub-namespace (e.g., test namespace `Logs2Obs.Adapters.Local.Tests.Foo` with `Options.Create()` where `Logs2Obs.Adapters.Local.Options` exists), using-alias directives must be placed INSIDE the namespace body (after the `namespace Foo;` declaration) to take precedence over the outer namespace lookup.

**Pattern:**
```csharp
using SomeNamespaceLevelUsing;       // compilation-unit level (outer namespace wins)
namespace Logs2Obs.Adapters.Local.Tests.Foo;
using Options = Microsoft.Extensions.Options.Options;  // namespace-body level (wins!)
```

---

### 2026-03-24: Logs2Obs.Adapters.Local Technical Decisions

**By:** Dolores (AI Agent)

#### Decision 1: NuGet Version Pinning Strategy
Pin all external dependencies to major versions using wildcard notation (e.g., `Version="6.*"`)

**Rationale:**
- Allows automatic minor/patch updates for security fixes
- Prevents breaking changes from major version bumps
- Simplifies dependency management across the solution

#### Decision 2: DuckDB In-Memory vs File-Based
Default to `:memory:` with configurable file path via `DuckDbOptions.DatabasePath`

**Rationale:**
- Local adapter is for **development and testing**, not production
- In-memory mode is fast and requires no cleanup
- File-based mode available for persistence scenarios (e.g., local debugging with data retention)

#### Decision 3: Polly Resilience Pipeline Selection
Use three distinct pipelines from `ResiliencePipelines` static class:
1. **ForExternalIo<T>()** - General external calls (MinIO, Postgres, Redis, RabbitMQ)
2. **ForSearch<T>()** - Search operations with timeout (Meilisearch)
3. **ForStorage<T>()** - Blob storage operations with longer timeout (MinIO reads/writes)

#### Decision 4: Meilisearch Aggregation Fallback
Implement in-memory aggregation after fetching search results (limit 10,000 docs)

**Rationale:** Meilisearch v0.x SDK lacks native faceted aggregation API. Manually group/count in LINQ.

#### Decision 5: IMetadataStore Key Extraction Convention
Use convention-based key extraction with fallback hierarchy:
1. Check explicit fields: `id`, `key`, `tenantId`, `queryId`, `jobId`, `ruleId`, `executionId`
2. Check any property ending with `Id` (case-insensitive)
3. Throw `InvalidOperationException` if no key found

#### Decision 6: RabbitMQ Publisher Confirms Removal
Remove `ConfirmSelectAsync()` and `WaitForConfirmsOrDieAsync()` calls due to RabbitMQ v7 API changes

**Rationale:** RabbitMQ.Client v7 uses different publisher confirms mechanism. For local dev adapter, best-effort delivery is acceptable. Production AWS adapter will use SQS/SNS with at-least-once delivery guarantees.

#### Decision 7: Quartz Scheduler Manual Registration
Manually register `ISchedulerFactory` instead of using `AddQuartz()` extension

**Rationale:** `AddQuartz()` extension pulls in heavy DI configuration. Local adapter only needs basic scheduler instance.

#### Decision 8: InProcessChannelMessageBus for Testing
Provide `InProcessChannelMessageBus` as second `IMessageBus` implementation using `System.Threading.Channels`

**Rationale:** Zero external dependencies (no RabbitMQ required). Useful for unit tests and local rapid iteration.

---

### 2026-03-24: Felix Infra Decisions — Phase 3

**By:** Felix (DevOps & Infra)

#### Decision 1: Port Assignments
Standard ports for all services. See orchestration log for full port table.

#### Decision 2: Meilisearch over OpenSearch for Local Dev
**Chosen:** Meilisearch (`getmeili/meilisearch:latest`)  
**Rejected:** OpenSearch

**Rationale:**
1. **Resource footprint:** OpenSearch requires 2–4 GB RAM. Meilisearch runs in ~200 MB.
2. **Zero JVM overhead:** Meilisearch is Rust; cold-start under 1 second vs. 20–30 for OpenSearch.
3. **Simpler API:** REST API maps cleanly to `ISearchIndexer` abstraction.
4. **Dev-only scope:** Local adapter never deployed to production. Production uses OpenSearch on AWS.

#### Decision 3: Ollama as Optional Profile (`--profile ai`)
**Why optional:**
1. **Image size:** ~1.5 GB before models.
2. **GPU dependency:** Performs acceptably only with GPU acceleration.
3. **Not required for core workflows:** Ollama only powers AI log summarization (Phase 8+).
4. **CI exclusion:** `docker compose up -d` in CI never pulls Ollama.

#### Decision 4: DuckDB — No Docker Container
DuckDB is an embedded in-process database (like SQLite). The `Logs2Obs.Adapters.Local` project references the `DuckDB.NET` NuGet package directly. No server process, no port, no Docker image.

#### Decision 5: PostgreSQL init-scripts via `docker-entrypoint-initdb.d`
The `docker/init-scripts/` directory is bind-mounted to `/docker-entrypoint-initdb.d` in the PostgreSQL container. Scripts are executed once, in filename order, when the data directory is first initialized.

---

### 2026-03-24: Stubbs — Adapter Test Assumptions

**By:** Stubbs (QA & Tester)  
**Context:** Scaffolded `Logs2Obs.Adapters.Local.Tests` anticipatorily before Dolores completed Phase 2 adapters.

#### Test Namespace Assumptions
All adapter types assumed to live in specific namespaces (derived from task spec). See `.squad/orchestration-log/2026-03-25T14-26-52-stubbs.md` for full fixture design patterns and open questions for Bernard.

#### Open Questions for Team (Deferred to Phase 5+ Integration)
1. Does `PostgresMetadataStore` require a `string Key` property on `T`, or does it accept a separate key parameter?
2. Does `MinioObjectStore` auto-create the bucket, or must the caller pre-create it?
3. Is `InProcessChannelMessageBus` broadcast (all subscribers get all messages) or competing-consumer?
4. Does `DuckDbQueryEngine.SubmitAsync` return `QueryStatus.Completed` synchronously, or always `QueryStatus.Pending`?

---

### 2026-03-25: Phase 4 API Layer Decisions — Maeve

**By:** Maeve (Backend Dev — Core/API)

#### Decision 1: Rate Limiter API — .NET 10 Functional Pattern
ASP.NET Core 10 changed rate limiting from chaining to functional pattern:
```csharp
options.AddPolicy("name", context =>
    RateLimitPartition.GetTokenBucketLimiter(
        partitionKey: context.GetTenantId(),
        factory: _ => new TokenBucketRateLimiterOptions { ... }));
```

#### Decision 2: IMemoryCache.Set — MemoryCacheEntryOptions Required
`IMemoryCache.Set()` in .NET 10 requires `MemoryCacheEntryOptions` for expiration:
```csharp
var options = new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(300)
};
_cache.Set(key, value, options);
```

#### Decision 3: Analyzer Warning Suppressions (TreatWarningsAsErrors)
Suppress non-critical rules in `Logs2Obs.Api.csproj`:
- **CA1305:** Culture-specific format (Serilog sink doesn't need IFormatProvider)
- **CA1848:** LoggerMessage delegates (readability over perf for non-hot-path)
- **CA1861:** Static readonly arrays (health check tags—negligible allocation)
- **CA1873:** Expensive logging args (structured logging acceptable cost)
- **CA2024:** EndOfStream in async (FileUploadParser is async-safe)
- **ASPDEPR002:** WithOpenApi deprecation (temporary until API replacement available)

#### Decision 4: IMetadataStore Interface — Table-Oriented Pattern
Use `GetAsync<T>(table, key)` / `PutAsync<T>(table, entity)` / `DeleteAsync(table, key)` / `QueryAsync<T>(table, filter)` pattern matching DynamoDB/Cosmos table strategy. Endpoints normalize to this interface; adapters specialize table naming (e.g., Postgres uses `metadata_{table}`).

#### Decision 5: Rate Limiter Partition Key Fallback Chain
```csharp
context.GetTenantId() ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
```
Ensures rate limiting works for authenticated (tenant ID), unauthenticated (IP), and edge cases (health checks, unknown IP).

#### Decision 6: gRPC Tenant ID Extraction — Header + Field Fallback
`LogIngestionGrpcService` checks in order:
1. gRPC metadata header `x-tenant-id`
2. `IngestLogRequest.TenantId` field

Supports both authenticated (header) and unauthenticated (field) gRPC calls.

#### Decision 7: Minimal APIs — No Controllers
All endpoints use `MapLogsEndpoints()`, `MapQueryEndpoints()` route groups. No controllers.

**Benefits:**
- Faster startup (no reflection-based discovery)
- Clear per-endpoint dependency injection
- Easier to test (pure functions, no framework magic)

#### Decision 8: OpenTelemetry HttpClient Instrumentation Omitted
`OpenTelemetry.Instrumentation.Http` unavailable in current package set. Removed `.AddHttpClientInstrumentation()` calls. Outbound HTTP tracing/metrics deferred to Phase 8+ (can be added when package available).

#### Decision 9: NaturalLanguageQuery Property Name
Command property is `NaturalLanguage`, not `Question`. Matches AI service contract where input is a natural language string (may be question, statement, or imperative).

#### Decision 10: IScheduler — No Manual Trigger API
`IScheduler` interface has `ScheduleAsync` / `UnscheduleAsync` / `GetNextRunTimeAsync` but NO `TriggerJobAsync`. Manual triggering deferred to Phase 8 (Operations & Monitoring) via background job queue when `IBackgroundJobQueue` is implemented.

---

### 2026-03-25: Phase 4 API Test Wiring — Stubbs (Test Assumptions → Verified Implementation)

**By:** Stubbs (QA & Tester)  
**Date:** 2026-03-25  
**Status:** Complete — All 15 skipped tests now enabled and passing

#### Summary
Successfully adapted 15 skipped API tests to Maeve's actual implementations. All assumptions either confirmed or corrected. Final test suite: 21 tests, 21 passing, 0 skipped, 0 failed.

#### ApiKeyAuthHandler Findings
- **Base class:** Inherits `AuthenticationHandler<ApiKeyAuthOptions>` — requires `IOptionsMonitor`, `ILoggerFactory`, `UrlEncoder` in constructor
- **Metadata table:** `"api_keys"` (not `"apikeys"`)
- **Metadata structure:** `Dictionary<string, string>` with keys `"tenantId"`, `"active"`, `"keyId"` (not typed `ApiKeyRecord`)
- **Cache:** Uses `IMemoryCache.Set()` extension method; tests must mock `CreateEntry()` + verify `ICacheEntry.Value` property setter
- **Cache key format:** `$"apikey:{apiKey}"`
- **Cache value:** `(string TenantId, string KeyId)` tuple

**Tests Updated:** Removed 8 Skip attributes, created `CreateHandler()` helper, fixed options monitor + cache mocking, updated metadata structure.

#### PayloadSizeMiddleware Findings
- **Signature:** `InvokeAsync(HttpContext context, CancellationToken cancellationToken)` (not `Invoke`)
- **Path filtering:** Only applies to `/api/v1/logs` — other paths bypass
- **Default max size:** 500 KB
- **Response:** HTTP 413 Payload Too Large with JSON body: `{ error, maxSize, receivedSize }`

**Tests Updated:** Removed 4 Skip attributes, added `CancellationToken.None` to all invocations, set request paths.

#### GlobalExceptionHandler Findings
- **Pattern:** Implements `IExceptionHandler` (not middleware) — registered via `.AddExceptionHandler()` in DI
- **Signature:** `TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)`
- **Logger:** Uses `Serilog.Log` static logger (not injected)
- **Exception mapping:**
  - `FluentValidation.ValidationException` → HTTP 400 with `validationErrors` array
  - `UnauthorizedAccessException` → HTTP 401 with `correlationId`
  - `Logs2ObsException` subtypes → HTTP 400/404 based on type
  - Generic exceptions → HTTP 500 with generic message (no stack trace leak)

**Tests Updated:** Removed 4 Skip attributes, switched to `IExceptionHandler` pattern, used actual `FluentValidation` types, verified `correlationId` generation.

#### Key Learnings
1. **Auth handlers are framework-complex** — Base class requires full setup. Future tests should anticipate.
2. **Moq limitation:** Extension methods cannot be mocked; mock underlying `CreateEntry()` + verify property setters.
3. **Middleware method signatures vary** — Always verify `Invoke` vs. `InvokeAsync` + `CancellationToken` requirements.
4. **Exception handlers != middleware** — `IExceptionHandler` is a different pattern; called directly, not via RequestDelegate.

---

## Phase 5

### 2026-03-25: Phase 5 Worker Service Decisions — Dolores

**By:** Dolores (Backend Dev — Pipeline/Data)  
**Date:** 2026-03-25  
**Phase:** 5 (Worker Service)

#### Decision 1: Parquet.Net 4.x Serialization Strategy

**Chosen:** `ParquetSerializer.SerializeAsync<T>()` with POCO class  
**Rejected:** Manual schema + column-by-column writing

**Rationale:** POCO class is simpler, type-safe, and maintainable. Add/remove fields by changing the POCO, not manual array management. Serializer is optimized; manual approach is prone to runtime index errors.

**Implementation:** `LogEntryParquetRecord` sealed class with 12 `required` init properties.

#### Decision 2: Polly 8 with Async Lambdas — Return Type Strategy

**Problem:** `ResiliencePipeline<Task>.ExecuteAsync()` causes type inference errors — no return value.

**Solution:** Use `ResiliencePipeline<object?>()` and return `null`:
```csharp
await ResiliencePipelines.ForStorage<object?>().ExecuteAsync(async _ =>
{
    await _objectStore.WriteAsync(key, stream, contentType, ct);
    return (object?)null;
}, ct);
```
Avoids refactoring Core's `ResiliencePipelines` static class or adding unnecessary `Task.Run()` thread hops.

#### Decision 3: OpenTelemetry Metrics — TagList vs. Array Literal

**Chosen:** Array literal syntax: `[new("tenant_id", tenantId)]`  
**Rejected:** `TagList` initializer syntax

**Rationale:** Array literal is more concise for single-tag scenarios. `TagList` is preferred for 3+ tags or dynamic tag sets. Worker metrics are 1-tag (`tenant_id`) — array literal wins on clarity.

#### Decision 4: IngestLogsHandler Fan-Out Strategy

**Pattern:** Parallel publish to both queues via `Task.WhenAll()`:
```csharp
await Task.WhenAll(
    _messageBus.PublishAsync("ls-storage-writer", batch, ct),
    _messageBus.PublishAsync("ls-search-indexer", batch, ct)
);
```
Sequential publish doubles latency. No data dependency exists between storage and search. Each worker checks Redis idempotency independently — duplicate messages are safe.

#### Decision 5: S3 Path Partitioning — Hourly Buckets

**Pattern:** `logs/{tenantId}/{yyyy/MM/dd/HH}/{batchId}.parquet`

**Why hourly:** ~720 files/month/tenant (vs. 43k minutely). Faster S3 list performance (24 prefixes/day). Replay-by-hour is a common operational use case. Batch ID uses `Guid.CreateVersion7()` for chronological sorting + uniqueness.

**Partition key for buffers:** `{tenantId}/{yyyy/MM/dd/HH}` — groups entries by hour before flush.

**Impact on future phases:**
- Phase 7 (QueryEngine): DuckDB scans `logs/{tenantId}/{yyyy/MM/dd/HH}/*.parquet` via glob patterns
- Phase 10 (AWS Adapters): S3 adapter replaces MinIO; path pattern stays identical

#### Decision 6: CA Analyzer Suppressions for Worker

Suppressed: `CA1848`, `CA1873`, `CA1725`, `CA1305`, `CA2012` via `<NoWarn>` in csproj.

**Rationale:** Worker is not a hot path (4 consumers vs. API's 1000s req/s). Clarity > micro-optimization.

#### Open Questions (Deferred)

1. **DLQ strategy:** After 3 nacks, log-and-drop or push to DLQ? (Currently: log-and-drop)
2. **Flush interval tuning:** 5s is dev default; production may need 15–30s based on volume
3. **Parquet compression:** Using Snappy (Serializer default); consider ZSTD for cold tier

---

### 2026-03-25: Phase 5 Worker Test Wiring — Stubbs

**By:** Stubbs (QA & Tester)  
**Date:** 2026-03-25  
**Status:** Complete — 26 tests, 26 passing, 0 skipped, 0 failed

#### Summary

Successfully wired `Logs2Obs.Worker.Tests` to Dolores's completed implementation. All 26 tests pass. Key findings from wiring:

- **Channel capacity:** `WorkerOptions.ChannelCapacity = 50_000` (not `ParallelConsumers * 2`)
- **Idempotency key format:** `log:{tenantId}:{entryId}` — confirmed as assumed
- **Metrics naming:** Dot-notation confirmed (`logs2obs.ingest.count`, etc.)
- **Worker constructors:** Accept `ILogger<T>` directly (not `ILoggerFactory`)
- **Flush interval:** Runs on fixed schedule (does not reset after flush)

#### Test Coverage Breakdown

| Suite | Tests | File |
|---|---|---|
| StorageWriterWorker | 8 | `Workers/StorageWriterWorkerTests.cs` |
| SearchIndexerWorker | 5 | `Workers/SearchIndexerWorkerTests.cs` |
| ParquetWriter | 4 | `Parquet/ParquetWriterTests.cs` |
| WorkerMetrics | 6 | `Telemetry/WorkerMetricsTests.cs` |
| ChannelBackpressure | 4 | `Pipeline/ChannelBackpressureTests.cs` |
| **Total** | **26** | |

#### Key Learnings

1. **Bounded channels + parallel consumers:** Tests confirm backpressure works correctly at 50k capacity with 4 parallel consumers
2. **Parquet POCO approach:** `SerializeAsync<T>()` makes schema testing straightforward — assert field presence rather than binary offsets
3. **WorkerMetrics isolation:** OTel meter tests must use unique meter names per test run to avoid counter bleed

#### Integration Tests (Deferred to Phase 12)

`StorageWriterWorkerIntegrationTests` and `SearchIndexerWorkerIntegrationTests` using Testcontainers (RabbitMQ + MinIO + Redis + Meilisearch) deferred to `tests/Logs2Obs.Integration.Tests/` in Phase 12.

---

## Phase 6

### 2026-03-25: Phase 6 Puller Decisions — Dolores

**By:** Dolores (Backend Dev — Pipeline/Data)  
**Date:** 2026-03-25  
**Phase:** 6 (Pull-Based Log Ingestion)

#### Decision 1: State Persistence Keys

Introduced `PullStateRecord` and `PullJobStateRecord` wrappers so `IMetadataStore` can derive keys via convention. 

- Pull state keys use `pullstate:{jobId}` format
- Pull job config keys use `pulljob:{tenantId}:{jobId}` format

**Rationale:** Aligns with `IMetadataStore` key extraction convention (Decision 5 in Phase 2+3). Wrapper types enable type-safe metadata operations without modifying Core abstractions.

#### Decision 2: Quartz Concurrency Bounding

Bound job execution concurrency via `PullerOptions.MaxConcurrentJobs` by configuring the Quartz default thread pool. Quartz DI job factory left at default.

**Rationale:** Single configuration point for max concurrent pull jobs. Prevents overwhelming source systems or downstream workers.

#### Decision 3: Batch Model Reuse

Puller publishes `Logs2Obs.Worker.Models.LogEntryBatch` to both storage and search indexer queues.

**Rationale:** Avoids duplicating the batch model in Puller. Batch already exists and is battle-tested (Phase 5).

#### Decision 4: Queue Configuration

Publish target is read from `PullerOptions.StorageWriterQueue` (defaulted in config) instead of hardcoded queue names.

**Rationale:** Queue names are infrastructure-dependent; reading from config enables easy local/dev/prod switching.

#### Decision 5: Connector Input Parsing

- **S3/Azure adapters:** Treat bucket/container as logical key prefixes; enumerate all blobs under prefix
- **CloudWatch adapter:** Expects JSON array of `LogEntry` items in response
- **Http/S3/Azure adapters:** Use NDJSON parsing with tenant override via request header/parameter

**Rationale:** Matches common source system patterns. NDJSON is standard for streaming log aggregators.

---

### 2026-03-25: Phase 6 Puller Test Assumptions — Stubbs

**By:** Stubbs (QA & Tester)  
**Date:** 2026-03-25  
**Status:** Complete — 32 tests, 32 passing, 0 skipped, 0 failed

#### Summary

Successfully scaffolded and wired `Logs2Obs.Puller.Tests` to Dolores's completed implementation. All 32 tests pass. Key assumptions validated:

#### IPullConnector Contract

- `PullAsync(PullJobConfig config, PullJobState? state, CancellationToken ct)` yields `LogEntry` records with `IngestionMode.Pull`
- `GetStateAsync(string jobId, CancellationToken ct)` returns `null` when no state exists
- `SaveStateAsync(string jobId, PullJobState state, CancellationToken ct)` persists arbitrary key/value state per job

#### State Key Expectations by Connector

| Connector | State Key | Value Format |
|-----------|-----------|--------------|
| AwsS3 | `lastProcessedKey` | S3 object key name |
| AzureBlob | `lastBlobName` | Blob name |
| CloudWatch | `lastEventTimestamp` | ISO 8601 timestamp |
| Http | `lastPullTimestamp` | ISO 8601 timestamp |

#### Scheduler Integration

- Quartz scheduler uses cron expressions from `PullJobConfig.Schedule`
- Startup loads all enabled jobs and registers a `CronTrigger` for each
- `PullJobQuartzJob` adapter executes connector + state service sequentially

#### Pull Job State Service

- Persists `PullJobConfig` by `JobId` via `IMetadataStore`
- Lists jobs by `TenantId` via `IMetadataStore.QueryAsync<PullJobConfig>()`
- Updates state atomically per job to prevent duplicate pulls

#### Test Coverage Breakdown

| Suite | Tests | File |
|---|---|---|
| AwsS3Connector | 8 | `Connectors/AwsS3ConnectorTests.cs` |
| AzureBlobConnector | 8 | `Connectors/AzureBlobConnectorTests.cs` |
| CloudWatchConnector | 6 | `Connectors/CloudWatchConnectorTests.cs` |
| HttpConnector | 4 | `Connectors/HttpConnectorTests.cs` |
| PullJobStateService | 4 | `State/PullJobStateServiceTests.cs` |
| PullJobScheduler | 2 | `Scheduler/PullJobSchedulerTests.cs` |
| **Total** | **32** | |

#### Key Learnings

1. **Connector abstraction is clean:** Four distinct source patterns (S3, Blob, API, Logs API) unified via `IPullConnector`
2. **State isolation per job:** Each job maintains independent cursor position — enables parallel pulls without coordination
3. **Quartz integration is straightforward:** Cron expressions provide flexible scheduling; no custom timer needed
4. **Metrics emission confirmed:** Pullers emit pull.frequency, pull.connector.latency, pull.items.processed counters

---

## Phase 7: Logs2Obs.QueryEngine — Query Routing & Cost Estimation

### Core QueryEngine Implementation (Dolores Phase 7)

**QueryService Architecture:**
- Implements `IQueryService` with full tier routing (Hot/Warm/Cold) based on retention days
- `ExecuteSqlQueryHandler` enforces SQL safety validation, applies time-range detection, cost estimation guardrails, and cross-tier fan-out
- Cross-tier fan-out uses `Task.WhenAll` to execute subqueries in parallel; appends `timestamp` range filters per tier before submission
- Result synthesis merges JSON array result locations when multiple tiers are queried

**Supporting Services:**
- `SavedQueryService` persists queries using metadata-store keys prefixed with `savedquery:{tenantId}:{queryId}`
- `ScheduledReportService` manages scheduled reports with tier configuration (defaults: hot=7 days, warm=90 days)
- `QueryEngineMetrics` emits cost estimation counters (cost.estimate, cost.confirmed)
- `SqlParser` extracts ISO timestamps from WHERE clauses for automatic tier selection

**Cost Guardrails:**
- Cost estimates trigger `PendingCostConfirmation` response when exceeding tenant confirmation threshold
- Always call `EstimateCostAsync` before any query submission, regardless of tier

### QueryEngine Test Suite (Stubbs Phase 7 & Wire)

**Test Coverage: 32 tests passing**
- `QueryTierRouterTests` (6): Hot/warm/cold routing, full-text override, no time-range default
- `SqlSafetyValidatorTests` (7): SELECT-only allowance, forbidden keywords, LIMIT analysis warnings
- `QueryServiceTests` (6): Tier routing contracts, cost confirmation thresholds
- `SavedQueryServiceTests` (5): Persistence and retrieval via metadata store
- `ScheduledReportServiceTests` (4): Report scheduling and execution
- `QueryEngineMetricsTests` (4): Cost and latency metric emission

**Test Assumptions:**
- HotRetentionDays = 7, WarmRetentionDays = 90
- Tests reference only `Logs2Obs.Core` contracts; no circular dependencies

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

---

## Phase 11: Logs2Obs.Adapters.Aws — AWS Cloud Adapters

### Core AWS Adapters Implementation

**Project Structure:**
- Logs2Obs.Adapters.Aws (11 adapter implementations)
- Logs2Obs.Adapters.Aws.Tests (7 active test classes + 21 skipped tests)

**Adapter Implementations:**
1. **S3ObjectStore** — Implements IObjectStore using Amazon S3 for log blob storage
2. **AwsSnsMessageBus** — Implements IMessageBus using SNS for pub/sub messaging
3. **AwsSqsSubscriber** — Implements IMessageSubscriber using SQS for message consumption
4. **DynamoMetadataStore** — Implements IMetadataStore using DynamoDB single-table design
5. **DynamoSchemaRegistry** — Implements ISchemaRegistry using DynamoDB
6. **AthenaQueryEngine** — Implements IQueryEngine using Athena for SQL queries on S3
7. **OpenSearchIndexer** — Implements IIndexer for log search
8. **ElastiCacheIdempotencyStore** — Implements IIdempotencyStore for deduplication
9. **SecretsManagerSecretStore** — Implements ISecretStore for credential management
10. **EventBridgeScheduler** — Implements IScheduler for scheduled tasks
11. **AwsAdaptersServiceCollectionExtensions** — DI configuration

**DynamoDB Design Patterns:**
- **Single-Table Design:** All entities stored in one DynamoDB table
- **Composite Key Pattern:** PK format = {table}#{key} (e.g., metadata#tenant-1-schema-id)
- **Sort Key:** Optional timestamp or entity type for range queries

**Key Test Implementation Detail:**
- **DynamoMetadataStoreTests.RequestHasKey()** — Uses Contains() (not Equals()) for assertions on composite PK values
- This accounts for the {table}#{key} pattern where full key = prefix + separator + actual key

**Test Results:**
- 7 active test classes with 192 total passing tests
- 21 skipped tests (marked [Skip] for integration-only scenarios)
- 0 failed tests
