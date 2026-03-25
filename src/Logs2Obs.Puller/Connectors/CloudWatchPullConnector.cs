namespace Logs2Obs.Puller.Connectors;

using System.Runtime.CompilerServices;
using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

public sealed class CloudWatchPullConnector : IPullConnector
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMetadataStore _metadataStore;
    private readonly ILogger<CloudWatchPullConnector> _logger;
    private readonly ResiliencePipeline _resilience;

    public CloudWatchPullConnector(
        IHttpClientFactory httpClientFactory,
        IMetadataStore metadataStore,
        ILogger<CloudWatchPullConnector> logger)
    {
        _httpClientFactory = httpClientFactory;
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
        var endpoint = config.ConnectorConfig.GetValueOrDefault("endpoint") ?? throw new InvalidOperationException("Missing endpoint config");
        var logGroupName = config.ConnectorConfig.GetValueOrDefault("logGroupName") ?? throw new InvalidOperationException("Missing logGroupName config");
        _logger.LogInformation("Pulling CloudWatch logs for {LogGroupName} since {Since}.", logGroupName, since);

        var httpClient = _httpClientFactory.CreateClient();
        var requestUrl = $"{endpoint.TrimEnd('/')}/logs?logGroupName={Uri.EscapeDataString(logGroupName)}&since={Uri.EscapeDataString(since.ToString("O"))}";

        using var response = await _resilience.ExecuteAsync(async token =>
            await httpClient.GetAsync(requestUrl, token), ct);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        var entries = await JsonSerializer.DeserializeAsync<List<LogEntry>>(responseStream, cancellationToken: ct)
            ?? new List<LogEntry>();

        long? maxTimestamp = null;

        foreach (var entry in entries)
        {
            var normalized = entry with { TenantId = config.TenantId };
            if (maxTimestamp == null || normalized.TimestampUnixMs > maxTimestamp)
            {
                maxTimestamp = normalized.TimestampUnixMs;
            }

            yield return normalized;
        }

        if (maxTimestamp.HasValue)
        {
            var newState = new Dictionary<string, string>
            {
                ["lastEventTimestamp"] = DateTimeOffset.FromUnixTimeMilliseconds(maxTimestamp.Value).ToString("O")
            };
            await SaveStateAsync(config.JobId, newState, ct);
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
}
