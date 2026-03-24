namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Models;

/// <summary>Cloud-agnostic search indexer abstraction for hot-tier log storage.</summary>
public interface ISearchIndexer
{
    /// <summary>Indexes a batch of log entries.</summary>
    Task IndexBatchAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct = default);

    /// <summary>Searches for log entries matching the given query string.</summary>
    Task<IReadOnlyList<LogEntry>> SearchAsync(string tenantId, string query, int limit, CancellationToken ct = default);

    /// <summary>Performs an aggregation query.</summary>
    Task<SearchAggResult> AggregateAsync(SearchAggRequest request, CancellationToken ct = default);

    /// <summary>Deletes all indexed entries for the specified tenant.</summary>
    Task DeleteByTenantAsync(string tenantId, CancellationToken ct = default);
}
