# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** xUnit 2, Moq 4, Testcontainers 3, FluentAssertions 7, Docker Compose (for integration tests)
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0)
- **Created:** 2026-03-24

## Key Facts for My Work

- **My phase:** Phase 12 (Full Test Suite) — runs against Phases 1–11 outputs
- **Test projects:** LightScope.Core.Tests, LightScope.Api.Tests, LightScope.Worker.Tests, LightScope.Puller.Tests, LightScope.Integration.Tests
- **Assertions style:** FluentAssertions ALWAYS — `result.Should().Be(expected)` — never `Assert.Equal`
- **Test naming:** `{Method}_{Scenario}_{ExpectedResult}` — e.g., `Validate_WhenLogTypeIsInvalid_ReturnsFalse`
- **Required unit tests (from Section 27.10):**
  - SqlSafetyValidator: DROP, DELETE, INSERT, CREATE, ALTER, CROSS JOIN, no partition filter, no LIMIT, valid SELECT
  - QueryTierRouter: each of Hot / Warm / Cold / CrossTier routing rules
  - DtoMapper.ToDomain: TenantId never from DTO, Id always new UUIDv7, IngestedAt always UtcNow
  - ApiKeyAuthHandler: valid key (cache hit), valid key (DB hit), invalid key, inactive key, missing header
  - LogEntryDtoValidator: all validation rules from Section 27.9
- **Integration tests use Testcontainers:** real PostgreSQL (Npgsql), real Redis (StackExchange.Redis), real MinIO (S3-compat)
- **End-to-end test:** ingest batch → RabbitMQ → Worker → MinIO → DuckDB query → assert result
- **Test runner command:** `dotnet test` against the docker-compose local stack
- **Coverage targets:** Every interface has at least one test file; SqlSafetyValidator and QueryTierRouter are the highest-priority test targets

## Learnings

<!-- Append new learnings below. -->

### 2026-03-25 — Logs2Obs.Puller.Tests scaffolded (anticipatory, pre-Puller)

**Files created** (all under `tests/Logs2Obs.Puller.Tests/`):
- `Logs2Obs.Puller.Tests.csproj` — xUnit 2, Moq 4, FluentAssertions 7, MS.Extensions.DependencyInjection 9, MS.Extensions.Logging.Abstractions 9; `<Using Remove="Microsoft.Extensions.Logging" />`
- `xunit.runner.json` — `parallelizeAssembly: true`, `parallelizeTestCollections: true`
- `GlobalUsings.cs` — xUnit, FluentAssertions, Moq, Logs2Obs.Core.Models, Logs2Obs.Core.Abstractions
- `Helpers/TestDataBuilders.cs` — `AValidPullJobConfig(jobId, tenantId, connectorType, schedule)`, `AValidLogEntry(tenantId)`
- `Pipeline/PullJobPipelineTests.cs` — 4 ACTIVE tests: IPullConnector contract tests (PullAsync yields entries, GetStateAsync null handling, SaveAndGetState round-trip, PullJobConfig validation)
- `Connectors/AwsS3PullConnectorTests.cs` — 7 tests (all skipped): pull objects, empty bucket, retry logic, state get/save, filter by lastProcessedKey
- `Connectors/AzureBlobPullConnectorTests.cs` — 4 tests (all skipped): pull blobs, empty container, state get/save
- `Connectors/CloudWatchPullConnectorTests.cs` — 4 tests (all skipped): pull logs, 500 retry, filter since timestamp, save lastEventTimestamp
- `Connectors/HttpPullConnectorTests.cs` — 5 tests (all skipped): NDJSON pull, API key header, 429 backoff, 404 exception, save lastPullTimestamp
- `Scheduling/PullJobSchedulerTests.cs` — 3 tests (all skipped): schedule with Quartz CronTrigger, unschedule job, startup load enabled jobs
- `Services/PullJobStateServiceTests.cs` — 5 tests (all skipped): GetJobAsync, SaveJobAsync, ListJobsAsync, DeleteJobAsync

**Test count:** 32 tests total (4 active passing, 28 skipped pending Dolores Phase 6)

**Build result (2026-03-25):** SUCCESS — 0 errors, 10 warnings (unused variables in skipped test TODOs — acceptable). Compiles cleanly against Logs2Obs.Core only.

**Test result:** ✅ 4 passing, 28 skipped. All active tests validate IPullConnector interface contract using Moq.

**Key design decisions:**
- Active tests (Pipeline) test Core abstractions only — no dependency on Dolores's implementations
- Skipped tests await concrete connector implementations (AwsS3PullConnector, AzureBlobPullConnector, etc.)
- All skipped tests marked with `[Fact(Skip = "Awaiting Dolores Phase 6")]`
- State persistence contract: `GetStateAsync` returns `null` when no state exists; `SaveStateAsync` stores arbitrary key-value dict
- Each connector uses different state keys: AwsS3 (`lastProcessedKey`), CloudWatch/Http (`lastEventTimestamp`/`lastPullTimestamp`), AzureBlob (`lastBlobName`)
- PullAsync contract: `since` parameter filters by timestamp, yields `LogEntry` with `IngestionMode.Pull`, handles empty sources gracefully
- Scheduler assumptions: Quartz.NET with `CronTrigger`, loads enabled jobs on startup from `IMetadataStore`
- State service wraps `IMetadataStore` for CRUD on `PullJobConfig` records

**LogLevel enum correction:** Bernard's Core uses `LogLevel.Information` (NOT `Info`). Updated `TestDataBuilders.AValidLogEntry()` to use correct enum value.

**Assumptions about Dolores's implementation** — see `.squad/decisions/inbox/stubbs-phase6-puller-tests.md`

### 2026-03-24 — Logs2Obs.Adapters.Local.Tests scaffolded (anticipatory, pre-adapters)

**Files created** (all under `tests/Logs2Obs.Adapters.Local.Tests/`):
- `Logs2Obs.Adapters.Local.Tests.csproj` — xUnit 2, FluentAssertions 7, Testcontainers 3 (PostgreSql, Redis, Minio, RabbitMq), MS.Extensions.Options 10, MS.Extensions.Logging.Abstractions 10
- `xunit.runner.json` — `parallelizeTestCollections: true`, `maxParallelThreads: 4`
- `Fixtures/PostgreSqlFixture.cs` — `PostgreSqlBuilder`, exposes `ConnectionString`
- `Fixtures/RedisFixture.cs` — `RedisBuilder`, exposes `ConnectionString`
- `Fixtures/MinioFixture.cs` — `MinioBuilder`, exposes `Endpoint`, `AccessKey`, `SecretKey`
- `Fixtures/RabbitMqFixture.cs` — `RabbitMqBuilder`, exposes `ConnectionString`
- `Collections/TestCollections.cs` — `[CollectionDefinition]` for all 4 fixture types
- `ObjectStore/MinioObjectStoreTests.cs` — 6 integration tests using `MinioFixture`
- `MessageBus/InProcessChannelMessageBusTests.cs` — 4 unit tests, uses `Task.WhenAll`/`CancellationTokenSource` timeout
- `MetadataStore/PostgresMetadataStoreTests.cs` — 5 integration tests using `PostgreSqlFixture`
- `Idempotency/RedisIdempotencyStoreTests.cs` — 4 integration tests using `RedisFixture`
- `QueryEngine/DuckDbQueryEngineTests.cs` — 3 unit tests (DuckDB in-process, `:memory:`)
- `Secrets/LocalSecretStoreTests.cs` — 3 unit tests (in-memory `ConcurrentDictionary`)

**Test count:** 25 tests total (6 + 4 + 5 + 4 + 3 + 3)

**Testcontainer fixture pattern used:** `IAsyncLifetime` with `[CollectionDefinition]` / `[Collection]` — one container per test collection, shared across tests in the collection. Each test uses `Guid.NewGuid()` keys to avoid cross-test interference.

**Build result (2026-03-24):** BLOCKED — two independent issues:
1. **Pre-existing CA errors in Logs2Obs.Core (31 errors)** — `TreatWarningsAsErrors=true` + `AnalysisMode=Recommended` cause CA1848, CA1873, CA1725, CA1716, CA1822 to fail the Core build. NOT caused by this scaffold. Affects Core.Tests as well. Bernard needs to fix.
2. **Logs2Obs.Adapters.Local project missing** — expected; blocked on Dolores completing Phase 2 adapters.

**Namespace assumptions for adapter types** — see `.squad/decisions/inbox/stubbs-adapter-test-assumptions.md`

### 2026-03-24 — LightScope.Core.Tests scaffolded (anticipatory, pre-Core)

**Files created** (all under `tests/LightScope.Core.Tests/`):
- `LightScope.Core.Tests.csproj` — xUnit 2, Moq 4, FluentAssertions 7, Testcontainers 3, FluentValidation 11, MS.Extensions.Logging.Abstractions 10
- `Query/SqlSafetyValidatorTests.cs` — 12 tests; forbidden keywords (DDL/DML), CROSS JOIN, partition filter, LIMIT, valid SELECT
- `Query/QueryTierRouterTests.cs` — 9 tests; all 5 tier routes + full-text override, CrossTier sub-query assertions, custom retention config
- `Mapping/DtoMapperTests.cs` — 10 tests; TenantId/Id/IngestedAt/IngestionMode contract, ToDto round-trip
- `Validation/LogEntryDtoValidatorTests.cs` — 25 tests; all FluentValidation rules from Section 27.9
- `Storage/S3PathBuilderTests.cs` — 15 tests; partition key format (Hive-style), Build path shape, uniqueness
- `Graphs/GraphSuggestionEngineTests.cs` — 17 tests; every rule from Section 17.1, priority ordering, empty schema
- `Helpers/TestDataBuilders.cs` — `AValidLogEntryDto()`, `AValidTenantSettings()`, `AValidMetricDto()`

**Key design references used:** Sections 7.2, 15.1, 16.2, 17.1, 27.9, 27.10 of `LightScope_Design_v3.md`

**Assumptions about Core API surface** — see `.squad/decisions.md` (merged from inbox 2026-03-24T19-24-08)

### 2026-03-24 — Bernard confirmed Core API surface (Phase 1 complete)

Bernard completed Logs2Obs.Core (79 files, 0 errors). The following assumptions from the test scaffold are now confirmed:

- **`LogLevel` enum values:** `Trace, Debug, Info, Warn, Error, Fatal` — Bernard used `Info` (NOT `Information`), matching design doc §7.1. `TestDataBuilders.AValidLogEntryDto()` should use `"Info"`, not `"Information"`. Update if tests fail on validator enum parse.
- **`DtoMapper.ToDto` exists:** Bernard implemented the reverse `public static LogEntryDto ToDto(LogEntry domain)` method. The `DtoMapperTests.ToDto_RoundTrip_*` tests will compile.
- **`MetricDto` naming:** Both `MetricDto` (with `MetricName, Unit, Value, MetricType`) and `MetricPayloadDto` (used on `LogEntryDto.Metric`) are present in `Logs2Obs.Core.Models`. Use `MetricDto` for standalone metric objects; use `MetricPayloadDto` for the nested property on `LogEntryDto`.

### 2026-03-25 — Logs2Obs.Api.Tests scaffolded (anticipatory, pre-API)

**Files created** (all under `tests/Logs2Obs.Api.Tests/`):
- `Logs2Obs.Api.Tests.csproj` — xUnit 2, FluentAssertions 7, Moq 4, MS.AspNetCore.Mvc.Testing 10, MS.Extensions.Logging.Abstractions 10, MS.Extensions.Caching.Memory 10; suppresses CA1707 (test naming convention)
- `xunit.runner.json` — `methodDisplay: "method"`, `methodDisplayOptions: "all"`
- `GlobalUsings.cs` — xUnit, FluentAssertions, Moq, Logs2Obs.Core.Models, Logs2Obs.Core.Abstractions
- `Auth/ApiKeyAuthHandlerTests.cs` — 7 tests (all skipped awaiting Maeve Phase 4): cache hit, cache miss, TenantId claim, invalid key, inactive key, missing header, cache duration
- `RateLimiting/TenantRateLimiterTests.cs` — 6 tests (active): TokenBucket under/over/replenishment, SlidingWindow under/over, tenant isolation
- `Middleware/PayloadSizeMiddlewareTests.cs` — 4 tests (all skipped): under limit, over limit (413), exactly at limit, bulk endpoint exempt
- `Middleware/GlobalExceptionHandlerTests.cs` — 4 tests (all skipped): ValidationException → 400, UnauthorizedAccessException → 401, unhandled → 500 with correlation ID, no stack trace leak
- `Helpers/TestDataBuilders.cs` — `AValidApiKey()`, `AValidTenantId()`, `AValidIngestRequest(count)`, `AValidLogEntryDto()`

**Test count:** 21 tests total (7 + 6 + 4 + 4); 6 active, 15 skipped pending Maeve's API project.

**Build result (2026-03-25):** SUCCESS — 0 errors. Project compiles cleanly against Logs2Obs.Core only.

**Key design decisions:**
- `IMetadataStore.GetAsync<T>(string table, string key, CancellationToken ct)` — signature requires table+key; ApiKeyAuthHandler will query `GetAsync<ApiKeyRecord>("apikeys", apiKey, ct)`
- `ApiKeyRecord` defined inline as test model with `TenantId`, `IsActive` properties
- Rate limiter tests use `System.Threading.RateLimiting` directly (TokenBucketRateLimiter, SlidingWindowRateLimiter) — tests algorithm in isolation, not middleware binding
- Middleware tests use `DefaultHttpContext` for unit-level testing without TestServer
- CA1707 (no underscores in names) suppressed in csproj — test naming convention `{Method}_{Scenario}_{ExpectedResult}` is standard for xUnit

**Assumptions about Maeve's implementation** — see `.squad/decisions/inbox/stubbs-phase4-api-tests.md`

### 2026-03-25 — Maeve Phase 4 API tests wired to actual implementations

**Context:** Maeve completed `src/Logs2Obs.Api/` with `ApiKeyAuthHandler`, `PayloadSizeMiddleware`, and `GlobalExceptionHandler`. Enabled all 15 skipped API tests.

**Actual API surface (vs. assumptions):**

1. **`ApiKeyAuthHandler` constructor:**
   - Actual: `(IOptionsMonitor<ApiKeyAuthOptions>, ILoggerFactory, UrlEncoder, IMemoryCache, IMetadataStore, ILogger<ApiKeyAuthHandler>)`
   - Inherits from `AuthenticationHandler<ApiKeyAuthOptions>` — requires `IOptionsMonitor`, `ILoggerFactory`, `UrlEncoder`
   - My assumption was simpler, missing authentication base class requirements

2. **Metadata structure:**
   - Actual: `Dictionary<string, string>` with keys: `"tenantId"`, `"active"`, `"keyId"`
   - Table name: `"api_keys"` (not `"apikeys"`)
   - My assumed `ApiKeyRecord` typed model was wrong; replaced with raw dictionary in tests

3. **Cache usage:**
   - Actual: Uses `_cache.Set(cacheKey, (tenantId, keyId), options)` extension method
   - Moq can't mock extension methods — had to mock `CreateEntry()` instead and verify `ICacheEntry.Value` property set
   - Cache key format: `$"apikey:{apiKey}"` — confirmed correct

4. **`PayloadSizeMiddleware`:**
   - Actual: Path-based check (`/api/v1/logs` only), uses `IOptions<PayloadSizeOptions>`, `ILogger<PayloadSizeMiddleware>`
   - Constructor: `(RequestDelegate, IOptions<PayloadSizeOptions>, ILogger<PayloadSizeMiddleware>)`
   - Method: `InvokeAsync(HttpContext, CancellationToken)` — NOT `Invoke(HttpContext)`
   - Test needed to add `CancellationToken.None` to all `InvokeAsync` calls

5. **`GlobalExceptionHandler`:**
   - Actual: Implements `IExceptionHandler` (ASP.NET Core diagnostics), not custom middleware
   - Method: `TryHandleAsync(HttpContext, Exception, CancellationToken) -> ValueTask<bool>`
   - No logger in constructor — uses `Serilog.Log` static logger
   - Maps `FluentValidation.ValidationException`, `UnauthorizedAccessException`, `Logs2ObsException` subtypes, generic fallback

**Changes made:**
- Removed `ApiKeyRecord` test model; used `Dictionary<string, string>` directly
- Updated all metadata store table names: `"apikeys"` → `"api_keys"`
- Fixed `ApiKeyAuthHandler` test helper to construct with full authentication base class requirements
- Changed cache mock from `Set()` to `CreateEntry()` + verify `Value` property
- Added `CancellationToken.None` to all `PayloadSizeMiddleware.InvokeAsync` calls
- Changed exception handler tests from middleware pattern to `IExceptionHandler.TryHandleAsync`
- Used `FluentValidation.ValidationException` and `FluentValidation.Results.ValidationFailure` from actual package

**Final test results:**
- **21 tests total** (all enabled): ApiKeyAuthHandler (8), PayloadSizeMiddleware (4), GlobalExceptionHandler (4), RateLimiter (6 — already passing)
- **All 21 passed** — 0 skipped, 0 failed
- Build: 0 errors, 2 warnings (NU1510 in Adapters.Local — unrelated)

**Key learning:** Authentication handlers have significant base class infrastructure (options monitor, logger factory, URL encoder). Future auth tests should account for `AuthenticationHandler<TOptions>` base requirements from the start.

### 2026-03-25 — Logs2Obs.Worker.Tests scaffolded (anticipatory, pre-Worker)

**Files created** (all under `tests/Logs2Obs.Worker.Tests/`):
- `Logs2Obs.Worker.Tests.csproj` — xUnit 2, FluentAssertions 7, Moq 4, Testcontainers 3 (Redis, MinIO, RabbitMq), MS.Extensions.Logging.Abstractions 10, MS.Extensions.Options 10; `TreatWarningsAsErrors=false`, `<Using Remove="Microsoft.Extensions.Logging" />`
- `xunit.runner.json` — `methodDisplay: "method"`, `methodDisplayOptions: "all"`
- `GlobalUsings.cs` — xUnit, FluentAssertions, Moq, Core.Models, Core.Abstractions, NullLogger, IOptions
- `Workers/StorageWriterWorkerTests.cs` — 8 tests (all skipped): idempotency check, duplicate handling, batch flush (size + interval), object store failure (dead-letter), success (ACK), parallel consumer backpressure
- `Workers/SearchIndexerWorkerTests.cs` — 5 tests (all skipped): index batch, batch flush, failure (dead-letter after retries), success (ACK), multi-tenant indexing
- `Parquet/ParquetWriterTests.cs` — 4 tests (all skipped): valid entries → non-empty stream, empty list → empty Parquet header, schema validation, tags serialized as JSON
- `Telemetry/WorkerMetricsTests.cs` — 6 tests (all skipped): ingest counter, duplicate counter, processing latency histogram, tenant ID tag grouping, flush counter, error counter
- `Pipeline/ChannelBackpressureTests.cs` — 4 tests (ALL ACTIVE, 4 passing): bounded channel backpressure (wait, drain, no message drop), unbounded channel overflow demonstration
- `Helpers/TestDataBuilders.cs` — `AValidLogEntry(tenantId?, level?)`, `AValidMessageEnvelope<T>(payload, receiptHandle?)`, `AValidLogEntryBatch(count, tenantId?)`

**Test count:** 27 tests total (8 + 5 + 4 + 6 + 4); 4 active passing, 22 skipped pending Dolores Phase 5 Worker project.

**Build result (2026-03-25):** SUCCESS — 0 errors, 26 warnings (CA1707 test naming — acceptable). Compiles cleanly against Logs2Obs.Core only.

**Test result:** All 4 active channel tests pass. 22 Worker/Parquet/Telemetry tests skipped with `[Fact(Skip = "Awaiting Dolores Phase 5")]`.

**Key design decisions:**
- All Worker/Parquet/Telemetry tests use `Mock<T>` for `IMessageBus`, `IIdempotencyStore`, `IObjectStore`, `ISearchIndexer` — tests written against Core abstractions
- `IParquetWriter` stub interface defined in test helpers (will be replaced with actual Worker project interface)
- `WorkerOptions` stub record defined in test helpers (`BatchSize`, `FlushInterval`, `ParallelConsumers`)
- `LogEntryBatch` stub record defined in test helpers (payload type for message bus)
- Tests validate **contracts**, not implementations — e.g., `ISearchIndexer.IndexBatchAsync`, `IMessageBus.DeadLetterAsync`, `IObjectStore.WriteAsync(Task, not ValueTask)`
- Channel backpressure tests use `System.Threading.Channels` directly — no dependencies on Worker project
- `ChannelReader.Count` property not supported; use `TryRead()` loop to count items
- `ValueTask.AsTask()` + delay needed to test incomplete async write tasks

**Assumptions about Dolores's Worker implementation** — see `.squad/decisions/inbox/stubbs-phase5-worker-tests.md`

**Interface signatures confirmed from Core (used in mocks):**
- `ISearchIndexer.IndexBatchAsync(IReadOnlyList<LogEntry>, CancellationToken) → Task`
- `IMessageBus.SubscribeAsync<T>(string queue, CancellationToken) → IAsyncEnumerable<MessageEnvelope<T>>`
- `IMessageBus.AcknowledgeAsync(string receiptHandle, CancellationToken) → Task`
- `IMessageBus.DeadLetterAsync(string receiptHandle, string reason, CancellationToken) → Task`
- `IObjectStore.WriteAsync(string key, Stream content, string contentType, CancellationToken) → Task` (NOT ValueTask)
- `IIdempotencyStore.CheckAndSetAsync(string key, TimeSpan ttl, CancellationToken) → ValueTask<bool>`

