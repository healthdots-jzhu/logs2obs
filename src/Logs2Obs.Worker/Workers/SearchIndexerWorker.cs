namespace Logs2Obs.Worker.Workers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Worker.Models;
using Logs2Obs.Worker.Options;
using Logs2Obs.Worker.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

public sealed class SearchIndexerWorker : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ISearchIndexer _searchIndexer;
    private readonly WorkerOptions _options;
    private readonly WorkerMetrics _metrics;
    private readonly ILogger<SearchIndexerWorker> _logger;
    private readonly ConcurrentDictionary<string, List<LogEntry>> _tenantBuffers = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    public SearchIndexerWorker(
        IMessageBus messageBus,
        IIdempotencyStore idempotencyStore,
        ISearchIndexer searchIndexer,
        IOptions<WorkerOptions> options,
        WorkerMetrics metrics,
        ILogger<SearchIndexerWorker> logger)
    {
        _messageBus = messageBus;
        _idempotencyStore = idempotencyStore;
        _searchIndexer = searchIndexer;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("SearchIndexerWorker starting with {ConsumerCount} consumers", _options.ConsumerCount);

        var consumerTasks = Enumerable.Range(0, _options.ConsumerCount)
            .Select(i => ConsumeFromQueueAsync(i, ct))
            .ToList();

        var flushTimerTask = FlushTimerAsync(ct);

        await Task.WhenAll(consumerTasks.Append(flushTimerTask));
    }

    private async Task ConsumeFromQueueAsync(int consumerId, CancellationToken ct)
    {
        _logger.LogInformation("Consumer {ConsumerId} starting", consumerId);

        try
        {
            await foreach (var envelope in _messageBus.SubscribeAsync<LogEntryBatch>(_options.SearchIndexerQueue, ct))
            {
                var sw = Stopwatch.StartNew();
                var batch = envelope.Payload;
                var validEntries = new List<LogEntry>();

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
                            var idempotencyKey = $"search:{entry.Id}";
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

                            lock (validEntries)
                            {
                                validEntries.Add(entry);
                            }
                        }
                        catch (Exception ex)
                        {
                            _metrics.RejectedCounter.Add(1, [new("tenant_id", entry.TenantId)]);
                            _logger.LogError(ex, "Failed to check idempotency for entry {EntryId}", entry.Id);
                        }
                    });

                foreach (var entry in validEntries)
                {
                    _tenantBuffers.AddOrUpdate(
                        entry.TenantId,
                        _ => new List<LogEntry> { entry },
                        (_, list) =>
                        {
                            lock (list)
                            {
                                list.Add(entry);
                            }
                            return list;
                        });
                }

                await CheckAndFlushFullBuffersAsync(ct);
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

    private async Task FlushTimerAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.FlushIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _logger.LogDebug("Flushing {TenantCount} tenant buffers on timer", _tenantBuffers.Count);
                await FlushAllBuffersAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("FlushTimer stopped, flushing remaining entries");
            await FlushAllBuffersAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FlushTimer failed");
            throw;
        }
    }

    private async Task CheckAndFlushFullBuffersAsync(CancellationToken ct)
    {
        var fullTenants = _tenantBuffers
            .Where(kvp => kvp.Value.Count >= _options.BatchSize)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var tenantId in fullTenants)
        {
            await FlushTenantBufferAsync(tenantId, ct);
        }
    }

    private async Task FlushAllBuffersAsync(CancellationToken ct)
    {
        var tenants = _tenantBuffers.Keys.ToList();
        foreach (var tenantId in tenants)
        {
            await FlushTenantBufferAsync(tenantId, ct);
        }
    }

    private async Task FlushTenantBufferAsync(string tenantId, CancellationToken ct)
    {
        await _flushLock.WaitAsync(ct);
        try
        {
            if (!_tenantBuffers.TryRemove(tenantId, out var buffer) || buffer.Count == 0)
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

            var sw = Stopwatch.StartNew();

            try
            {
                await ResiliencePipelines.ForSearch<object?>().ExecuteAsync(async _ =>
                {
                    await _searchIndexer.IndexBatchAsync(snapshot, ct);
                    return (object?)null;
                }, ct);

                _metrics.SearchIndexed.Add(snapshot.Count, [new("tenant_id", tenantId)]);
                _metrics.IndexLatency.Record(sw.Elapsed.TotalMilliseconds, [new("tenant_id", tenantId)]);

                _logger.LogInformation("Indexed {Count} entries for tenant {TenantId}", snapshot.Count, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index {Count} entries for tenant {TenantId}", snapshot.Count, tenantId);

                foreach (var entry in snapshot)
                {
                    if (_tenantBuffers.TryGetValue(tenantId, out var list))
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
        finally
        {
            _flushLock.Release();
        }
    }
}
