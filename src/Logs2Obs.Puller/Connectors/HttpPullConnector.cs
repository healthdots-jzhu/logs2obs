namespace Logs2Obs.Puller.Connectors;

using System.Runtime.CompilerServices;
using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

public sealed class HttpPullConnector : IPullConnector
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMetadataStore _metadataStore;
    private readonly ILogger<HttpPullConnector> _logger;
    private readonly ResiliencePipeline _resilience;

    public HttpPullConnector(
        IHttpClientFactory httpClientFactory,
        IMetadataStore metadataStore,
        ILogger<HttpPullConnector> logger)
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
        var url = config.ConnectorConfig.GetValueOrDefault("url") ?? throw new InvalidOperationException("Missing url config");
        var apiKey = config.ConnectorConfig.GetValueOrDefault("apiKey");

        _logger.LogInformation("Pulling logs from HTTP endpoint {Url}.", url);

        var httpClient = _httpClientFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        using var response = await _resilience.ExecuteAsync(async token =>
            await httpClient.GetAsync(url, token), ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (var entry in ParseNdJsonAsync(stream, config.TenantId, ct))
        {
            yield return entry;
        }

        var newState = new Dictionary<string, string>
        {
            ["lastPullAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        await SaveStateAsync(config.JobId, newState, ct);
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
}
