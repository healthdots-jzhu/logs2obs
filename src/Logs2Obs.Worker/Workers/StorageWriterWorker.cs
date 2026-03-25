namespace Logs2Obs.Worker.Workers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Worker.Models;
using Logs2Obs.Worker.Options;
using Logs2Obs.Worker.Parquet;
using Logs2Obs.Worker.Storage;
using Logs2Obs.Worker.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

public sealed class StorageWriterWorker : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IObjectStore _objectStore;
    private readonly IParquetWriter _parquetWriter;
    private readonly WorkerOptions _options;
    private readonly WorkerMetrics _metrics;
    private readonly ILogger<StorageWriterWorker> _logger;
    private readonly Channel<LogEntry> _channel;
    private readonly ConcurrentDictionary<string, List<LogEntry>> _partitionBuffers = new();

    public StorageWriterWorker(
        IMessageBus messageBus,
        IIdempotencyStore idempotencyStore,
        IObjectStore objectStore,
        IParquetWriter parquetWriter,
        IOptions<WorkerOptions> options,
        WorkerMetrics metrics,
        ILogger<StorageWriterWorker> logger)
    {
        _messageBus = messageBus;
        _idempotencyStore = idempotencyStore;
        _objectStore = objectStore;
        _parquetWriter = parquetWriter;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;

        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("StorageWriterWorker starting with {ConsumerCount} consumers", _options.ConsumerCount);

        var consumerTasks = Enumerable.Range(0, _options.ConsumerCount)
            .Select(i => ConsumeFromQueueAsync(i, ct))
            .ToList();

        var batchWriterTask = BatchWriterAsync(ct);

        await Task.WhenAll(consumerTasks.Append(batchWriterTask));
    }

    private async Task ConsumeFromQueueAsync(int consumerId, CancellationToken ct)
    {
        _logger.LogInformation("Consumer {ConsumerId} starting", consumerId);

        try
        {
            await foreach (var envelope in _messageBus.SubscribeAsync<LogEntryBatch>(_options.StorageWriterQueue, ct))
            {
                var sw = Stopwatch.StartNew();
                var batch = envelope.Payload;

                await Parallel.ForEachAsync(
                    batch.Entries,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _options.MaxParallelism,
                        CancellationToken = ct
                    },
                    async (entry, ct) =>
                    {
                        try
                        {
                            var idempotencyKey = $"storage:{entry.Id}";
                            var isNew = await _idempotencyStore.CheckAndSetAsync(
                                idempotencyKey,
                                TimeSpan.FromHours(24),
                                ct);

                            if (!isNew)
                            {
                                _metrics.DuplicateCounter.Add(1, [new("tenant_id", entry.TenantId)]);
                                _logger.LogDebug("Duplicate entry {EntryId} skipped", entry.Id);
                                return;
                            }

                            await _channel.Writer.WriteAsync(entry, ct);
                            _metrics.IngestCounter.Add(1, [new("tenant_id", entry.TenantId)]);
                        }
                        catch (Exception ex)
                        {
                            _metrics.RejectedCounter.Add(1, [new("tenant_id", entry.TenantId)]);
                            _logger.LogError(ex, "Failed to process entry {EntryId}", entry.Id);
                            throw;
                        }
                    });

                await _messageBus.AcknowledgeAsync(envelope.ReceiptHandle, ct);
                _metrics.ProcessingLatency.Record(sw.Elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Consumer {ConsumerId} stopped", consumerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consumer {ConsumerId} failed", consumerId);
            throw;
        }
    }

    private async Task BatchWriterAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.FlushIntervalSeconds));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var timerTask = timer.WaitForNextTickAsync(ct);
                var readTask = _channel.Reader.WaitToReadAsync(ct);

                var completed = await Task.WhenAny(timerTask.AsTask(), readTask.AsTask());

                if (completed == readTask.AsTask())
                {
                    while (_channel.Reader.TryRead(out var entry))
                    {
                        var partitionKey = S3PathBuilder.GetPartitionKey(entry);

                        _partitionBuffers.AddOrUpdate(
                            partitionKey,
                            _ => new List<LogEntry> { entry },
                            (_, list) =>
                            {
                                lock (list)
                                {
                                    list.Add(entry);
                                }
                                return list;
                            });

                        if (_partitionBuffers[partitionKey].Count >= _options.BatchSize)
                        {
                            await FlushPartitionAsync(partitionKey, ct);
                        }
                    }
                }

                if (completed == timerTask.AsTask())
                {
                    _logger.LogDebug("Flushing {PartitionCount} partitions on timer", _partitionBuffers.Count);
                    await FlushAllPartitionsAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("BatchWriter stopped, flushing remaining entries");
            await FlushAllPartitionsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchWriter failed");
            throw;
        }
    }

    private async Task FlushAllPartitionsAsync(CancellationToken ct)
    {
        var keys = _partitionBuffers.Keys.ToList();
        foreach (var key in keys)
        {
            await FlushPartitionAsync(key, ct);
        }
    }

    private async Task FlushPartitionAsync(string partitionKey, CancellationToken ct)
    {
        if (!_partitionBuffers.TryRemove(partitionKey, out var buffer) || buffer.Count == 0)
        {
            return;
        }

        List<LogEntry> snapshot;
        lock (buffer)
        {
            snapshot = new List<LogEntry>(buffer);
            buffer.Clear();
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        try
        {
            var parquetStream = await _parquetWriter.WriteAsync(snapshot, ct);
            var s3Key = S3PathBuilder.BuildPath(snapshot[0]);

            await ResiliencePipelines.ForStorage<object?>().ExecuteAsync(async _ =>
            {
                await _objectStore.WriteAsync(s3Key, parquetStream, "application/octet-stream", ct);
                return (object?)null;
            }, ct);

            _metrics.ParquetFilesWritten.Add(1);
            _metrics.ParquetBytesWritten.Add(parquetStream.Length);

            _logger.LogInformation("Flushed {Count} entries to {Key}", snapshot.Count, s3Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush partition {PartitionKey}", partitionKey);

            foreach (var entry in snapshot)
            {
                if (_partitionBuffers.TryGetValue(partitionKey, out var list))
                {
                    lock (list)
                    {
                        list.Add(entry);
                    }
                }
            }

            throw;
        }
    }
}
