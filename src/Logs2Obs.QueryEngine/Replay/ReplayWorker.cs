namespace Logs2Obs.QueryEngine.Replay;

using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using CoreLogLevel = Logs2Obs.Core.Models.LogLevel;
using Logs2Obs.Core.Resilience;
using Logs2Obs.QueryEngine.Models;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet.Serialization;
using Polly;

public sealed class ReplayWorker(
    IMessageBus messageBus,
    IObjectStore objectStore,
    IMetadataStore metadataStore,
    IOptions<QueryEngineOptions> options,
    ILogger<ReplayWorker> logger) : BackgroundService
{
    private const string TableName = "replay-jobs";

    private readonly IMessageBus _messageBus = messageBus;
    private readonly IObjectStore _objectStore = objectStore;
    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly QueryEngineOptions _options = options.Value;
    private readonly ILogger<ReplayWorker> _logger = logger;
    private readonly ResiliencePipeline<Stream?> _readPipeline = ResiliencePipelines.ForStorage<Stream?>();
    private readonly ResiliencePipeline<object?> _publishPipeline = ResiliencePipelines.ForExternalIo<object?>();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ReplayWorker starting on queue {Queue}", _options.SystemEventsQueue);

        await foreach (var envelope in _messageBus.SubscribeAsync<ReplayStartedEvent>(_options.SystemEventsQueue, ct))
        {
            try
            {
                await ProcessReplayAsync(envelope.Payload, ct);
                await _messageBus.AcknowledgeAsync(envelope.ReceiptHandle, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Replay processing failed for job {JobId}", envelope.Payload.JobId);
                await _messageBus.DeadLetterAsync(envelope.ReceiptHandle, ex.Message, ct);
            }
        }
    }

    private async Task ProcessReplayAsync(ReplayStartedEvent evt, CancellationToken ct)
    {
        var job = await _metadataStore.GetAsync<ReplayJob>(TableName, evt.JobId, ct);
        if (job is null)
        {
            _logger.LogWarning("Replay job {JobId} not found for tenant {TenantId}", evt.JobId, evt.TenantId);
            return;
        }

        job = job with { Status = ReplayStatus.Running };
        await _metadataStore.PutAsync(TableName, job, ct);

        try
        {
            var files = await ListReplayFilesAsync(evt.TenantId, evt.From, evt.To, ct);
            var maxParallel = Math.Max(1, evt.Options.MaxParallelFiles);

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxParallel,
                    CancellationToken = ct
                },
                async (path, token) =>
                {
                    await ReplayFileAsync(evt, path, token);
                });

            job = job with { Status = ReplayStatus.Completed };
            await _metadataStore.PutAsync(TableName, job, ct);
            _logger.LogInformation("Replay job {JobId} completed with {FileCount} files", job.JobId, files.Count);
        }
        catch (Exception ex)
        {
            job = job with { Status = ReplayStatus.Failed };
            await _metadataStore.PutAsync(TableName, job, ct);
            _logger.LogError(ex, "Replay job {JobId} failed", job.JobId);
            throw;
        }
    }

    private async Task ReplayFileAsync(ReplayStartedEvent evt, string path, CancellationToken ct)
    {
        var stream = await _readPipeline.ExecuteAsync(
            async token => await _objectStore.ReadAsync(path, token),
            ct);

        if (stream is null)
        {
            _logger.LogWarning("Replay file {Path} missing for job {JobId}", path, evt.JobId);
            return;
        }

        await using (stream)
        {
            var records = await ParquetSerializer.DeserializeAsync<LogEntryParquetRecord>(stream, cancellationToken: ct);
            var entries = records.Select(r => MapEntry(r, evt.TenantId)).ToList();
            if (entries.Count == 0)
                return;

            if (!evt.Options.ReindexSearch)
                return;

            var batch = new LogEntryBatch(entries, evt.TenantId, Guid.NewGuid().ToString("N"));
            await _publishPipeline.ExecuteAsync(async token =>
            {
                await _messageBus.PublishAsync(_options.IngestQueue, batch, token);
                return (object?)null;
            }, ct);
        }
    }

    private async Task<List<string>> ListReplayFilesAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var prefixes = BuildPrefixes(tenantId, from, to).Distinct().ToList();
        var files = new List<string>();

        foreach (var prefix in prefixes)
        {
            await foreach (var key in _objectStore.ListAsync(prefix, ct))
            {
                files.Add(key);
            }
        }

        return files;
    }

    private IEnumerable<string> BuildPrefixes(string tenantId, DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = new DateTimeOffset(from.Year, from.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(to.Year, to.Month, 1, 0, 0, 0, TimeSpan.Zero);

        while (cursor <= end)
        {
            yield return $"{_options.ReplayObjectPrefix}/{tenantId}/{cursor:yyyy/MM}/";
            cursor = cursor.AddMonths(1);
        }
    }

    private static LogEntry MapEntry(LogEntryParquetRecord record, string fallbackTenantId)
    {
        var logType = Enum.TryParse(record.LogType, true, out LogType parsedType) ? parsedType : LogType.Application;
        var level = Enum.TryParse(record.Level, true, out Logs2Obs.Core.Models.LogLevel parsedLevel)
            ? parsedLevel
            : Logs2Obs.Core.Models.LogLevel.Information;
        var tags = ParseTags(record.Tags);

        return new LogEntry
        {
            Id = record.Id,
            SourceId = record.SourceId,
            LogType = logType,
            Level = level,
            Environment = record.Environment,
            Category = string.IsNullOrWhiteSpace(record.Category) ? null : record.Category,
            TimestampUnixMs = record.TimestampUnixMs,
            Message = record.Message,
            TraceId = string.IsNullOrWhiteSpace(record.TraceId) ? null : record.TraceId,
            TenantId = string.IsNullOrWhiteSpace(record.TenantId) ? fallbackTenantId : record.TenantId,
            IngestedAt = DateTimeOffset.UtcNow,
            IngestionMode = IngestionMode.Replay,
            SchemaVersion = (uint)record.SchemaVersion,
            Tags = tags
        };
    }

    private static Dictionary<string, string>? ParseTags(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(tags);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class LogEntryParquetRecord
    {
        public string Id { get; init; } = string.Empty;
        public string SourceId { get; init; } = string.Empty;
        public string LogType { get; init; } = string.Empty;
        public string Level { get; init; } = string.Empty;
        public string Environment { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public long TimestampUnixMs { get; init; }
        public string Message { get; init; } = string.Empty;
        public string TraceId { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
        public int SchemaVersion { get; init; }
        public string Tags { get; init; } = string.Empty;
    }
}
