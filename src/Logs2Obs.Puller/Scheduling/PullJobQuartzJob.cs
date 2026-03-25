namespace Logs2Obs.Puller.Scheduling;

using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Puller.Options;
using Logs2Obs.Puller.Services;
using Logs2Obs.Puller.Telemetry;
using Logs2Obs.Worker.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

public sealed class PullJobQuartzJob : IJob
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageBus _messageBus;
    private readonly IMetadataStore _metadataStore;
    private readonly PullerMetrics _metrics;
    private readonly ILogger<PullJobQuartzJob> _logger;

    public PullJobQuartzJob(
        IServiceProvider serviceProvider,
        IMessageBus messageBus,
        IMetadataStore metadataStore,
        PullerMetrics metrics,
        ILogger<PullJobQuartzJob> logger)
    {
        _serviceProvider = serviceProvider;
        _messageBus = messageBus;
        _metadataStore = metadataStore;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var dataMap = context.JobDetail.JobDataMap;
        if (!dataMap.TryGetValue("config", out var configObj) || configObj is null)
        {
            throw new InvalidOperationException("Missing config in job data");
        }

        var config = configObj switch
        {
            PullJobConfig cfg => cfg,
            string json => JsonSerializer.Deserialize<PullJobConfig>(json) ?? throw new InvalidOperationException("Failed to deserialize config"),
            _ => throw new InvalidOperationException("Invalid config in job data")
        };

        var connectorKey = config.ConnectorType.ToString();
        var connector = _serviceProvider.GetRequiredKeyedService<IPullConnector>(connectorKey);
        var options = _serviceProvider.GetRequiredService<IOptions<PullerOptions>>().Value;

        var startTime = DateTimeOffset.UtcNow;
        var state = await connector.GetStateAsync(config.JobId, ct);
        var since = config.LastRunAt ?? DateTimeOffset.UtcNow.AddHours(-1);

        if (state != null && state.TryGetValue("lastEventTimestamp", out var lastEventTs))
        {
            since = DateTimeOffset.Parse(lastEventTs);
        }
        else if (state != null && state.TryGetValue("lastPullAt", out var lastPullAt))
        {
            since = DateTimeOffset.Parse(lastPullAt);
        }

        var batch = new List<LogEntry>(options.BatchSize);
        var totalEntries = 0L;

        try
        {
            _logger.LogInformation("Starting pull job {JobId} for tenant {TenantId}.", config.JobId, config.TenantId);
            await foreach (var entry in connector.PullAsync(config, since, ct))
            {
                batch.Add(entry);
                totalEntries++;

                if (batch.Count >= options.BatchSize)
                {
                    var batchId = Guid.NewGuid().ToString("N");
                    var entryBatch = new LogEntryBatch(batch.ToArray(), config.TenantId, batchId);
                    await _messageBus.PublishAsync(options.StorageWriterQueue, entryBatch, ct);
                    _metrics.RecordBatchPublished(config.TenantId);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                var batchId = Guid.NewGuid().ToString("N");
                var entryBatch = new LogEntryBatch(batch.ToArray(), config.TenantId, batchId);
                await _messageBus.PublishAsync(options.StorageWriterQueue, entryBatch, ct);
                _metrics.RecordBatchPublished(config.TenantId);
            }

            var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
            var recordedEntries = totalEntries > int.MaxValue ? int.MaxValue : (int)totalEntries;
            _metrics.RecordEntriesPulled(recordedEntries, config.TenantId, connectorKey);
            _metrics.RecordPullDuration(elapsed, connectorKey);

            var updatedConfig = config with { LastRunAt = DateTimeOffset.UtcNow };
            await _metadataStore.PutAsync("pulljob", PullJobStateRecord.FromConfig(updatedConfig), ct);
            _logger.LogInformation("Completed pull job {JobId} with {EntryCount} entries.", config.JobId, totalEntries);
        }
        catch (Exception ex)
        {
            _metrics.RecordJobError(config.TenantId, connectorKey, ex.GetType().Name);
            _logger.LogError(ex, "Pull job {JobId} failed.", config.JobId);
            throw;
        }
    }
}
