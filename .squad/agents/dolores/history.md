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
