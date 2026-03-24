namespace Logs2Obs.Core.MatViews;

using Logs2Obs.Core.Models;

/// <summary>Standard pre-aggregated materialized view definitions from Section 22.2.</summary>
public static class StandardMatViews
{
    /// <summary>Count of Error/Fatal log entries per service per minute.</summary>
    public static MatViewDefinition ErrorRatePerMinute => new()
    {
        Name        = "error_rate_per_minute",
        Description = "Count of Error/Fatal log entries per service per minute",
        Sql = """
            SELECT
              date_trunc('minute', timestamp) AS minute_bucket,
              sourceid AS service,
              COUNT(*) AS error_count
            FROM logs
            WHERE logtype = 'Error' AND level IN ('Error', 'Fatal')
              AND {TENANT_FILTER}
              AND timestamp >= NOW() - INTERVAL '2' MINUTE
            GROUP BY 1, 2
            """,
        RefreshIntervalSeconds = 60,
        StorageTarget          = MatViewStorage.Redis,
        RetentionMinutes       = 1440,
        SuggestedGraphType     = GraphType.AreaChart
    };

    /// <summary>P50/P95/P99 latency per service per 5-minute bucket.</summary>
    public static MatViewDefinition LatencyP99 => new()
    {
        Name        = "latency_p99_per_service",
        Description = "P50/P95/P99 latency per service per 5-minute bucket",
        Sql = """
            SELECT
              date_trunc('minute', timestamp) - INTERVAL MOD(MINUTE(timestamp), 5) MINUTE AS bucket,
              sourceid AS service,
              APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.50) AS p50_ms,
              APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.95) AS p95_ms,
              APPROX_PERCENTILE(CAST(metric_value AS DOUBLE), 0.99) AS p99_ms
            FROM logs
            WHERE logtype = 'Metric' AND category = 'http-latency'
              AND {TENANT_FILTER}
              AND timestamp >= NOW() - INTERVAL '10' MINUTE
            GROUP BY 1, 2
            """,
        RefreshIntervalSeconds = 300,
        StorageTarget          = MatViewStorage.Redis,
        RetentionMinutes       = 2880,
        SuggestedGraphType     = GraphType.LineChart
    };

    /// <summary>Total log entries per log type per minute.</summary>
    public static MatViewDefinition LogVolumeByType => new()
    {
        Name        = "log_volume_by_type",
        Description = "Total log entries per log type per minute",
        Sql = """
            SELECT
              date_trunc('minute', timestamp) AS minute_bucket,
              logtype,
              COUNT(*) AS entry_count
            FROM logs
            WHERE {TENANT_FILTER}
              AND timestamp >= NOW() - INTERVAL '2' MINUTE
            GROUP BY 1, 2
            """,
        RefreshIntervalSeconds = 60,
        StorageTarget          = MatViewStorage.Redis,
        RetentionMinutes       = 1440,
        SuggestedGraphType     = GraphType.StackedAreaChart
    };

    /// <summary>All standard materialized view definitions.</summary>
    public static IReadOnlyList<MatViewDefinition> All =>
    [
        ErrorRatePerMinute,
        LatencyP99,
        LogVolumeByType
    ];
}
