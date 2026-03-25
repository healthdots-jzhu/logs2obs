namespace Logs2Obs.QueryEngine.Services;

using System.Runtime.CompilerServices;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;

public sealed class SavedQueryService(
    IMetadataStore metadataStore,
    ILogger<SavedQueryService> logger)
{
    private const string TableName = "saved_queries";

    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly ILogger<SavedQueryService> _logger = logger;

    public async Task<SavedQuery?> GetAsync(string queryId, string tenantId, CancellationToken ct)
    {
        var key = BuildKey(tenantId, queryId);
        var record = await _metadataStore.GetAsync<SavedQueryRecord>(TableName, key, ct);
        return record is null ? null : Map(record);
    }

    public async Task SaveAsync(SavedQuery query, CancellationToken ct)
    {
        var record = new SavedQueryRecord
        {
            Key = BuildKey(query.TenantId, query.QueryId),
            QueryId = query.QueryId,
            TenantId = query.TenantId,
            Name = query.Name,
            Sql = query.Sql,
            Description = query.Description,
            CreatedAt = query.CreatedAt,
            UpdatedAt = query.UpdatedAt
        };

        await _metadataStore.PutAsync(TableName, record, ct);
        _logger.LogInformation("Saved query {QueryId} for tenant {TenantId}", query.QueryId, query.TenantId);
    }

    public async IAsyncEnumerable<SavedQuery> ListAsync(string tenantId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in _metadataStore.QueryAsync<SavedQueryRecord>(TableName, r => r.TenantId == tenantId, ct))
            yield return Map(record);
    }

    public Task DeleteAsync(string queryId, string tenantId, CancellationToken ct)
    {
        var key = BuildKey(tenantId, queryId);
        _logger.LogInformation("Deleting saved query {QueryId} for tenant {TenantId}", queryId, tenantId);
        return _metadataStore.DeleteAsync(TableName, key, ct);
    }

    private static string BuildKey(string tenantId, string queryId) =>
        $"savedquery:{tenantId}:{queryId}";

    private static SavedQuery Map(SavedQueryRecord record) => new()
    {
        QueryId = record.QueryId,
        TenantId = record.TenantId,
        Name = record.Name,
        Sql = record.Sql,
        Description = record.Description,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };

    private sealed record SavedQueryRecord
    {
        public required string Key { get; init; }
        public required string QueryId { get; init; }
        public required string TenantId { get; init; }
        public required string Name { get; init; }
        public required string Sql { get; init; }
        public string? Description { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }
}
