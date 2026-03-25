namespace Logs2Obs.Core.Handlers;

using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Exceptions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Query;
using MediatR;
using Microsoft.Extensions.Logging;

public class ExecuteSqlQueryHandler(
    IQueryEngine queryEngine,
    ISqlSafetyValidator sqlSafetyValidator,
    QueryTierRouter tierRouter,
    IMetadataStore metadataStore,
    ILogger<ExecuteSqlQueryHandler> logger)
    : IRequestHandler<ExecuteSqlQuery, QuerySubmitResult>
{
    private const string TenantSettingsTable = "tenant_settings";
    private const double MaxScanGbHardLimit = 100.0;
    private const int DefaultHotRetentionDays = 7;
    private const int DefaultWarmRetentionDays = 90;

    private readonly IQueryEngine _queryEngine = queryEngine;
    private readonly ISqlSafetyValidator _sqlSafetyValidator = sqlSafetyValidator;
    private readonly QueryTierRouter _tierRouter = tierRouter;
    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly ILogger<ExecuteSqlQueryHandler> _logger = logger;

    public async Task<QuerySubmitResult> Handle(ExecuteSqlQuery command, CancellationToken ct)
    {
        _logger.LogInformation("Executing SQL query for tenant {TenantId}", command.TenantId);

        _sqlSafetyValidator.Validate(command.Sql);

        var parsedQuery = SqlParser.Parse(command.Sql);
        var tenantSettings = await GetTenantSettingsAsync(command.TenantId, ct);
        var tierDecision = _tierRouter.Route(parsedQuery, tenantSettings);

        var estimate = await _queryEngine.EstimateCostAsync(command.Sql, ct);
        if (estimate.EstimatedScanGb > MaxScanGbHardLimit)
            throw new QueryGuardException($"Estimated scan {estimate.EstimatedScanGb:F2} GB exceeds hard limit {MaxScanGbHardLimit:F2} GB.");

        if (estimate.EstimatedCostUsd > command.ConfirmCostIfAboveUsd)
        {
            return new QuerySubmitResult
            {
                Status = QueryStatus.PendingCostConfirmation,
                Estimate = estimate
            };
        }

        QuerySubmitResult result;
        if (tierDecision.Tier == QueryTier.CrossTier && tierDecision.SubQueries is { Count: > 0 })
        {
            var tasks = tierDecision.SubQueries
                .Select(subQuery => _queryEngine.SubmitAsync(command.TenantId, BuildSubQuerySql(command.Sql, subQuery), ct))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            result = MergeResults(results);
        }
        else
        {
            result = await _queryEngine.SubmitAsync(command.TenantId, command.Sql, ct);
        }

        return result with { Estimate = estimate };
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
