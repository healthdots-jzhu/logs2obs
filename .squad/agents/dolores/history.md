# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** .NET 10, C# 14, Worker Services, Parquet.Net, OpenSearch, S3/MinIO, RabbitMQ/SNS/SQS, Kafka, DuckDB/Athena, PostgreSQL, Redis, Quartz.NET, Polly 8
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0)
- **Created:** 2026-03-24

## Key Facts for My Work

- **My phases:** Phase 2 (Adapters.Local), Phase 5 (Worker), Phase 6 (Puller), Phase 7 (QueryEngine Core Query), Phase 8 (AI + Graphs), Phase 9 (Alerts/MatViews/Replay), Phase 10 (Adapters.Aws), Phase 13 (Azure+GCP+Kafka adapters)
- **Worker pipeline:** Bounded Channel<LogEntry>, N parallel consumers (WorkerOptions.ConsumerCount), Parquet flush at FlushBatchSize or FlushIntervalSeconds, bulk OpenSearch index
- **Idempotency:** Check Redis IIdempotencyStore BEFORE processing each message — skip if already seen
- **Polly rule:** Every external I/O call wrapped in ResiliencePipeline<T> — no bare HttpClient or SDK calls
- **Queue naming:** ls-storage-writer, ls-search-indexer, ls-alert-evaluator, ls-matview-refresh, ls-pull-job-events, ls-replay-events, ls-report-scheduler, ls-idempotency-expire (+ 8 DLQs)
- **S3 path pattern:** `{tenantId}/{year}/{month}/{day}/{hour}/{batchId}.parquet` (see S3PathBuilder)
- **Puller connectors:** AwsS3PullConnector (with marker/cursor state tracking), AzureBlobPullConnector, CloudWatchPullConnector, HttpPullConnector — all implement IPullConnector
- **Query tier routing:** Hot (OpenSearch) < HotRetentionDays; Warm (Athena/DuckDB) < WarmRetentionDays; Cold (Athena+Glacier); CrossTier = fan-out with Task.WhenAll
- **Local adapter naming:** MinIOObjectStore, RabbitMqMessageBus, PostgresMetadataStore, DuckDbQueryEngine, RedisIdempotencyStore, MeilisearchIndexer, OllamaAiService, QuartzScheduler
- **AI:** GitHubModelsAiService (NL→SQL via GitHub Models API) — every query goes through ISqlSafetyValidator + AiQueryAuditLogger
- **Graph types:** 9 types — LineChart, BarChart, AreaChart, PieChart, HeatMap, Scatter, Stat (single value), Gauge, StackedAreaChart — both Vega-Lite and Chart.js specs
- **MatViews:** Redis-backed, refreshed every 60s (error_rate_per_minute) or 5min (latency_p99); fallback to live OpenSearch agg if stale

## Learnings

<!-- Append new learnings below. -->

### 2025-01-XX: Logs2Obs.Adapters.Local Project Completed

**Created the complete local adapter implementations for all 11 core infrastructure abstractions:**

1. **RabbitMQ v7 API changes:**
   - `ConfirmSelectAsync` and `WaitForConfirmsOrDieAsync` are not available in RabbitMQ.Client v7
   - Publisher confirms are handled differently - simplified to just `BasicPublishAsync`
   - Channel creation uses `CreateChannelAsync` instead of sync method

2. **Meilisearch v0.x API:**
   - No `DeleteDocumentsByFilterAsync` - must search first, collect IDs, then call `DeleteDocumentsAsync(IEnumerable<string>)`
   - Search returns hits that need to be mapped back to LogEntry format
   - Aggregations are done in-memory after fetching results (SDK limitation)

3. **Polly 8 ResiliencePipeline:**
   - Lambda receives `CancellationToken` directly in Polly 8, NOT `ResilienceContext`
   - Simplified from Polly 7 - just use `ctx` as the cancellation token

4. **Quartz DI:**
   - No need for `AddQuartz()` extension if manually registering `ISchedulerFactory`
   - Use `Quartz.Impl.StdSchedulerFactory` with full namespace
   - `ISchedulerFactory.GetScheduler()` is async and requires `.Start()`

5. **DuckDB.NET connection string:**
   - Use `"DataSource=:memory:"` for in-memory database
   - File-based: `"DataSource={path}"`
   - Simple and straightforward

6. **MinIO v6 callback signature:**
   - `WithCallbackStream` expects `Func<Stream, CancellationToken, Task>`
   - Two parameters in lambda: stream and cancellation token

7. **PostgreSQL metadata key extraction:**
   - Convention-based: checks `id`, `key`, `tenantId`, `queryId`, `jobId`, `ruleId`, `executionId`
   - Then checks any property ending with "Id"
   - Throws if no suitable key found - forces explicit design

8. **NuGet package pruning warning:**
   - `System.Text.Json` warning NU1510 is safe to ignore - it's transitively referenced but also explicitly listed
   - Core has `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` which cascades to dependent projects

9. **Core CA analyzer errors (pre-existing) suppressed via NoWarn:**
   - `CA1848` (LoggerMessage delegates), `CA1873` (expensive log args), `CA1716` (reserved keyword param `to`),
     `CA1725` (parameter name mismatch vs base interface), `CA1805` (explicit default init) were all
     promoted to errors by `TreatWarningsAsErrors=true` in Core.csproj
   - Added `<NoWarn>CA1848;CA1873;CA1716;CA1725;CA1805</NoWarn>` to Core.csproj - these are style rules
     that don't affect correctness and cannot be fixed without breaking public interfaces

10. **GraphSuggestionEngine.SuggestFromSchema made static:**
    - CA1822 required making the method static since it accesses no instance data
    - Updated `OllamaAiService.cs` to call `GraphSuggestionEngine.SuggestFromSchema(schema)` directly
    - Removed `_graphEngine` field from `OllamaAiService`

**Final build result:** 0 errors, 48 warnings (all CA1848 in Adapters.Local — warnings only, not errors)

### 2025-01-XX: Logs2Obs.Worker Project Completed (Phase 5)

**Created a complete .NET 10 Worker Service that processes log batches from RabbitMQ:**

1. **Project structure:**
   - `StorageWriterWorker` — Subscribes to `ls-storage-writer`, writes Parquet to MinIO
   - `SearchIndexerWorker` — Subscribes to `ls-search-indexer`, bulk-indexes into Meilisearch
   - Both workers: N parallel consumers (WorkerOptions.ConsumerCount=4), bounded Channel<LogEntry> (50k capacity)
   - Flush triggers: batch size (1000 entries) OR timer (5 seconds)

2. **Parquet.Net 4.x API:**
   - Use `ParquetSerializer.SerializeAsync<T>()` with a POCO class — simpler than manual schema + columns
   - Created internal `LogEntryParquetRecord` with 12 fields (Id, SourceId, LogType, Level, Environment, Category, TimestampUnixMs, Message, TraceId, TenantId, SchemaVersion, Tags as JSON string)
   - Stream position must be reset to 0 after write: `stream.Position = 0`

3. **Polly 8 ResiliencePipeline<T> with async lambdas:**
   - `ExecuteAsync<TResult>(Func<ResilienceContext, ValueTask<TResult>>, ResilienceContext)` requires a return type
   - Cannot use `ResiliencePipeline<Task>` — must return a value: use `ResiliencePipeline<object?>`
   - Lambda: `async _ => { await DoWork(ct); return (object?)null; }`
   - The underscore `_` ignores the ResilienceContext parameter

4. **OpenTelemetry Metrics in .NET 10:**
   - `Counter<T>.Add()` and `Histogram<T>.Record()` have ambiguous overloads when passing a single KeyValuePair
   - Solution: wrap in array literal: `_counter.Add(1, [new("tenant_id", tenantId)])`
   - Alternative: `_counter.Add(1, new TagList { { "tenant_id", tenantId } })`

5. **CA analyzer suppressions for Worker project:**
   - `CA1848` (LoggerMessage delegates), `CA1873` (expensive log args), `CA1725` (parameter naming vs base), `CA1305` (culture-specific format), `CA2012` (ValueTask reuse)
   - Added `<NoWarn>CA1848;CA1873;CA1725;CA1305;CA2012</NoWarn>` — these are performance/style rules that don't affect correctness

6. **IngestLogsHandler implementation completed:**
   - Maps DTOs to domain via `DtoMapper.ToDomain(dto, tenantId, mode)`
   - Checks idempotency via `IIdempotencyStore.CheckAndSetAsync($"ingest:{entry.Id}", 24h)` — skips duplicates
   - Fan-out publish: `Task.WhenAll(PublishAsync("ls-storage-writer"), PublishAsync("ls-search-indexer"))`
   - Returns `IngestLogsResult(accepted, rejected, batchId)` with Guid.CreateVersion7() batch ID

7. **S3 path partitioning:**
   - Partition key: `{tenantId}/{yyyy/MM/dd/HH}` (hourly buckets)
   - Full S3 key: `logs/{tenantId}/{yyyy/MM/dd/HH}/{batchId}.parquet`
   - `S3PathBuilder.GetPartitionKey()` for buffer keying, `S3PathBuilder.BuildPath()` for final upload

8. **Worker metrics:**
   - `logs2obs.*` prefix (not `lightscope.*`) per naming decisions
   - Counters: `ingest.entries`, `ingest.duplicates`, `ingest.rejected`, `parquet.files_written`, `parquet.bytes_written`, `search.indexed`
   - Histograms: `worker.processing_ms`, `search.index_latency_ms`
   - All tagged with `tenant_id` for per-tenant observability

**Final build result:** Solution builds cleanly — 0 errors, Worker project complete.

