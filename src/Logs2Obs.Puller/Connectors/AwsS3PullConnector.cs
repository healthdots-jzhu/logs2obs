namespace Logs2Obs.Puller.Connectors;

using System.Runtime.CompilerServices;
using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

public sealed class AwsS3PullConnector : IPullConnector
{
    private readonly IObjectStore _objectStore;
    private readonly IMetadataStore _metadataStore;
    private readonly ILogger<AwsS3PullConnector> _logger;
    private readonly ResiliencePipeline _resilience;

    public AwsS3PullConnector(
        IObjectStore objectStore,
        IMetadataStore metadataStore,
        ILogger<AwsS3PullConnector> logger)
    {
        _objectStore = objectStore;
        _metadataStore = metadataStore;
        _logger = logger;
        _resilience = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500)
            })
            .Build();
    }

    public async IAsyncEnumerable<LogEntry> PullAsync(
        PullJobConfig config,
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var bucket = config.ConnectorConfig.GetValueOrDefault("bucket") ?? throw new InvalidOperationException("Missing bucket config");
        var prefix = config.ConnectorConfig.GetValueOrDefault("prefix") ?? string.Empty;
        var objectPrefix = BuildObjectPrefix(bucket, prefix);

        var state = await GetStateAsync(config.JobId, ct);
        var lastProcessedKey = state?.GetValueOrDefault("lastProcessedKey");

        var keys = await _resilience.ExecuteAsync(async token =>
        {
            var list = new List<string>();
            await foreach (var key in _objectStore.ListAsync(objectPrefix, token))
            {
                list.Add(key);
            }
            return list;
        }, ct);

        keys.Sort(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (lastProcessedKey != null && string.Compare(key, lastProcessedKey, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            await foreach (var entry in ProcessFileAsync(key, config.TenantId, ct))
            {
                yield return entry;
            }

            var newState = new Dictionary<string, string> { ["lastProcessedKey"] = key };
            await SaveStateAsync(config.JobId, newState, ct);
        }
    }

    private async IAsyncEnumerable<LogEntry> ProcessFileAsync(
        string key,
        string tenantId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = await _resilience.ExecuteAsync(
            async token => await _objectStore.ReadAsync(key, token),
            ct);
        if (stream is null)
        {
            _logger.LogWarning("Object {Key} not found while pulling logs.", key);
            yield break;
        }

        await using var readStream = stream;

        if (key.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var gzipStream = new System.IO.Compression.GZipStream(readStream, System.IO.Compression.CompressionMode.Decompress);
            await foreach (var entry in ParseNdJsonAsync(gzipStream, tenantId, ct))
            {
                yield return entry;
            }
        }
        else
        {
            await foreach (var entry in ParseNdJsonAsync(readStream, tenantId, ct))
            {
                yield return entry;
            }
        }
    }

    private static async IAsyncEnumerable<LogEntry> ParseNdJsonAsync(
        Stream stream,
        string tenantId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            LogEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<LogEntry>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry != null)
            {
                yield return entry with { TenantId = tenantId };
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetStateAsync(string jobId, CancellationToken ct = default)
    {
        var record = await _metadataStore.GetAsync<PullStateRecord>("pullstate", PullStateRecord.BuildKey(jobId), ct);
        return record?.State;
    }

    public async Task SaveStateAsync(string jobId, IReadOnlyDictionary<string, string> state, CancellationToken ct = default)
    {
        var record = new PullStateRecord
        {
            Key = PullStateRecord.BuildKey(jobId),
            State = new Dictionary<string, string>(state)
        };
        await _metadataStore.PutAsync("pullstate", record, ct);
    }

    private static string BuildObjectPrefix(string bucket, string prefix)
    {
        var sanitizedBucket = bucket.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return sanitizedBucket;
        }
        return $"{sanitizedBucket}/{prefix.TrimStart('/')}";
    }
}
