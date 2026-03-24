namespace Logs2Obs.Core.Abstractions;

/// <summary>Engine for managing and querying pre-aggregated materialized views.</summary>
public interface IMatViewEngine
{
    /// <summary>Triggers a refresh of the specified materialized view for the given tenant.</summary>
    Task RefreshAsync(string tenantId, string viewName, CancellationToken ct = default);

    /// <summary>Queries the specified materialized view for the given tenant and time range.</summary>
    Task<MatViewResult> QueryAsync(string tenantId, string viewName, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Returns true if the specified view is fresh (within its refresh interval).</summary>
    Task<bool> IsFreshAsync(string tenantId, string viewName, CancellationToken ct = default);
}
