namespace Logs2Obs.Puller.Services;

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;

public sealed class PullJobStateService
{
    private readonly IMetadataStore _metadataStore;

    public PullJobStateService(IMetadataStore metadataStore)
    {
        _metadataStore = metadataStore;
    }

    public async Task<PullJobConfig?> GetJobAsync(string jobId, CancellationToken ct = default)
    {
        await foreach (var record in _metadataStore.QueryAsync<PullJobStateRecord>("pulljob", r => r.Config.JobId == jobId, ct))
        {
            return record.Config;
        }

        return null;
    }

    public async Task SaveJobAsync(PullJobConfig config, CancellationToken ct = default)
    {
        var record = PullJobStateRecord.FromConfig(config);
        await _metadataStore.PutAsync("pulljob", record, ct);
    }

    public async IAsyncEnumerable<PullJobConfig> ListJobsAsync(
        string tenantId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var record in _metadataStore.QueryAsync<PullJobStateRecord>(
                           "pulljob",
                           r => tenantId == "*" || r.Config.TenantId == tenantId,
                           ct))
        {
            yield return record.Config;
        }
    }

    public async Task DeleteJobAsync(string jobId, CancellationToken ct = default)
    {
        var keys = new List<string>();
        await foreach (var record in _metadataStore.QueryAsync<PullJobStateRecord>("pulljob", r => r.Config.JobId == jobId, ct))
        {
            keys.Add(record.Key);
        }

        foreach (var key in keys)
        {
            await _metadataStore.DeleteAsync("pulljob", key, ct);
        }
    }
}

internal sealed record PullJobStateRecord
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("config")]
    public required PullJobConfig Config { get; init; }

    public static string BuildKey(string tenantId, string jobId) => $"pulljob:{tenantId}:{jobId}";

    public static PullJobStateRecord FromConfig(PullJobConfig config) => new()
    {
        Key = BuildKey(config.TenantId, config.JobId),
        Config = config
    };
}
