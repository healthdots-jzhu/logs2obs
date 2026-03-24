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
