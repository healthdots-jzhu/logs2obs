namespace Logs2Obs.QueryEngine.Services;

using System.Diagnostics;
using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Exceptions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Query;
using Logs2Obs.Core.Resilience;
using Logs2Obs.QueryEngine.Telemetry;
using Microsoft.Extensions.Logging;
using Polly;

public sealed class QueryService(
    IQueryEngine queryEngine,
    ISqlSafetyValidator safetyValidator,
    QueryTierRouter tierRouter,
    IMetadataStore metadataStore,
    QueryEngineMetrics metrics,
    ILogger<QueryService> logger)
{
    private const string TenantSettingsTable = "tenant_settings";
    private const double MaxScanGbHardLimit = 100.0;
    private const int DefaultHotRetentionDays = 7;
    private const int DefaultWarmRetentionDays = 90;

    private readonly IQueryEngine _queryEngine = queryEngine;
    private readonly ISqlSafetyValidator _safetyValidator = safetyValidator;
    private readonly QueryTierRouter _tierRouter = tierRouter;
    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly QueryEngineMetrics _metrics = metrics;
    private readonly ILogger<QueryService> _logger = logger;
    private readonly ResiliencePipeline<QuerySubmitResult> _submitPipeline =
        ResiliencePipelines.ForExternalIo<QuerySubmitResult>();

    public async Task<QuerySubmitResult> ExecuteAsync(ExecuteSqlQuery cmd, CancellationToken ct)
    {
        _logger.LogInformation("Executing SQL query for tenant {TenantId}", cmd.TenantId);

        _safetyValidator.Validate(cmd.Sql);

        var parsedQuery = SqlParser.Parse(cmd.Sql);
        var tenantSettings = await GetTenantSettingsAsync(cmd.TenantId, ct);
        var tierDecision = _tierRouter.Route(parsedQuery, tenantSettings);

        var estimate = await _queryEngine.EstimateCostAsync(cmd.Sql, ct);
        if (estimate.EstimatedScanGb > MaxScanGbHardLimit)
        {
            _metrics.RecordRejected(cmd.TenantId, "scan_limit");
            throw new QueryGuardException($"Estimated scan {estimate.EstimatedScanGb:F2} GB exceeds hard limit {MaxScanGbHardLimit:F2} GB.");
        }

        if (estimate.EstimatedCostUsd > cmd.ConfirmCostIfAboveUsd)
        {
            _metrics.RecordCostConfirmationRequired(cmd.TenantId);
            return new QuerySubmitResult
            {
                Status = QueryStatus.PendingCostConfirmation,
                Estimate = estimate
            };
        }

        var stopwatch = Stopwatch.StartNew();
        QuerySubmitResult result;
        if (tierDecision.Tier == QueryTier.CrossTier && tierDecision.SubQueries is { Count: > 0 })
        {
            var tasks = tierDecision.SubQueries
                .Select(subQuery => SubmitWithRetryAsync(cmd.TenantId, subQuery.Tier, BuildSubQuerySql(cmd.Sql, subQuery), ct))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            result = MergeResults(results);
        }
        else
        {
            result = await SubmitWithRetryAsync(cmd.TenantId, tierDecision.Tier, cmd.Sql, ct);
        }

        stopwatch.Stop();
        _metrics.RecordDuration(tierDecision.Tier, stopwatch.Elapsed.TotalMilliseconds);

        return result with { Estimate = estimate };
    }

    private async Task<QuerySubmitResult> SubmitWithRetryAsync(string tenantId, QueryTier tier, string sql, CancellationToken ct)
    {
        _metrics.RecordSubmitted(tenantId, tier);
        var result = await _submitPipeline.ExecuteAsync(
            async token => await _queryEngine.SubmitAsync(tenantId, sql, token), ct);

        if (result.Status == QueryStatus.Completed)
            _metrics.RecordCompleted(tenantId, tier);

        return result;
    }

    private async Task<TenantSettings> GetTenantSettingsAsync(string tenantId, CancellationToken ct)
    {
        var stored = await _metadataStore.GetAsync<TenantSettings>(TenantSettingsTable, tenantId, ct);
        if (stored is null)
        {
            return new TenantSettings
            {
                TenantId = tenantId,
                Name = tenantId,
                HotRetentionDays = DefaultHotRetentionDays,
                WarmRetentionDays = DefaultWarmRetentionDays
            };
        }

        var hot = stored.HotRetentionDays > 0 ? stored.HotRetentionDays : DefaultHotRetentionDays;
        var warm = stored.WarmRetentionDays > 0 ? stored.WarmRetentionDays : DefaultWarmRetentionDays;

        return stored with { HotRetentionDays = hot, WarmRetentionDays = warm };
    }

    private static string BuildSubQuerySql(string sql, SubQuery subQuery)
    {
        var filter = $"timestamp >= '{subQuery.From:O}' AND timestamp < '{subQuery.To:O}'";
        return AppendFilter(sql, filter);
    }

    private static string AppendFilter(string sql, string filter)
    {
        var trimmed = sql.Trim().TrimEnd(';');
        var upper = trimmed.ToUpperInvariant();
        var orderIndex = upper.IndexOf(" ORDER BY ", StringComparison.Ordinal);
        var limitIndex = upper.IndexOf(" LIMIT ", StringComparison.Ordinal);
        var insertIndex = trimmed.Length;

        if (orderIndex >= 0 && limitIndex >= 0)
            insertIndex = Math.Min(orderIndex, limitIndex);
        else if (orderIndex >= 0)
            insertIndex = orderIndex;
        else if (limitIndex >= 0)
            insertIndex = limitIndex;

        var head = trimmed[..insertIndex].TrimEnd();
        var tail = trimmed[insertIndex..];
        var hasWhere = upper.Contains(" WHERE ", StringComparison.Ordinal);
        var clause = hasWhere ? $" AND {filter} " : $" WHERE {filter} ";

        return $"{head}{clause}{tail}".Trim();
    }

    private static QuerySubmitResult MergeResults(IReadOnlyList<QuerySubmitResult> results)
    {
        var status = ResolveStatus(results);
        var executionId = string.Join(",", results.Select(r => r.ExecutionId).Where(id => !string.IsNullOrWhiteSpace(id)));
        var location = TryMergeResultLocations(results);

        return new QuerySubmitResult
        {
            ExecutionId = string.IsNullOrWhiteSpace(executionId) ? null : executionId,
            Status = status,
            ResultLocation = location
        };
    }

    private static QueryStatus ResolveStatus(IReadOnlyList<QuerySubmitResult> results)
    {
        if (results.Any(r => r.Status == QueryStatus.Failed))
            return QueryStatus.Failed;
        if (results.Any(r => r.Status == QueryStatus.PendingCostConfirmation))
            return QueryStatus.PendingCostConfirmation;
        if (results.All(r => r.Status == QueryStatus.Completed))
            return QueryStatus.Completed;
        if (results.Any(r => r.Status == QueryStatus.Running))
            return QueryStatus.Running;
        return QueryStatus.Pending;
    }

    private static string? TryMergeResultLocations(IReadOnlyList<QuerySubmitResult> results)
    {
        if (results.Any(r => string.IsNullOrWhiteSpace(r.ResultLocation)))
            return null;

        var merged = new List<JsonElement>();
        foreach (var result in results)
        {
            try
            {
                using var doc = JsonDocument.Parse(result.ResultLocation!);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var element in doc.RootElement.EnumerateArray())
                    merged.Add(element.Clone());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return JsonSerializer.Serialize(merged);
    }
}
