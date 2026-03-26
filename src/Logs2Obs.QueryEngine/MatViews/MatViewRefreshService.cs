namespace Logs2Obs.QueryEngine.MatViews;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.MatViews;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

public sealed class MatViewRefreshService(
    IMatViewEngine matViewEngine,
    IQueryEngine queryEngine,
    ILogger<MatViewRefreshService> logger)
{
    private const int MaxRefreshParallelism = 3;

    private readonly IMatViewEngine _matViewEngine = matViewEngine;
    private readonly IQueryEngine _queryEngine = queryEngine;
    private readonly ILogger<MatViewRefreshService> _logger = logger;
    private readonly ResiliencePipeline<QuerySubmitResult> _queryPipeline = ResiliencePipelines.ForExternalIo<QuerySubmitResult>();

    public async Task RefreshAllForTenantAsync(string tenantId, CancellationToken ct)
    {
        var escapedTenant = tenantId.Replace("'", "''", StringComparison.Ordinal);

        await Parallel.ForEachAsync(
            StandardMatViews.All,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxRefreshParallelism,
                CancellationToken = ct
            },
            async (view, token) =>
            {
                try
                {
                    var sql = view.Sql.Replace("{TENANT_FILTER}", $"tenantId='{escapedTenant}'", StringComparison.Ordinal);
                    var result = await _queryPipeline.ExecuteAsync(
                        async t => await _queryEngine.SubmitAsync(tenantId, sql, t), token);

                    if (result.Status != Logs2Obs.Core.Models.QueryStatus.Completed)
                    {
                        _logger.LogWarning(
                            "MatView {ViewName} refresh returned status {Status} for tenant {TenantId}",
                            view.Name, result.Status, tenantId);
                        return;
                    }

                    await _matViewEngine.RefreshAsync(tenantId, view.Name, token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MatView {ViewName} refresh failed for tenant {TenantId}", view.Name, tenantId);
                    throw;
                }
            });
    }
}
