namespace Logs2Obs.Adapters.Aws.QueryEngine;

using System.Text.Json;
using Amazon.Athena;
using Amazon.Athena.Model;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Options;

using AthenaQueryExecution = Amazon.Athena.Model.QueryExecution;

public sealed class AthenaQueryEngine(
    IAmazonAthena athena,
    IOptions<AwsAdaptersOptions> options,
    ISqlSafetyValidator safetyValidator) : IQueryEngine
{
    private readonly AthenaOptions _opts = options.Value.Athena;

    public async Task<QuerySubmitResult> SubmitAsync(string tenantId, string sql, CancellationToken ct = default)
    {
        safetyValidator.Validate(sql);

        var pipeline = ResiliencePipelines.ForExternalIo<QuerySubmitResult>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var request = new StartQueryExecutionRequest
            {
                QueryString = sql,
                WorkGroup = _opts.WorkGroup,
                QueryExecutionContext = new QueryExecutionContext
                {
                    Database = _opts.Database
                },
                ResultConfiguration = new ResultConfiguration
                {
                    OutputLocation = _opts.OutputLocation
                }
            };

            var response = await athena.StartQueryExecutionAsync(request, token).ConfigureAwait(false);
            return new QuerySubmitResult
            {
                ExecutionId = response.QueryExecutionId,
                Status = QueryStatus.Running
            };
        }, ct).ConfigureAwait(false);
    }

    public async Task<Logs2Obs.Core.Models.QueryExecution> GetResultAsync(string executionId, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<Logs2Obs.Core.Models.QueryExecution>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var execRequest = new GetQueryExecutionRequest { QueryExecutionId = executionId };
            var execResponse = await athena.GetQueryExecutionAsync(execRequest, token).ConfigureAwait(false);

            AthenaQueryExecution execution = execResponse.QueryExecution;
            var state = execution.Status.State?.Value;
            var status = MapStatus(state);
            var completedAt = ToDateTimeOffset(execution.Status.CompletionDateTime);
            var resultLocation = execution.ResultConfiguration?.OutputLocation;

            if (status == QueryStatus.Completed)
            {
                var result = await ReadResultsAsync(executionId, token).ConfigureAwait(false);
                resultLocation = JsonSerializer.Serialize(result);
            }

            return new Logs2Obs.Core.Models.QueryExecution
            {
                ExecutionId = executionId,
                TenantId = string.Empty,
                Sql = execution.Query ?? string.Empty,
                Status = status,
                StartedAt = ToDateTimeOffset(execution.Status.SubmissionDateTime) ?? DateTimeOffset.UtcNow,
                CompletedAt = completedAt,
                ResultLocation = resultLocation,
                ErrorMessage = execution.Status.StateChangeReason
            };
        }, ct).ConfigureAwait(false);
    }

    public Task<QueryCostEstimate> EstimateCostAsync(string sql, CancellationToken ct = default)
    {
        var estimate = new QueryCostEstimate
        {
            EstimatedScanGb = 1.0,
            EstimatedCostUsd = 0.005,
            ConfidenceLevel = "low"
        };
        return Task.FromResult(estimate);
    }

    public async Task CancelAsync(string executionId, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var request = new StopQueryExecutionRequest { QueryExecutionId = executionId };
            await athena.StopQueryExecutionAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    private static QueryStatus MapStatus(string? state)
    {
        return state?.ToUpperInvariant() switch
        {
            "QUEUED" => QueryStatus.Pending,
            "RUNNING" => QueryStatus.Running,
            "SUCCEEDED" => QueryStatus.Completed,
            "CANCELLED" => QueryStatus.Failed,
            "FAILED" => QueryStatus.Failed,
            _ => QueryStatus.Pending
        };
    }

    private async Task<IReadOnlyList<Dictionary<string, object?>>> ReadResultsAsync(
        string executionId,
        CancellationToken ct)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<GetQueryResultsResponse>();
        var results = new List<Dictionary<string, object?>>();

        string? nextToken = null;
        List<ColumnInfo>? columns = null;
        do
        {
            var request = new GetQueryResultsRequest
            {
                QueryExecutionId = executionId,
                NextToken = nextToken
            };

            var response = await pipeline.ExecuteAsync(
                async token => await athena.GetQueryResultsAsync(request, token).ConfigureAwait(false), ct)
                .ConfigureAwait(false);

            columns ??= response.ResultSet.ResultSetMetadata.ColumnInfo.ToList();
            var rows = response.ResultSet.Rows;
            var startIndex = results.Count == 0 ? 1 : 0;
            for (var i = startIndex; i < rows.Count; i++)
            {
                var row = rows[i];
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < columns.Count; c++)
                {
                    var name = columns[c].Name;
                    var value = row.Data.Count > c ? row.Data[c].VarCharValue : null;
                    dict[name] = value;
                }
                results.Add(dict);
            }

            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return results;
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        var utc = DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        return new DateTimeOffset(utc);
    }
}
