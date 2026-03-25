# Logs2Obs.Worker

.NET 10 Worker Service that consumes log batches from RabbitMQ, writes Parquet files to MinIO, and bulk-indexes entries into Meilisearch.

## Overview

Two parallel workers:
1. **StorageWriterWorker**: Consumes from `ls-storage-writer`, checks idempotency (Redis), writes Parquet to MinIO
2. **SearchIndexerWorker**: Consumes from `ls-search-indexer`, checks idempotency (Redis), bulk-indexes into Meilisearch

## Configuration

Environment variables or `appsettings.json`:
- `Worker:ConsumerCount` — Parallel consumer count per worker (default: 4)
- `Worker:BatchSize` — Entries per batch before flush (default: 1000)
- `Worker:FlushIntervalSeconds` — Flush interval (default: 5)
- `Worker:ChannelCapacity` — Channel buffer size (default: 50000)
- `Worker:MaxParallelism` — Parallel processing degree (default: 8)
- `Worker:StorageWriterQueue` — Storage queue name (default: `ls-storage-writer`)
- `Worker:SearchIndexerQueue` — Search queue name (default: `ls-search-indexer`)

## Dependencies

- **RabbitMQ**: Message bus (localhost:5672)
- **Redis**: Idempotency store (localhost:6379)
- **MinIO**: Object storage (localhost:9000)
- **Meilisearch**: Search indexer (localhost:7700)

## Running Locally

```bash
dotnet run --project src/Logs2Obs.Worker
```

Metrics exposed at `http://localhost:8080/metrics` (Prometheus format).

## Metrics

- `logs2obs.ingest.entries` — Entries ingested
- `logs2obs.ingest.duplicates` — Duplicates skipped
- `logs2obs.ingest.rejected` — Entries rejected
- `logs2obs.worker.processing_ms` — Processing latency histogram
- `logs2obs.parquet.files_written` — Parquet files written
- `logs2obs.parquet.bytes_written` — Parquet bytes written
- `logs2obs.search.indexed` — Entries indexed
- `logs2obs.search.index_latency_ms` — Indexing latency histogram
