namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Models;

/// <summary>Cloud-agnostic query engine abstraction for warm/cold-tier SQL queries.</summary>
public interface IQueryEngine
{
    /// <summary>Submits a SQL query for the given tenant and returns submission result.</summary>
    Task<QuerySubmitResult> SubmitAsync(string tenantId, string sql, CancellationToken ct = default);

    /// <summary>Returns the current status of a previously submitted query execution.</summary>
    Task<QueryExecution> GetResultAsync(string executionId, CancellationToken ct = default);

    /// <summary>Estimates the cost of executing the given SQL query.</summary>
    Task<QueryCostEstimate> EstimateCostAsync(string sql, CancellationToken ct = default);

    /// <summary>Cancels the specified query execution.</summary>
    Task CancelAsync(string executionId, CancellationToken ct = default);
}
