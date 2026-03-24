namespace Logs2Obs.Adapters.Local.QueryEngine;

using System.Text.Json;
using DuckDB.NET.Data;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Adapters.Local.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class DuckDbQueryEngine(
    IOptions<DuckDbOptions> options,
    ILogger<DuckDbQueryEngine> logger,
    ISqlSafetyValidator safetyValidator) : IQueryEngine
{
    private readonly DuckDbOptions _opts = options.Value;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, QueryExecution> _executions = new();

    public async Task<QuerySubmitResult> SubmitAsync(string tenantId, string sql, CancellationToken ct = default)
    {
        safetyValidator.Validate(sql);

        var executionId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;

        var pipeline = ResiliencePipelines.ForSearch<QuerySubmitResult>();
        return await pipeline.ExecuteAsync(async token =>
        {
            try
            {
                var connStr = _opts.DatabasePath == ":memory:"
                    ? "DataSource=:memory:"
                    : $"DataSource={_opts.DatabasePath}";

                await using var conn = new DuckDBConnection(connStr);
                await conn.OpenAsync(token);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = _opts.MaxQueryTimeoutSeconds;

                var rows = new List<Dictionary<string, object?>>();
                await using var reader = await cmd.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }

                var resultJson = JsonSerializer.Serialize(rows);
                var exec = new QueryExecution
                {
                    ExecutionId = executionId,
                    TenantId = tenantId,
                    Sql = sql,
                    Status = QueryStatus.Completed,
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ResultLocation = resultJson
                };
                _executions[executionId] = exec;

                logger.LogInformation("DuckDB query {ExecutionId} completed: {RowCount} rows", executionId, rows.Count);
                return new QuerySubmitResult
                {
                    ExecutionId = executionId,
                    Status = QueryStatus.Completed,
                    ResultLocation = resultJson
                };
            }
            catch (Exception ex)
            {
                var exec = new QueryExecution
                {
                    ExecutionId = executionId,
                    TenantId = tenantId,
                    Sql = sql,
                    Status = QueryStatus.Failed,
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ErrorMessage = ex.Message
                };
                _executions[executionId] = exec;
                logger.LogError(ex, "DuckDB query {ExecutionId} failed", executionId);
                return new QuerySubmitResult { ExecutionId = executionId, Status = QueryStatus.Failed };
            }
        }, ct);
    }

    public Task<QueryExecution> GetResultAsync(string executionId, CancellationToken ct = default)
    {
        if (_executions.TryGetValue(executionId, out var exec))
            return Task.FromResult(exec);

        return Task.FromResult(new QueryExecution
        {
            ExecutionId = executionId,
            TenantId = string.Empty,
            Sql = string.Empty,
            Status = QueryStatus.Failed,
            StartedAt = DateTimeOffset.UtcNow,
            ErrorMessage = $"Execution {executionId} not found."
        });
    }

    public Task<QueryCostEstimate> EstimateCostAsync(string sql, CancellationToken ct = default)
    {
        var estimate = new QueryCostEstimate
        {
            EstimatedScanGb = 0.001,
            EstimatedCostUsd = 0.000005,
            ConfidenceLevel = "low"
        };
        return Task.FromResult(estimate);
    }

    public Task CancelAsync(string executionId, CancellationToken ct = default)
    {
        logger.LogWarning("CancelAsync is a no-op for DuckDbQueryEngine (execution: {ExecutionId})", executionId);
        return Task.CompletedTask;
    }
}
